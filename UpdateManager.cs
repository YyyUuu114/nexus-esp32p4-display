using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexusDisplay;

internal static class UpdateManager
{
    private const long MaxPackageBytes = 300L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<bool> CheckAndPrepareAsync()
    {
        try
        {
            UpdateChannel? channel = LoadUpdateChannel();
            if (channel is null || string.IsNullOrWhiteSpace(channel.ManifestUrl))
                return await SelectLocalPackageAsync();

            if (!channel.Channel.Equals(BuildInfo.ReleaseChannel, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新通道与当前程序不匹配");

            if (!Uri.TryCreate(channel.ManifestUrl, UriKind.Absolute, out Uri? manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("更新清单必须使用 HTTPS 地址");

            using var client = CreateClient();
            string json = await client.GetStringAsync(manifestUri);
            UpdateManifest manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("更新清单格式无效");
            ValidateManifest(manifest);
            Version available = ParseVersion(manifest.Version);
            if (available <= BuildInfo.Current)
            {
                MessageBox.Show($"当前 v{BuildInfo.DisplayVersion} 已是最新版。", "NEXUS Display 更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            string compatibilityNotice = manifest.PeerUpdateRequired
                ? $"\n\n此版本要求配套固件 {manifest.PairedFirmwareVersion ?? "指定版本"}，必须同步更新。"
                : "";
            DialogResult install = MessageBox.Show(
                $"发现新版本 v{available.ToString(3)}，是否立即下载并安装？{compatibilityNotice}",
                "NEXUS Display 更新", MessageBoxButtons.YesNo,
                manifest.PeerUpdateRequired ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            if (install != DialogResult.Yes) return false;

            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out Uri? downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("更新包必须使用 HTTPS 地址");
            if (string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64)
                throw new InvalidDataException("更新清单缺少有效的 SHA-256");

            string zipPath = Path.Combine(CreateUpdateDirectory(), "update.zip");
            await DownloadAsync(client, downloadUri, zipPath);
            VerifySha256(zipPath, manifest.Sha256);
            return PrepareInstall(zipPath, available);
        }
        catch (Exception ex)
        {
            AppLog.Error("UPDATE", ex);
            MessageBox.Show($"更新失败：{AppLog.ExceptionSummary(ex)}", "NEXUS Display 更新",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static async Task<bool> SelectLocalPackageAsync()
    {
        DialogResult choose = MessageBox.Show(
            "尚未配置在线更新源。是否选择本地新版 ZIP 并自动安装？",
            "NEXUS Display 更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (choose != DialogResult.Yes) return false;

        using var dialog = new OpenFileDialog
        {
            Title = "选择 NEXUS Display 更新包",
            Filter = "NEXUS 更新包 (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != DialogResult.OK) return false;
        if (new FileInfo(dialog.FileName).Length > MaxPackageBytes)
            throw new InvalidDataException("更新包超过 300 MB 限制");

        string zipPath = Path.Combine(CreateUpdateDirectory(), "update.zip");
        File.Copy(dialog.FileName, zipPath, true);
        await Task.Yield();
        return PrepareInstall(zipPath, null);
    }

    private static bool PrepareInstall(string zipPath, Version? expectedVersion)
    {
        string stage = Path.Combine(Path.GetDirectoryName(zipPath)!, "stage");
        ZipFile.ExtractToDirectory(zipPath, stage, true);
        string? payloadExe = Directory.EnumerateFiles(stage, "Nexus Display.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (payloadExe is null)
            throw new InvalidDataException("更新包中找不到 Nexus Display.exe");

        Version payloadVersion = ParseVersion(FileVersionInfo.GetVersionInfo(payloadExe).FileVersion ?? "0.0.0");
        if (expectedVersion is not null && payloadVersion != expectedVersion)
            throw new InvalidDataException("更新包版本与清单不一致");
        if (payloadVersion <= BuildInfo.Current)
        {
            MessageBox.Show($"所选更新包为 v{payloadVersion.ToString(3)}，没有高于当前版本。",
                "NEXUS Display 更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        string target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string scriptPath = Path.Combine(AppLog.DirectoryPath, $"apply-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, ApplyScript, new System.Text.UTF8Encoding(false));

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(Path.GetDirectoryName(payloadExe)!);
        startInfo.ArgumentList.Add(target);
        using Process? helper = Process.Start(startInfo);
        if (helper is null)
            throw new InvalidOperationException("无法启动更新辅助程序");
        AppLog.Info("UPDATE", $"apply v{payloadVersion.ToString(3)}");
        return true;
    }

    private static UpdateChannel? LoadUpdateChannel()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "update-channel.json");
        if (!File.Exists(configPath)) return null;
        return JsonSerializer.Deserialize<UpdateChannel>(File.ReadAllText(configPath), JsonOptions);
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        if (!manifest.Component.Equals("desktop", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新清单组件不是 desktop");
        if (!manifest.Channel.Equals(BuildInfo.ReleaseChannel, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新清单通道与当前程序不匹配");
        if (manifest.ProtocolMajor <= 0)
            throw new InvalidDataException("更新清单缺少有效协议主版本");

        Version version = ParseVersion(manifest.Version);
        bool channelValid = BuildInfo.ReleaseChannel == "stable"
            ? version.Build == 0
            : version.Build is >= 1 and <= 9;
        if (!channelValid)
            throw new InvalidDataException("更新包版本不符合当前通道规则");
        if (manifest.PeerUpdateRequired && string.IsNullOrWhiteSpace(manifest.PairedFirmwareVersion))
            throw new InvalidDataException("协同更新清单缺少配套固件版本");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NexusDisplay", BuildInfo.DisplayVersion));
        return client;
    }

    private static async Task DownloadAsync(HttpClient client, Uri uri, string destination)
    {
        using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxPackageBytes)
            throw new InvalidDataException("更新包超过 300 MB 限制");

        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer)) > 0)
        {
            total += count;
            if (total > MaxPackageBytes) throw new InvalidDataException("更新包超过 300 MB 限制");
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
    }

    private static void VerifySha256(string path, string expected)
    {
        using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新包 SHA-256 校验失败");
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

    private static Version ParseVersion(string value)
    {
        string clean = value.Split(['+', '-'], 2)[0];
        if (!Version.TryParse(clean, out Version? version))
            throw new InvalidDataException($"无效版本号：{value}");
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private const string ApplyScript = """
param([int]$ProcessId, [string]$Source, [string]$Target)
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:LOCALAPPDATA 'NexusDisplay\NexusDisplay.log'
try {
    Wait-Process -Id $ProcessId -Timeout 30 -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        $destination = Join-Path $Target $_.Name
        if ($_.Name -eq 'update-channel.json' -and (Test-Path -LiteralPath $destination)) { return }
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    }
    Start-Process -FilePath (Join-Path $Target 'Nexus Display.exe') -ArgumentList '--background' -WindowStyle Hidden
} catch {
    Add-Content -LiteralPath $log -Value ("{0} ERR UPDATE apply failed: {1}" -f (Get-Date -Format 'MM-dd HH:mm:ss'), $_.Exception.Message)
}
Remove-Item -LiteralPath $Source -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
""";

    private sealed class UpdateChannel
    {
        public string? ManifestUrl { get; init; }
        public string Channel { get; init; } = "";
    }

    private sealed class UpdateManifest
    {
        public string Component { get; init; } = "";
        public string Channel { get; init; } = "";
        public string Version { get; init; } = "";
        public string DownloadUrl { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public int ProtocolMajor { get; init; }
        public string UpdateClass { get; init; } = "";
        public bool PeerUpdateRequired { get; init; }
        public string? PairedFirmwareVersion { get; init; }
    }
}
