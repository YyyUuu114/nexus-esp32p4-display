using System.Diagnostics;
using System.Security.Cryptography;

namespace NexusDisplay;

internal static class InstallationManager
{
    public const string ExecutableName = "Nexus Display.exe";
    public static string InstallRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NEXUS Display");
    public static string CurrentDirectory { get; } = Path.Combine(InstallRoot, "current");
    public static string CurrentExecutable { get; } = Path.Combine(CurrentDirectory, ExecutableName);
    public static string UpdaterDirectory { get; } = Path.Combine(InstallRoot, "updater");
    public static string UpdaterExecutable { get; } = Path.Combine(UpdaterDirectory, "Nexus Display Updater.exe");

    public static bool IsCurrentProcessInstalled => PathsEqual(
        Environment.ProcessPath ?? Application.ExecutablePath, CurrentExecutable);

    public static bool EnsureInstalledAndRelaunch(IReadOnlyCollection<string> arguments)
    {
        if (IsCurrentProcessInstalled) return false;

        InstallCurrentExecutable();
        var forwarded = arguments
            .Where(value => !value.Equals("--installed-launch", StringComparison.OrdinalIgnoreCase))
            .Append("--installed-launch")
            .ToArray();
        StartProcess(CurrentExecutable, forwarded);
        return true;
    }

    public static void EnsureUpdaterCopy()
    {
        EnsureProtectedInstallRoot();
        EnsureProtectedChildDirectory(UpdaterDirectory, "更新程序目录");
        string source = Environment.ProcessPath ?? Application.ExecutablePath;
        string temporary = Path.Combine(UpdaterDirectory, $"updater-{Guid.NewGuid():N}.tmp");
        File.Copy(source, temporary, true);
        File.Move(temporary, UpdaterExecutable, true);
    }

    public static void StartProcess(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("无法启动已安装的 NEXUS Display");
    }

    private static void InstallCurrentExecutable()
    {
        EnsureProtectedInstallRoot();
        EnsureProtectedChildDirectory(CurrentDirectory, "当前版本目录");

        string source = Environment.ProcessPath ?? Application.ExecutablePath;
        ProductVersion sourceVersion = BuildInfo.Current;
        ProductVersion? installedVersion = ReadFileProductVersion(CurrentExecutable);
        bool shouldInstall = !installedVersion.HasValue || sourceVersion > installedVersion.Value ||
                             (sourceVersion == installedVersion.Value &&
                              !FilesHaveSameSha256(source, CurrentExecutable));
        if (shouldInstall)
        {
            string temporary = Path.Combine(CurrentDirectory, $"install-{Guid.NewGuid():N}.tmp");
            File.Copy(source, temporary, true);
            File.Move(temporary, CurrentExecutable, true);
        }

        string channelSource = Path.Combine(AppContext.BaseDirectory, "update-channel.json");
        string channelTarget = Path.Combine(CurrentDirectory, "update-channel.json");
        if (File.Exists(channelSource))
            File.Copy(channelSource, channelTarget, true);
        else if (!File.Exists(channelTarget))
            File.WriteAllText(channelTarget,
                $$"""
                {
                  "channel": "{{BuildInfo.ReleaseChannel}}",
                  "manifestUrl": "{{BuildInfo.OfficialManifestUrl}}"
                }
                """,
                new System.Text.UTF8Encoding(false));

        AppLog.Info("INSTALL", $"protected v{sourceVersion}");
    }

    private static void EnsureProtectedInstallRoot()
    {
        string programFiles = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(InstallRoot).TrimEnd(Path.DirectorySeparatorChar) +
                      Path.DirectorySeparatorChar;
        if (!root.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安装目录不在受保护的 Program Files 下");

        Directory.CreateDirectory(InstallRoot);
        var attributes = File.GetAttributes(InstallRoot);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("安装目录不能是重解析点");
    }

    private static void EnsureProtectedChildDirectory(string path, string description)
    {
        string canonicalRoot = Path.GetFullPath(InstallRoot).TrimEnd(Path.DirectorySeparatorChar) +
                               Path.DirectorySeparatorChar;
        string canonicalPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) +
                               Path.DirectorySeparatorChar;
        if (!canonicalPath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{description}超出安装目录");

        Directory.CreateDirectory(path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"{description}不能是重解析点");
    }

    private static bool FilesHaveSameSha256(string left, string right)
    {
        if (!File.Exists(left) || !File.Exists(right)) return false;
        using FileStream leftStream = new(left, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream rightStream = new(right, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] leftHash = SHA256.HashData(leftStream);
        byte[] rightHash = SHA256.HashData(rightStream);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static ProductVersion? ReadFileProductVersion(string path)
    {
        if (!File.Exists(path)) return null;
        string? text = FileVersionInfo.GetVersionInfo(path).FileVersion;
        string[] parts = (text ?? "").Split('.');
        return parts.Length >= 3 && ProductVersion.TryParse(string.Join('.', parts.Take(3)), out ProductVersion version)
            ? version
            : null;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);
}
