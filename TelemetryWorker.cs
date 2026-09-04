using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace NexusDisplay;

internal readonly record struct AgentStatus(bool Connected, string? Port, string Message);

internal sealed class TelemetryWorker : IDisposable
{
    private const string EspressifUsbSerialIdentity = "VID_303A&PID_1001";
    private readonly Computer _computer;
    private readonly IHardware[] _hardware;
    private readonly IHardware[] _nonGpuHardware;
    private readonly IHardware? _cpu;
    private readonly IHardware[] _gpus;
    private readonly IHardware? _primaryGpu;
    private readonly EnergyAccumulator _energy = new();
    private readonly TelemetryPipeline<TelemetrySnapshot> _pipeline;
    private SerialPort? _serial;
    private string? _port;
    private string? _sessionNonce;
    private ProductVersion? _firmwareVersion;
    private string? _lastVerifiedPort;
    private uint _sequence;
    private int _serialBacklogTicks;
    private volatile bool _reconnectRequested;
    private long _nextScanTimestamp;
    private GpuTelemetry _latestGpu = GpuTelemetry.Empty;
    private GpuUpdateJob? _gpuUpdate;
    private NetworkCounter? _networkPrevious;
    private bool _disposed;

    public event Action<AgentStatus>? StatusChanged;

    public TelemetryWorker()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsPowerMonitorEnabled = true
        };
        _computer.Open();
        _hardware = Flatten(_computer.Hardware).ToArray();
        _cpu = _hardware.FirstOrDefault(hardware => hardware.HardwareType == HardwareType.Cpu);
        _gpus = _hardware.Where(hardware => hardware.HardwareType is
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToArray();
        var gpuScope = new HashSet<IHardware>(_gpus.SelectMany(gpu => Flatten([gpu])));
        _nonGpuHardware = _hardware.Where(hardware => !gpuScope.Contains(hardware)).ToArray();
        _primaryGpu = _gpus
            .OrderBy(hardware => hardware.HardwareType == HardwareType.GpuIntel ? 1 : 0)
            .ThenBy(hardware => hardware.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var initial = new TelemetrySnapshot(
            SanitizeHost(Environment.MachineName),
            null, null, null, null, null, null, 0.0,
            null, null, null, null, null);
        _pipeline = new TelemetryPipeline<TelemetrySnapshot>(
            initial,
            SampleOnce,
            TransportOnce,
            OnSamplingError,
            OnTransportError,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    public void Start() => _pipeline.Start();

    public void RequestReconnect()
    {
        _reconnectRequested = true;
        StatusChanged?.Invoke(new AgentStatus(false, _port, "正在重新连接"));
    }

    private TelemetrySnapshot SampleOnce()
    {
        long started = Stopwatch.GetTimestamp();
        TelemetrySnapshot snapshot = ReadSnapshot();
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started, Stopwatch.GetTimestamp());
        if (elapsed >= TimeSpan.FromSeconds(3))
        {
            AppLog.Warn("SENSOR", $"slow {elapsed.TotalMilliseconds:0}ms",
                "sensor-slow", TimeSpan.FromMinutes(5));
        }
        return snapshot;
    }

    private void TransportOnce(TelemetrySnapshot snapshot, TimeSpan sampleAge)
    {
        if (_reconnectRequested)
        {
            _reconnectRequested = false;
            CloseSerial();
            _nextScanTimestamp = 0;
        }

        long now = Stopwatch.GetTimestamp();
        if (_serial is null && now >= _nextScanTimestamp)
        {
            TryConnect();
            _nextScanTimestamp = now + 3 * Stopwatch.Frequency;
        }

        if (_serial is not null && _sessionNonce is not null)
        {
            int queuedBytes = _serial.BytesToWrite;
            _serialBacklogTicks = queuedBytes > 0 ? _serialBacklogTicks + 1 : 0;
            if (_serialBacklogTicks >= 3)
                throw new IOException($"串口发送队列持续未排空 ({queuedBytes} bytes)");

            TelemetryFrame frame = snapshot.ToFrame(_sequence++, _sessionNonce);
            _serial.WriteLine(JsonSerializer.Serialize(frame));
            string message = sampleAge >= TimeSpan.FromSeconds(3)
                ? $"通信正常 · 采样延迟 {Math.Min((int)sampleAge.TotalSeconds, 999)} 秒"
                : $"实时发送 · CPU {Format(frame.cpu_load)}% · GPU {Format(frame.gpu_load)}%";
            StatusChanged?.Invoke(new AgentStatus(true, _port, message));
            if (sampleAge >= TimeSpan.FromSeconds(5))
            {
                AppLog.Warn("SENSOR", $"stale {sampleAge.TotalSeconds:0}s; transport alive",
                    "sensor-stale", TimeSpan.FromMinutes(5));
            }
        }
        else
        {
            StatusChanged?.Invoke(new AgentStatus(false, null, "等待兼容开发板"));
        }
    }

    private static void OnSamplingError(Exception ex) =>
        AppLog.Error("SENSOR", ex, "sensor-loop");

    private void OnTransportError(Exception ex)
    {
        AppLog.Error("USB", ex, "usb-loop");
        StatusChanged?.Invoke(new AgentStatus(false, _port, "连接中断，自动重试"));
        CloseSerial();
        _nextScanTimestamp = 0;
    }

    private void TryConnect()
    {
        foreach (string candidate in FindCandidatePorts())
        {
            SerialPort? serial = null;
            try
            {
                serial = CreateSerialPort(candidate);
                serial.Open();
                serial.DiscardInBuffer();
                serial.DiscardOutBuffer();

                string nonce = ProtocolHandshake.CreateNonce();
                serial.WriteLine(ProtocolHandshake.CreateHello(nonce));
                string response = ReadProtocolLine(serial, TimeSpan.FromMilliseconds(1500));
                FirmwareIdentity identity = ProtocolHandshake.ValidateReady(response, nonce);

                _serial = serial;
                serial = null;
                _port = candidate;
                _lastVerifiedPort = candidate;
                _sessionNonce = identity.Nonce;
                _firmwareVersion = identity.Version;
                AppLog.Info("USB", $"ready {candidate} fw={identity.Version} proto={BuildInfo.ProtocolVersion}");
                StatusChanged?.Invoke(new AgentStatus(true, candidate,
                    $"已连接 {candidate} · 固件 v{identity.Version}"));
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or
                                              TimeoutException or InvalidDataException or InvalidOperationException)
            {
                if (string.Equals(candidate, _lastVerifiedPort, StringComparison.OrdinalIgnoreCase))
                    _lastVerifiedPort = null;
                AppLog.Warn("PORT", $"reject {candidate}: {AppLog.ExceptionSummary(ex)}",
                    $"port-reject-{candidate}", TimeSpan.FromMinutes(10));
            }
            finally
            {
                if (serial is not null)
                {
                    try { if (serial.IsOpen) serial.Close(); } catch { }
                    serial.Dispose();
                }
            }
        }
    }

    private TelemetrySnapshot ReadSnapshot()
    {
        foreach (IHardware item in _nonGpuHardware)
        {
            try { item.Update(); } catch { }
        }

        GpuTelemetry gpu = GetGpuTelemetry();
        MemoryStatus? memory = GetMemoryStatus();
        (double? down, double? up) = GetNetworkRates();
        double? cpuLoad = Bounded(GetPreferred(_cpu is null ? [] : [_cpu], SensorType.Load,
            ["^CPU Total$", "^CPU Core Max$", "Total"], true), 0.0, 100.0);
        double? cpuTemp = Bounded(GetPreferred(_cpu is null ? [] : [_cpu], SensorType.Temperature,
            ["^CPU Package$", "Tctl/Tdie", "Core Average", "Core Max"], true), 0.0, 250.0);
        double? cpuPower = Bounded(GetPreferred(_cpu is null ? [] : [_cpu], SensorType.Power,
            ["^CPU Package$", "Package", "Cores"], true), 0.0, 5000.0);
        double? combinedPower = SumAvailable([cpuPower, gpu.AllPower]);
        _energy.AddSample(Stopwatch.GetTimestamp(), Stopwatch.Frequency, combinedPower);

        double? fan = Bounded(GetPreferred(_nonGpuHardware, SensorType.Fan,
            ["CPU", "Pump", "Chassis", "System"], true), 0.0, 1_000_000.0) ?? gpu.FanRpm;
        return new TelemetrySnapshot(
            SanitizeHost(Environment.MachineName),
            cpuLoad, cpuTemp, cpuPower,
            gpu.Load, gpu.Temperature, gpu.Power,
            Math.Min(_energy.KilowattHours, 1_000_000.0),
            memory?.UsedGiB, memory?.TotalGiB,
            ClampNullable(down, 0.0, 10_000_000.0),
            ClampNullable(up, 0.0, 10_000_000.0),
            fan);
    }

    private GpuTelemetry GetGpuTelemetry()
    {
        if (_gpus.Length == 0) return GpuTelemetry.Empty;

        GpuUpdateJob? job = _gpuUpdate;
        if (job is null)
        {
            _gpuUpdate = StartGpuUpdate();
        }
        else if (job.Task.IsCompleted)
        {
            try
            {
                _latestGpu = job.Task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLog.Error("GPU", ex, "gpu-provider");
            }
            _gpuUpdate = StartGpuUpdate();
        }
        else
        {
            TimeSpan updateAge = Stopwatch.GetElapsedTime(job.Started, Stopwatch.GetTimestamp());
            if (updateAge >= TimeSpan.FromSeconds(3))
            {
                AppLog.Warn("GPU", $"provider delayed {updateAge.TotalSeconds:0}s",
                    "gpu-provider-slow", TimeSpan.FromMinutes(5));
            }
        }

        if (_latestGpu.Timestamp == 0 ||
            Stopwatch.GetElapsedTime(_latestGpu.Timestamp, Stopwatch.GetTimestamp()) >= TimeSpan.FromSeconds(5))
            return GpuTelemetry.Empty;
        return _latestGpu;
    }

    private GpuUpdateJob StartGpuUpdate()
    {
        long started = Stopwatch.GetTimestamp();
        Task<GpuTelemetry> task = Task.Run(ReadGpuTelemetry);
        return new GpuUpdateJob(task, started);
    }

    private GpuTelemetry ReadGpuTelemetry()
    {
        foreach (IHardware gpu in _gpus) gpu.Update();

        double? load = Bounded(GetPreferred(_primaryGpu is null ? [] : [_primaryGpu], SensorType.Load,
            ["^GPU Core$", "^D3D 3D$", "GPU"], true), 0.0, 100.0);
        double? temperature = Bounded(GetPreferred(_primaryGpu is null ? [] : [_primaryGpu],
            SensorType.Temperature, ["^GPU Core$", "GPU Hot Spot", "GPU"], true), 0.0, 250.0);
        double? power = Bounded(GetPreferred(_primaryGpu is null ? [] : [_primaryGpu], SensorType.Power,
            ["^GPU Board Power$", "^GPU Package$", "^GPU Power$", "Board", "Package"], true),
            0.0, 5000.0);
        double? allPower = SumAvailable(_gpus.Select(item =>
            Bounded(GetPreferred([item], SensorType.Power,
                ["^GPU Board Power$", "^GPU Package$", "^GPU Power$", "Board", "Package"], true),
                0.0, 5000.0)));
        double? fan = Bounded(GetPreferred(_gpus, SensorType.Fan,
            ["GPU", "Fan"], true), 0.0, 1_000_000.0);
        return new GpuTelemetry(load, temperature, power, allPower, fan, Stopwatch.GetTimestamp());
    }

    private IEnumerable<string> FindCandidatePorts()
    {
        string[] available = SerialPort.GetPortNames()
            .Select(port => port.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (available.Length == 0) return [];

        var preferred = new List<string>();
        if (_lastVerifiedPort is not null && available.Contains(_lastVerifiedPort, StringComparer.OrdinalIgnoreCase))
            return [_lastVerifiedPort];

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%' ");
            foreach (ManagementObject device in searcher.Get())
            {
                string name = device["Name"]?.ToString() ?? "";
                string id = device["PNPDeviceID"]?.ToString() ?? "";
                if (!id.Contains(EspressifUsbSerialIdentity, StringComparison.OrdinalIgnoreCase)) continue;
                Match match = Regex.Match(name, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                if (match.Success) preferred.Add(match.Groups[1].Value.ToUpperInvariant());
            }
            return preferred.Where(port => available.Contains(port, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            AppLog.Warn("PORT", AppLog.ExceptionSummary(ex), "port-wmi", TimeSpan.FromMinutes(10));
            return preferred.Concat(available).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    private static SerialPort CreateSerialPort(string port) => new(port, 115200, Parity.None, 8, StopBits.One)
    {
        Encoding = new UTF8Encoding(false, true),
        DtrEnable = false,
        RtsEnable = false,
        NewLine = "\n",
        ReadTimeout = 200,
        WriteTimeout = 700,
        ReadBufferSize = 4096,
        WriteBufferSize = 1024
    };

    private static string ReadProtocolLine(SerialPort serial, TimeSpan timeout)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        var bytes = new List<byte>(256);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            try
            {
                int value = serial.ReadByte();
                if (value < 0) continue;
                if (value == '\n')
                {
                    if (bytes.Count > 0 && bytes[^1] == '\r') bytes.RemoveAt(bytes.Count - 1);
                    if (bytes.Count == 0 || bytes.Contains((byte)'\r'))
                        throw new InvalidDataException("握手响应行结束无效");
                    return new UTF8Encoding(false, true).GetString(bytes.ToArray());
                }
                if (bytes.Count >= 1024) throw new InvalidDataException("握手响应超过 1024 字节");
                bytes.Add((byte)value);
            }
            catch (TimeoutException)
            {
                // A short serial timeout keeps the overall handshake deadline bounded.
            }
        }
        throw new TimeoutException("开发板握手超时");
    }

    private static float? GetPreferred(IEnumerable<IHardware> hardware, SensorType type,
                                       string[] patterns, bool maximumFallback)
    {
        ISensor[] candidates = hardware.SelectMany(item => item.Sensors)
            .Where(sensor => sensor.SensorType == type && sensor.Value.HasValue &&
                             float.IsFinite(sensor.Value.Value))
            .ToArray();
        foreach (string pattern in patterns)
        {
            ISensor? match = candidates.FirstOrDefault(sensor =>
                Regex.IsMatch(sensor.Name, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            if (match?.Value is float value) return value;
        }
        if (candidates.Length == 0) return null;
        return maximumFallback ? candidates.Max(sensor => sensor.Value!.Value) : candidates[0].Value;
    }

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> roots)
    {
        foreach (IHardware root in roots)
        {
            yield return root;
            foreach (IHardware child in Flatten(root.SubHardware)) yield return child;
        }
    }

    private static MemoryStatus? GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status) || status.TotalPhysical == 0) return null;
        const double gib = 1024d * 1024d * 1024d;
        return new MemoryStatus(
            Math.Round((status.TotalPhysical - status.AvailablePhysical) / gib, 1),
            Math.Round(status.TotalPhysical / gib, 1));
    }

    private (double? Down, double? Up) GetNetworkRates()
    {
        ulong received = 0;
        ulong sent = 0;
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                IPv4InterfaceStatistics statistics = adapter.GetIPv4Statistics();
                received += (ulong)statistics.BytesReceived;
                sent += (ulong)statistics.BytesSent;
            }
            catch { }
        }

        long now = Stopwatch.GetTimestamp();
        var current = new NetworkCounter(received, sent, now);
        if (_networkPrevious is not NetworkCounter previous)
        {
            _networkPrevious = current;
            return (null, null);
        }
        _networkPrevious = current;
        double seconds = Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds;
        if (seconds <= 0 || received < previous.Received || sent < previous.Sent) return (null, null);
        return ((received - previous.Received) * 8d / seconds / 1_000_000d,
                (sent - previous.Sent) * 8d / seconds / 1_000_000d);
    }

    private static double? Bounded(float? value, double minimum, double maximum) =>
        value.HasValue && float.IsFinite(value.Value) && value.Value >= minimum && value.Value <= maximum
            ? Math.Round(value.Value, 1)
            : null;

    private static double? ClampNullable(double? value, double minimum, double maximum) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value >= minimum && value.Value <= maximum
            ? Math.Round(value.Value, 1)
            : null;

    private static double? SumAvailable(IEnumerable<double?> values)
    {
        double sum = 0.0;
        bool found = false;
        foreach (double? value in values)
        {
            if (!value.HasValue || !double.IsFinite(value.Value) || value.Value < 0.0) continue;
            sum += value.Value;
            found = true;
        }
        return found && double.IsFinite(sum) ? sum : null;
    }

    private static string SanitizeHost(string input)
    {
        string result = new(input.ToUpperInvariant()
            .Where(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')
            .Take(15)
            .ToArray());
        return result.Length == 0 ? "DESKTOP" : result;
    }

    private static string Format(double? value) =>
        value?.ToString("0", CultureInfo.InvariantCulture) ?? "--";

    private void CloseSerial()
    {
        try { if (_serial?.IsOpen == true) _serial.Close(); } catch { }
        _serial?.Dispose();
        _serial = null;
        _sessionNonce = null;
        _firmwareVersion = null;
        _port = null;
        _serialBacklogTicks = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PipelineStopResult stopped = _pipeline.Stop(TimeSpan.FromSeconds(3));
        if (stopped.TransportStopped) CloseSerial();
        else AppLog.Warn("USB", "transport thread did not stop before shutdown");

        bool gpuStopped = _gpuUpdate is null || _gpuUpdate.Task.IsCompleted;
        if (stopped.SamplingStopped && gpuStopped) _computer.Close();
        else AppLog.Warn("SENSOR", "provider still blocked during shutdown");
        _pipeline.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx status);

    private readonly record struct MemoryStatus(double UsedGiB, double TotalGiB);
    private readonly record struct NetworkCounter(ulong Received, ulong Sent, long Timestamp);
    private sealed record GpuUpdateJob(Task<GpuTelemetry> Task, long Started);
    private sealed record GpuTelemetry(
        double? Load,
        double? Temperature,
        double? Power,
        double? AllPower,
        double? FanRpm,
        long Timestamp)
    {
        public static GpuTelemetry Empty { get; } = new(null, null, null, null, null, 0);
    }
}

internal sealed record TelemetrySnapshot(
    string Host,
    double? CpuLoad,
    double? CpuTemp,
    double? CpuPower,
    double? GpuLoad,
    double? GpuTemp,
    double? GpuPower,
    double SessionEnergyKwh,
    double? MemoryUsedGiB,
    double? MemoryTotalGiB,
    double? NetDownMbps,
    double? NetUpMbps,
    double? FanRpm)
{
    public TelemetryFrame ToFrame(uint sequence, string sessionNonce)
    {
        DateTime now = DateTime.Now;
        return new TelemetryFrame
        {
            v = BuildInfo.ProtocolMajor,
            seq = sequence,
            session_nonce = sessionNonce,
            host = Host,
            date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            clock = now.ToString("HH:mm", CultureInfo.InvariantCulture),
            cpu_load = CpuLoad,
            cpu_temp = CpuTemp,
            cpu_power = CpuPower,
            gpu_load = GpuLoad,
            gpu_temp = GpuTemp,
            gpu_power = GpuPower,
            session_energy_kwh = Math.Round(SessionEnergyKwh, 6, MidpointRounding.AwayFromZero),
            memory_used_gb = MemoryUsedGiB,
            memory_total_gb = MemoryTotalGiB,
            net_down_mbps = NetDownMbps,
            net_up_mbps = NetUpMbps,
            fan_rpm = FanRpm
        };
    }
}

internal sealed class TelemetryFrame
{
    public int v { get; init; }
    public uint seq { get; init; }
    public string session_nonce { get; init; } = "";
    public string host { get; init; } = "";
    public string date { get; init; } = "";
    public string clock { get; init; } = "";
    public double? cpu_load { get; init; }
    public double? cpu_temp { get; init; }
    public double? cpu_power { get; init; }
    public double? gpu_load { get; init; }
    public double? gpu_temp { get; init; }
    public double? gpu_power { get; init; }
    public double session_energy_kwh { get; init; }
    public double? memory_used_gb { get; init; }
    public double? memory_total_gb { get; init; }
    public double? net_down_mbps { get; init; }
    public double? net_up_mbps { get; init; }
    public double? fan_rpm { get; init; }
}
