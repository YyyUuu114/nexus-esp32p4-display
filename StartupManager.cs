using System.Diagnostics;

namespace NexusDisplay;

internal static class StartupManager
{
    private const string TaskName = "Nexus Display Hardware Monitor";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName) == 0;

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
                return Run("/Delete", "/TN", TaskName, "/F") == 0 || !IsEnabled();

            string executable = Environment.ProcessPath ?? Application.ExecutablePath;
            string command = $"\"{executable}\" --background";
            int exitCode = Run("/Create", "/TN", TaskName, "/SC", "ONLOGON", "/RL", "HIGHEST",
                "/TR", command, "/F");
            if (exitCode != 0)
            {
                AppLog.Error("STARTUP", $"schtasks exit={exitCode}");
                return false;
            }

            bool settingsApplied = ApplyPersistentSettings();
            if (!settingsApplied)
                AppLog.Error("STARTUP", "task settings failed");
            return settingsApplied;
        }
        catch (Exception ex)
        {
            AppLog.Error("STARTUP", ex);
            return false;
        }
    }

    private static int Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null) return -1;
        process.WaitForExit(8000);
        return process.HasExited ? process.ExitCode : -1;
    }

    private static bool ApplyPersistentSettings()
    {
        string script =
            "$task=Get-ScheduledTask -TaskName 'Nexus Display Hardware Monitor';" +
            "$task.Settings.DisallowStartIfOnBatteries=$false;" +
            "$task.Settings.StopIfGoingOnBatteries=$false;" +
            "$task.Settings.ExecutionTimeLimit='PT0S';" +
            "$task.Settings.RestartCount=3;" +
            "$task.Settings.RestartInterval='PT1M';" +
            "$task.Settings.MultipleInstances='IgnoreNew';" +
            "Set-ScheduledTask -InputObject $task | Out-Null";

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory,
                @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo);
        if (process is null) return false;
        process.WaitForExit(12_000);
        return process.HasExited && process.ExitCode == 0;
    }
}

internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;
    private const int RetentionDays = 14;
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
        string message = exception.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "error";
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
                if (throttleKey is not null)
                    LastThrottledWrite[throttleKey] = now;

                Directory.CreateDirectory(DirectoryPath);
                RotateIfNeeded();
                string compact = message.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
                File.AppendAllText(FilePath,
                    $"{now:MM-dd HH:mm:ss} {level} {code} {compact}{Environment.NewLine}",
                    new System.Text.UTF8Encoding(false));
            }
        }
        catch { }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < MaxBytes)
            return;

        string first = Path.Combine(DirectoryPath, "NexusDisplay.1.log");
        string second = Path.Combine(DirectoryPath, "NexusDisplay.2.log");
        if (File.Exists(second)) File.Delete(second);
        if (File.Exists(first)) File.Move(first, second);
        File.Move(FilePath, first);
    }
}
