using System.Diagnostics;
using System.Xml.Linq;

namespace NexusDisplay;

internal static class StartupManager
{
    private const string TaskName = "Nexus Display Hardware Monitor";

    public static bool IsEnabled()
    {
        ProcessResult result = Run("/Query", "/TN", TaskName, "/XML");
        if (result.ExitCode != 0) return false;
        try
        {
            XDocument document = XDocument.Parse(result.StandardOutput, LoadOptions.None);
            string? command = document.Descendants().FirstOrDefault(
                element => element.Name.LocalName == "Command")?.Value;
            string? arguments = document.Descendants().FirstOrDefault(
                element => element.Name.LocalName == "Arguments")?.Value;
            return command is not null &&
                   Path.GetFullPath(command).Equals(
                       Path.GetFullPath(InstallationManager.CurrentExecutable),
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(arguments?.Trim(), "--background", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLog.Warn("STARTUP", AppLog.ExceptionSummary(ex), "startup-query", TimeSpan.FromMinutes(10));
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                ProcessResult deletion = Run("/Delete", "/TN", TaskName, "/F");
                bool removed = deletion.ExitCode == 0 || !TaskExists();
                if (removed) AppLog.Info("STARTUP", "disabled");
                return removed;
            }

            if (!File.Exists(InstallationManager.CurrentExecutable))
                throw new InvalidOperationException("请先安装 NEXUS Display");

            string taskCommand = $"\"{InstallationManager.CurrentExecutable}\" --background";
            ProcessResult creation = Run(
                "/Create", "/TN", TaskName, "/SC", "ONLOGON", "/RL", "HIGHEST",
                "/TR", taskCommand, "/F");
            if (creation.ExitCode != 0 || !IsEnabled())
            {
                AppLog.Error("STARTUP", $"create exit={creation.ExitCode}");
                return false;
            }
            AppLog.Info("STARTUP", "enabled protected-target");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("STARTUP", ex);
            return false;
        }
    }

    private static bool TaskExists() => Run("/Query", "/TN", TaskName).ExitCode == 0;

    private static ProcessResult Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

        using Process? process = Process.Start(startInfo);
        if (process is null) return new ProcessResult(-1, "", "process start failed");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(true); } catch { }
            process.WaitForExit(2_000);
            return new ProcessResult(-1, CompletedText(standardOutput), "timeout");
        }
        if (!Task.WaitAll([standardOutput, standardError], 2_000))
            return new ProcessResult(-1, CompletedText(standardOutput), "output timeout");
        return new ProcessResult(process.ExitCode, standardOutput.Result, standardError.Result);
    }

    private static string CompletedText(Task<string> task) =>
        task.Status == TaskStatus.RanToCompletion ? task.Result : "";

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;
    private const int RetentionDays = 7;
    private const int MaximumMessageLength = 600;
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusDisplay");
    private static string FilePath => Path.Combine(DirectoryPath, "NexusDisplay.log");
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> LastThrottledWrite = new(StringComparer.Ordinal);

    public static void Initialize()
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                foreach (string file in Directory.EnumerateFiles(DirectoryPath, "NexusDisplay*.log"))
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-RetentionDays))
                        File.Delete(file);
                }
                RotateIfNeeded();
            }
        }
        catch { }
    }

    public static void Info(string code, string message) => Write("INF", code, message);
    public static void Warn(string code, string message, string? throttleKey = null, TimeSpan? interval = null) =>
        Write("WRN", code, message, throttleKey, interval);
    public static void Error(string code, string message) => Write("ERR", code, message);
    public static void Error(string code, Exception exception, string? throttleKey = null) =>
        Write("ERR", code, ExceptionSummary(exception), throttleKey, TimeSpan.FromMinutes(5));

    public static string ExceptionSummary(Exception exception)
    {
        string message = exception.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "error";
        return $"{exception.GetType().Name}: {message}";
    }

    private static void Write(string level, string code, string message,
                              string? throttleKey = null, TimeSpan? interval = null)
    {
        try
        {
            lock (Gate)
            {
                DateTime now = DateTime.Now;
                if (throttleKey is not null && LastThrottledWrite.TryGetValue(throttleKey, out DateTime last) &&
                    now - last < (interval ?? TimeSpan.FromMinutes(5)))
                    return;
                if (throttleKey is not null) LastThrottledWrite[throttleKey] = now;

                Directory.CreateDirectory(DirectoryPath);
                RotateIfNeeded();
                string compact = message.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
                if (compact.Length > MaximumMessageLength) compact = compact[..MaximumMessageLength];
                File.AppendAllText(FilePath,
                    $"{now:MM-dd HH:mm:ss} {level} {code} {compact}{Environment.NewLine}",
                    new System.Text.UTF8Encoding(false));
            }
        }
        catch { }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < MaxBytes) return;
        string first = Path.Combine(DirectoryPath, "NexusDisplay.1.log");
        string second = Path.Combine(DirectoryPath, "NexusDisplay.2.log");
        if (File.Exists(second)) File.Delete(second);
        if (File.Exists(first)) File.Move(first, second);
        File.Move(FilePath, first);
    }
}
