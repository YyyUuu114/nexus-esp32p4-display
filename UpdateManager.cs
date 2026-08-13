using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NexusDisplay;

internal static class UpdateManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    public static async Task<bool> CheckAndPrepareAsync()
    {
        try
        {
            Uri manifestUri = LoadManifestUri();
            using HttpClient client = CreateClient();
            string envelopeJson = await DownloadTextAsync(client, manifestUri, 128 * 1024);
            UpdatePayload payload = UpdateTrust.VerifyEnvelope(envelopeJson);
            ProductVersion available = ProductVersion.Parse(payload.Version);
            if (available <= BuildInfo.Current)
            {
                MessageBox.Show($"当前 v{BuildInfo.DisplayVersion} 已是最新版。", "NEXUS Display 更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            string compatibilityNotice = payload.PeerUpdateRequired
                ? $"\n\n此版本要求配套固件 v{payload.PairedFirmwareVersion}，两端必须同步更新。"
                : "";
            DialogResult install = MessageBox.Show(
                $"发现已签名版本 v{available}，是否立即下载并安装？{compatibilityNotice}",
                "NEXUS Display 更新", MessageBoxButtons.YesNo,
                payload.PeerUpdateRequired ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            if (install != DialogResult.Yes) return false;

            string updateDirectory = CreateUpdateDirectory();
            string envelopePath = Path.Combine(updateDirectory, "update-envelope.json");
            string packagePath = Path.Combine(updateDirectory, "update.zip");
            File.WriteAllText(envelopePath, envelopeJson, new UTF8Encoding(false));
            await DownloadPackageAsync(client, new Uri(payload.DownloadUrl), packagePath);
            UpdateTrust.VerifyPackageHash(packagePath, payload);

            InstallationManager.EnsureUpdaterCopy();
            var startInfo = new ProcessStartInfo
            {
                FileName = InstallationManager.UpdaterExecutable,
                UseShellExecute = true,
                WorkingDirectory = InstallationManager.UpdaterDirectory
            };
            foreach (string argument in new[]
            {
                "--apply-update", "--parent-pid", Environment.ProcessId.ToString(),
                "--package", packagePath, "--envelope", envelopePath
            }) startInfo.ArgumentList.Add(argument);
            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("无法启动受保护的更新辅助程序");
            AppLog.Info("UPDATE", $"prepared v{available}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("UPDATE", ex);
            MessageBox.Show($"更新失败：{AppLog.ExceptionSummary(ex)}", "NEXUS Display 更新",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    public static int RunApplyMode(string[] arguments)
    {
        string executable = Environment.ProcessPath ?? Application.ExecutablePath;
        if (!Path.GetFullPath(executable).Equals(
                Path.GetFullPath(InstallationManager.UpdaterExecutable),
                StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Error("UPDATE", "apply mode rejected outside protected updater path");
            return 2;
        }

        string? packagePath = GetOption(arguments, "--package");
        string? envelopePath = GetOption(arguments, "--envelope");
        string? parentText = GetOption(arguments, "--parent-pid");
        if (packagePath is null || envelopePath is null ||
            !int.TryParse(parentText, out int parentProcessId) || parentProcessId <= 0)
        {
            AppLog.Error("UPDATE", "apply arguments invalid");
            return 2;
        }

        string? incoming = null;
        string? protectedPackage = null;
        try
        {
            WaitForParent(parentProcessId);
            string envelopeJson = File.ReadAllText(envelopePath, new UTF8Encoding(false, true));
            UpdatePayload payload = UpdateTrust.VerifyEnvelope(envelopeJson);
            ProductVersion version = ProductVersion.Parse(payload.Version);
            if (version <= BuildInfo.Current)
                throw new InvalidDataException("更新版本没有高于当前更新辅助程序版本");

            protectedPackage = Path.Combine(InstallationManager.InstallRoot,
                $"package-{Guid.NewGuid():N}.zip");
            File.Copy(packagePath, protectedPackage, false);
            UpdateTrust.VerifyPackageHash(protectedPackage, payload);
            incoming = Path.Combine(InstallationManager.InstallRoot, $"incoming-{Guid.NewGuid():N}");
            UpdateTrust.ExtractValidatedPackage(protectedPackage, incoming, version);
            ApplyAtomicSwap(incoming, version);
            incoming = null;
            File.Delete(protectedPackage);
            protectedPackage = null;
            TryDeleteUpdateStaging(packagePath);
            AppLog.Info("UPDATE", $"installed v{version}");
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("UPDATE", ex);
            if (incoming is not null) TryDeleteProtectedDirectory(incoming);
            if (protectedPackage is not null)
            {
                try { File.Delete(protectedPackage); } catch { }
            }
            TryStartCurrentVersion();
            return 1;
        }
    }

    private static void ApplyAtomicSwap(string incoming, ProductVersion version)
    {
        string current = InstallationManager.CurrentDirectory;
        string rollback = Path.Combine(InstallationManager.InstallRoot, "rollback");
        if (!Directory.Exists(current)) throw new DirectoryNotFoundException("当前安装目录不存在");
        if (Directory.Exists(rollback)) TryDeleteProtectedDirectory(rollback);

        Directory.Move(current, rollback);
        bool newCurrentPlaced = false;
        try
        {
            Directory.Move(incoming, current);
            newCurrentPlaced = true;
            string executable = Path.Combine(current, InstallationManager.ExecutableName);
            using Process child = StartInstalledProcess(executable, ["--background", "--post-update", version.ToString()]);
            if (child.WaitForExit(2500))
                throw new InvalidOperationException($"新版启动后立即退出，代码 {child.ExitCode}");
        }
        catch
        {
            if (newCurrentPlaced && Directory.Exists(current))
            {
                string failed = Path.Combine(InstallationManager.InstallRoot, $"failed-{Guid.NewGuid():N}");
                Directory.Move(current, failed);
                Directory.Move(rollback, current);
                TryDeleteProtectedDirectory(failed);
            }
            else if (!Directory.Exists(current) && Directory.Exists(rollback))
            {
                Directory.Move(rollback, current);
            }
            TryStartCurrentVersion();
            throw;
        }
    }

    private static Process StartInstalledProcess(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动更新后的程序");
    }

    private static void TryStartCurrentVersion()
    {
        try
        {
            if (File.Exists(InstallationManager.CurrentExecutable))
                StartInstalledProcess(InstallationManager.CurrentExecutable, ["--background"]);
        }
        catch (Exception ex)
        {
            AppLog.Error("UPDATE", $"rollback launch failed: {AppLog.ExceptionSummary(ex)}");
        }
    }

    private static void WaitForParent(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.WaitForExit(45_000)) throw new TimeoutException("旧版程序未在 45 秒内退出");
        }
        catch (ArgumentException)
        {
            // The parent exited before the updater opened its process handle.
        }
    }

    private static Uri LoadManifestUri()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "update-channel.json");
        string configuredUrl = BuildInfo.OfficialManifestUrl;
        if (File.Exists(configPath))
        {
            UpdateChannel channel = JsonSerializer.Deserialize<UpdateChannel>(
                File.ReadAllText(configPath), JsonOptions) ?? throw new InvalidDataException("更新通道配置无效");
            if (!channel.Channel.Equals(BuildInfo.ReleaseChannel, StringComparison.Ordinal) ||
                !channel.ManifestUrl.Equals(BuildInfo.OfficialManifestUrl, StringComparison.Ordinal))
                throw new InvalidDataException("更新通道配置不属于本版本的固定官方源");
            configuredUrl = channel.ManifestUrl;
        }
        return new Uri(configuredUrl, UriKind.Absolute);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NexusDisplay", BuildInfo.DisplayVersion));
        return client;
    }

    private static async Task<string> DownloadTextAsync(HttpClient client, Uri uri, int maximumBytes)
    {
        using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
            throw new InvalidDataException("更新清单过大");
        await using Stream input = await response.Content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length > maximumBytes - count) throw new InvalidDataException("更新清单过大");
            output.Write(buffer, 0, count);
        }
        return new UTF8Encoding(false, true).GetString(output.ToArray());
    }

    private static async Task DownloadPackageAsync(HttpClient client, Uri uri, string destination)
    {
        using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > UpdateTrust.MaximumPackageBytes)
            throw new InvalidDataException("更新包超过 300 MB 限制");
        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            total += count;
            if (total > UpdateTrust.MaximumPackageBytes) throw new InvalidDataException("更新包超过 300 MB 限制");
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
    }

    private static string CreateUpdateDirectory()
    {
        string root = Path.Combine(AppLog.DirectoryPath, "updates");
        Directory.CreateDirectory(root);
        foreach (string old in Directory.EnumerateDirectories(root))
        {
            if (Directory.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-2))
            {
                try { Directory.Delete(old, true); } catch { }
            }
        }
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteUpdateStaging(string packagePath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(packagePath);
            string updatesRoot = Path.GetFullPath(Path.Combine(AppLog.DirectoryPath, "updates"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (directory is not null &&
                (Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, true);
        }
        catch { }
    }

    private static void TryDeleteProtectedDirectory(string path)
    {
        string root = Path.GetFullPath(InstallationManager.InstallRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || target.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("拒绝删除安装根目录以外的路径");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private static string? GetOption(string[] arguments, string name)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return arguments[index + 1];
        }
        return null;
    }

    private sealed class UpdateChannel
    {
        public string Channel { get; init; } = "";
        public string ManifestUrl { get; init; } = "";
    }
}
