using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace NexusDisplay;

internal readonly record struct AgentStatus(bool Connected, string? Port, string Message);

internal sealed class TelemetryWorker : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Computer _computer;
    private readonly IHardware[] _hardware;
    private readonly IHardware? _cpu;
    private readonly IHardware[] _gpus;
    private Task? _task;
    private SerialPort? _serial;
    private string? _port;
    private uint _sequence;
    private volatile bool _reconnectRequested;
    private NetworkCounter? _networkPrevious;
    private long? _lastEnergySampleTimestamp;
    private double? _lastCombinedPowerWatts;
    private double _sessionEnergyKwh;
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
        _cpu = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _gpus = _hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToArray();
    }

    public void Start() => _task = Task.Run(() => RunAsync(_stop.Token));

    public void RequestReconnect()
    {
        _reconnectRequested = true;
        StatusChanged?.Invoke(new AgentStatus(false, _port, "正在重新连接"));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_reconnectRequested)
                {
                    _reconnectRequested = false;
                    CloseSerial();
                }

                // Sampling is deliberately independent of the USB connection. Energy
                // belongs to this app process lifetime and survives board disconnects.
                TelemetryFrame frame = ReadFrame();

                if (_serial is null || !_serial.IsOpen)
                {
                    _port = FindEspPort();
                    if (_port is null)
                    {
                        StatusChanged?.Invoke(new AgentStatus(false, null, "等待开发板"));
                        await Task.Delay(3000, cancellationToken);
                        continue;
                    }

                    OpenSerial(_port);
                    AppLog.Info("USB", $"connected {_port}");
                    StatusChanged?.Invoke(new AgentStatus(true, _port, $"已连接 {_port}"));
                    await Task.Delay(1200, cancellationToken);
                }

                string json = JsonSerializer.Serialize(frame);
                _serial!.Write(json);
                _serial.Write("\n");
                StatusChanged?.Invoke(new AgentStatus(true, _port,
                    $"实时发送 · CPU {Format(frame.cpu_load)}% · GPU {Format(frame.gpu_load)}%"));
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Error("USB", ex, "usb-loop");
                StatusChanged?.Invoke(new AgentStatus(false, _port, "连接中断，自动重试"));
                CloseSerial();
                try { await Task.Delay(2500, cancellationToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private TelemetryFrame ReadFrame()
    {
        foreach (IHardware item in _hardware)
        {
            try { item.Update(); } catch { }
        }

        IHardware? gpu = SelectPrimaryGpu();
        MemoryStatus memory = GetMemoryStatus();
        (double? down, double? up) = GetNetworkRates();
        float? fan = GetPreferred(_hardware, SensorType.Fan, ["CPU", "Pump", "Chassis", "System"], true);
        double? cpuLoad = Metric(GetPreferred(_cpu is null ? [] : [_cpu], SensorType.Load, ["^CPU Total$", "^CPU Core Max$", "Total"], true));
        double? cpuTemp = Positive(GetPreferred(_cpu is null ? [] : [_cpu], SensorType.Temperature, ["^CPU Package$", "Tctl/Tdie", "Core Average", "Core Max"], true));
        double? cpuPower = Positive(GetPreferred(_cpu is null ? [] : [_cpu], SensorType.Power, ["^CPU Package$", "Package", "Cores"], true));
        double? gpuLoad = Metric(GetPreferred(gpu is null ? [] : [gpu], SensorType.Load, ["^GPU Core$", "^D3D 3D$", "GPU"], true));
        double? gpuTemp = Positive(GetPreferred(gpu is null ? [] : [gpu], SensorType.Temperature, ["^GPU Core$", "GPU Hot Spot", "GPU"], true));
        double? gpuPower = Metric(GetPreferred(gpu is null ? [] : [gpu], SensorType.Power, ["^GPU Board Power$", "^GPU Package$", "^GPU Power$", "Board", "Package"], true));

        UpdateSessionEnergy(cpuPower, gpuPower);

        return new TelemetryFrame
        {
            v = BuildInfo.ProtocolMajor,
            seq = _sequence++,
            ts_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            host = Environment.MachineName.ToUpperInvariant(),
            date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            clock = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            cpu_load = cpuLoad,
            cpu_temp = cpuTemp,
            cpu_power = cpuPower,
            gpu_load = gpuLoad,
            gpu_temp = gpuTemp,
            gpu_power = gpuPower,
            session_energy_kwh = Math.Round(_sessionEnergyKwh, 6, MidpointRounding.AwayFromZero),
            memory_used_gb = Round(memory.UsedGiB),
            memory_total_gb = Round(memory.TotalGiB),
            net_down_mbps = Round(down),
            net_up_mbps = Round(up),
            fan_rpm = Round(fan, 0)
        };
    }

    private void UpdateSessionEnergy(double? cpuPower, double? gpuPower)
    {
        long now = Stopwatch.GetTimestamp();
        double? combinedPower = cpuPower.HasValue || gpuPower.HasValue
            ? (cpuPower ?? 0.0) + (gpuPower ?? 0.0)
            : null;

        if (_lastEnergySampleTimestamp.HasValue && _lastCombinedPowerWatts.HasValue && combinedPower.HasValue)
        {
            double elapsedSeconds = (now - _lastEnergySampleTimestamp.Value) / (double)Stopwatch.Frequency;
            // Ignore suspend/hibernate-sized gaps; sensor endpoints cannot represent them.
            if (elapsedSeconds > 0.0 && elapsedSeconds <= 10.0)
            {
                double averageWatts = (_lastCombinedPowerWatts.Value + combinedPower.Value) / 2.0;
                _sessionEnergyKwh += averageWatts * elapsedSeconds / 3_600_000.0;
            }
        }

        _lastEnergySampleTimestamp = now;
        _lastCombinedPowerWatts = combinedPower;
    }

    private IHardware? SelectPrimaryGpu()
    {
        return _gpus
            .Select(g => new
            {
                Hardware = g,
                Integrated = g.HardwareType == HardwareType.GpuIntel ? 1 : 0,
                Load = GetPreferred([g], SensorType.Load, ["^GPU Core$", "^D3D 3D$", "GPU"], true) ?? -1
            })
            .OrderBy(x => x.Integrated)
            .ThenByDescending(x => x.Load)
            .Select(x => x.Hardware)
            .FirstOrDefault();
    }

    private static float? GetPreferred(IEnumerable<IHardware> hardware, SensorType type, string[] patterns, bool maximumFallback)
    {
        var candidates = hardware.SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == type && s.Value.HasValue && float.IsFinite(s.Value.Value))
            .ToArray();
        foreach (string pattern in patterns)
        {
            ISensor? match = candidates.FirstOrDefault(s => Regex.IsMatch(s.Name, pattern, RegexOptions.IgnoreCase));
            if (match?.Value is float value) return value;
        }
        if (candidates.Length == 0) return null;
        return maximumFallback ? candidates.Max(s => s.Value!.Value) : candidates[0].Value;
    }

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> roots)
    {
        foreach (IHardware root in roots)
        {
            yield return root;
            foreach (IHardware child in Flatten(root.SubHardware))
                yield return child;
        }
    }

    private void OpenSerial(string port)
    {
        _serial = new SerialPort(port, 115200, Parity.None, 8, StopBits.One)
        {
            Encoding = new System.Text.UTF8Encoding(false),
            DtrEnable = false,
            RtsEnable = false,
            WriteTimeout = 700
        };
        _serial.Open();
    }

    private static string? FindEspPort()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%' ");
            string? fallback = null;
            foreach (ManagementObject device in searcher.Get())
            {
                string name = device["Name"]?.ToString() ?? "";
                string id = device["PNPDeviceID"]?.ToString() ?? "";
                Match match = Regex.Match(name, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                string port = match.Groups[1].Value.ToUpperInvariant();
                fallback ??= port;
                if (id.Contains("VID_303A&PID_1001", StringComparison.OrdinalIgnoreCase))
                    return port;
            }

            string[] ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
            if (ports.Length == 1) return ports[0];
            return fallback is not null && ports.Length == 1 ? fallback : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("PORT", AppLog.ExceptionSummary(ex), "port-scan", TimeSpan.FromMinutes(10));
            string[] ports = SerialPort.GetPortNames();
            return ports.Length == 1 ? ports[0] : null;
        }
    }

    private static MemoryStatus GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return default;
        const double gib = 1024d * 1024d * 1024d;
        return new MemoryStatus((status.TotalPhysical - status.AvailablePhysical) / gib, status.TotalPhysical / gib);
    }

    private (double? Down, double? Up) GetNetworkRates()
    {
        ulong received = 0, sent = 0;
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                IPv4InterfaceStatistics stats = adapter.GetIPv4Statistics();
                received += (ulong)stats.BytesReceived;
                sent += (ulong)stats.BytesSent;
            }
            catch { }
        }

        var now = Stopwatch.GetTimestamp();
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

    private static double? Metric(float? value) => value.HasValue && float.IsFinite(value.Value) ? Math.Round(value.Value, 1) : null;
    private static double? Positive(float? value) => value is > 0 && float.IsFinite(value.Value) ? Math.Round(value.Value, 1) : null;
    private static double? Round(double? value, int digits = 1) => value.HasValue && double.IsFinite(value.Value) ? Math.Round(value.Value, digits) : null;
    private static string Format(double? value) => value?.ToString("0", CultureInfo.InvariantCulture) ?? "--";

    private void CloseSerial()
    {
        try { if (_serial?.IsOpen == true) _serial.Close(); } catch { }
        _serial?.Dispose();
        _serial = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _task?.Wait(2500); } catch { }
        CloseSerial();
        _computer.Close();
        _stop.Dispose();
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
}

internal sealed class TelemetryFrame
{
    public int v { get; init; }
    public uint seq { get; init; }
    public long ts_ms { get; init; }
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
