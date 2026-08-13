using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexusDisplay;

internal sealed class SignedUpdateEnvelope
{
    public int SchemaVersion { get; init; }
    public string KeyId { get; init; } = "";
    public string Payload { get; init; } = "";
    public string Signature { get; init; } = "";
}

internal sealed class UpdatePayload
{
    public int SchemaVersion { get; init; }
    public string Component { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Version { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public int ProtocolMajor { get; init; }
    public int ProtocolRevision { get; init; }
    public string UpdateClass { get; init; } = "";
    public bool PeerUpdateRequired { get; init; }
    public string? PairedFirmwareVersion { get; init; }
    public ProductVersionRange SupportedFirmwareVersions { get; init; } = new();
}

internal sealed class ProductVersionRange
{
    public string Minimum { get; init; } = "";
    public string MaximumExclusive { get; init; } = "";
}

internal static class UpdateTrust
{
    public const long MaximumPackageBytes = 300L * 1024 * 1024;
    public const long MaximumExpandedBytes = 600L * 1024 * 1024;
    private const int MaximumEnvelopeCharacters = 128 * 1024;
    private const int MaximumPayloadBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    public static UpdatePayload VerifyEnvelope(string json)
    {
        if (json.Length is 0 or > MaximumEnvelopeCharacters)
            throw new InvalidDataException("更新清单大小无效");
        EnsureNoDuplicateProperties(json);
        SignedUpdateEnvelope envelope = JsonSerializer.Deserialize<SignedUpdateEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("更新清单格式无效");
        if (envelope.SchemaVersion != 1 ||
            !envelope.KeyId.Equals(BuildInfo.UpdateSigningKeyId, StringComparison.Ordinal))
            throw new InvalidDataException("更新清单版本或签名密钥标识不受信任");

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Convert.FromBase64String(envelope.Payload);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("更新清单签名编码无效", exception);
        }
        if (payloadBytes.Length is 0 or > MaximumPayloadBytes || signature.Length is 0 or > 256)
            throw new InvalidDataException("更新清单签名数据大小无效");

        if (!SignatureVerifier.VerifyEcdsaP256(
                BuildInfo.UpdateSigningPublicKey, payloadBytes, signature))
            throw new CryptographicException("更新清单 ECDSA 签名无效");

        string payloadJson = new UTF8Encoding(false, true).GetString(payloadBytes);
        EnsureNoDuplicateProperties(payloadJson);
        UpdatePayload payload = JsonSerializer.Deserialize<UpdatePayload>(payloadJson, JsonOptions)
            ?? throw new InvalidDataException("签名载荷格式无效");
        ValidatePayload(payload);
        return payload;
    }

    public static ProductVersion ValidatePayload(UpdatePayload payload)
    {
        if (payload.SchemaVersion != 1 || !payload.Component.Equals("desktop", StringComparison.Ordinal) ||
            !payload.Channel.Equals(BuildInfo.ReleaseChannel, StringComparison.Ordinal))
            throw new InvalidDataException("更新载荷组件或通道不匹配");

        ProductVersion version = ProductVersion.Parse(payload.Version);
        bool channelValid = BuildInfo.ReleaseChannel == "stable" ? version.Channel == 0 : version.Channel is >= 1 and <= 9;
        if (!channelValid) throw new InvalidDataException("更新版本不符合当前通道规则");

        if (!Uri.TryCreate(payload.DownloadUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                "/YyyUuu114/nexus-esp32p4-display/releases/download/",
                StringComparison.Ordinal) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("更新包地址不属于受信任的 GitHub 发布路径");
        if (payload.Sha256.Length != 64 || !payload.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("更新包 SHA-256 无效");

        string[] classes = ["single-endpoint", "coordinated-additive", "coordinated-breaking", "paired-baseline"];
        if (!classes.Contains(payload.UpdateClass, StringComparer.Ordinal))
            throw new InvalidDataException("更新分类无效");
        if (payload.ProtocolMajor <= 0 || payload.ProtocolRevision < 0)
            throw new InvalidDataException("更新协议版本无效");
        if (payload.UpdateClass == "single-endpoint" &&
            (payload.ProtocolMajor != BuildInfo.ProtocolMajor ||
             payload.ProtocolRevision != BuildInfo.ProtocolRevision || payload.PeerUpdateRequired))
            throw new InvalidDataException("单端更新不得改变线协议或要求另一端更新");
        if (payload.UpdateClass == "coordinated-additive" &&
            (payload.ProtocolMajor != BuildInfo.ProtocolMajor ||
             payload.ProtocolRevision < BuildInfo.ProtocolRevision || payload.PeerUpdateRequired))
            throw new InvalidDataException("增量协同更新元数据不一致");
        if (payload.UpdateClass is "coordinated-breaking" or "paired-baseline" &&
            (!payload.PeerUpdateRequired || string.IsNullOrWhiteSpace(payload.PairedFirmwareVersion)))
            throw new InvalidDataException("破坏性或配对更新必须声明配套固件");

        ProductVersion minimum = ProductVersion.Parse(payload.SupportedFirmwareVersions.Minimum);
        ProductVersion maximum = ProductVersion.Parse(payload.SupportedFirmwareVersions.MaximumExclusive);
        if (minimum >= maximum || maximum.Channel != 0)
            throw new InvalidDataException("固件兼容范围无效");
        if (payload.PairedFirmwareVersion is not null)
        {
            ProductVersion paired = ProductVersion.Parse(payload.PairedFirmwareVersion);
            if (paired < minimum || paired >= maximum)
                throw new InvalidDataException("配套固件不在更新声明的支持范围内");
        }
        return version;
    }

    public static void VerifyPackageHash(string packagePath, UpdatePayload payload)
    {
        var info = new FileInfo(packagePath);
        if (!info.Exists || info.Length is <= 0 or > MaximumPackageBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("更新包文件大小或类型无效");
        using FileStream stream = new(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("更新包 SHA-256 校验失败");
    }

    public static string ExtractValidatedPackage(string packagePath, string destinationRoot,
                                                 ProductVersion expectedVersion)
    {
        Directory.CreateDirectory(destinationRoot);
        string canonicalRoot = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) +
                               Path.DirectorySeparatorChar;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredExpandedBytes = 0;
        long actualExpandedBytes = 0;

        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relative = NormalizeEntryName(entry.FullName);
            if (relative.Length == 0) continue;
            string destination = Path.GetFullPath(Path.Combine(canonicalRoot, relative));
            if (!destination.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase) ||
                !destinations.Add(destination))
                throw new InvalidDataException($"更新包路径不安全或重复：{entry.FullName}");
            if (IsSymbolicLink(entry)) throw new InvalidDataException("更新包不得包含符号链接");
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            if (entry.Length < 0 || entry.Length > MaximumPackageBytes ||
                declaredExpandedBytes > MaximumExpandedBytes - entry.Length)
                throw new InvalidDataException("更新包解压大小超限");
            declaredExpandedBytes += entry.Length;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream source = entry.Open();
            using FileStream target = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long copied = CopyWithLimit(source, target, entry.Length,
                MaximumExpandedBytes - actualExpandedBytes);
            actualExpandedBytes += copied;
            if (copied != entry.Length)
                throw new InvalidDataException("更新包条目长度与目录记录不一致");
        }

        string executable = Path.Combine(destinationRoot, InstallationManager.ExecutableName);
        if (!File.Exists(executable)) throw new InvalidDataException("更新包缺少主程序");
        ProductVersion actualVersion = ReadExecutableVersion(executable);
        if (actualVersion != expectedVersion)
            throw new InvalidDataException($"更新包程序版本 {actualVersion} 与签名载荷 {expectedVersion} 不一致");
        return executable;
    }

    private static long CopyWithLimit(Stream source, Stream target, long declaredLength, long remainingTotal)
    {
        byte[] buffer = new byte[80 * 1024];
        long copied = 0;
        while (true)
        {
            int read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) return copied;
            if (copied > declaredLength - read || copied > remainingTotal - read)
                throw new InvalidDataException("更新包实际解压大小超限");
            target.Write(buffer, 0, read);
            copied += read;
        }
    }

    public static ProductVersion ReadExecutableVersion(string executable)
    {
        string? fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(executable).FileVersion;
        string[] parts = (fileVersion ?? "").Split('.');
        if (parts.Length < 3 || !ProductVersion.TryParse(string.Join('.', parts.Take(3)), out ProductVersion result))
            throw new InvalidDataException("无法读取更新包程序版本");
        return result;
    }

    private static string NormalizeEntryName(string fullName)
    {
        string normalized = fullName.Replace('\\', '/').TrimStart('/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0 && segments[0].Equals("NEXUS Display", StringComparison.OrdinalIgnoreCase))
            segments = segments[1..];
        foreach (string segment in segments)
        {
            if (segment is "." or ".." || segment.Contains(':') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new InvalidDataException($"更新包路径段无效：{segment}");
        }
        return Path.Combine(segments);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixFileType == 0xA000 ||
               (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static void EnsureNoDuplicateProperties(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16
        });
        Visit(document.RootElement);

        static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InvalidDataException($"JSON 包含重复字段：{property.Name}");
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray()) Visit(item);
            }
        }
    }
}
