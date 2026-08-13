using System.Security.Cryptography;
using System.Text.Json;

namespace NexusDisplay;

internal readonly record struct FirmwareIdentity(ProductVersion Version, string Nonce);

internal static class ProtocolHandshake
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static string CreateNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static string CreateHello(string nonce)
    {
        if (!IsNonce(nonce)) throw new ArgumentException("Invalid session nonce.", nameof(nonce));
        return JsonSerializer.Serialize(new HelloFrame
        {
            type = "hello",
            product = BuildInfo.ProductId,
            desktop_version = BuildInfo.DisplayVersion,
            protocol_major = BuildInfo.ProtocolMajor,
            protocol_revision = BuildInfo.ProtocolRevision,
            minimum_firmware_version = BuildInfo.MinimumFirmwareVersion,
            maximum_firmware_major_exclusive = BuildInfo.MaximumFirmwareMajorExclusive,
            nonce = nonce
        }, SerializerOptions);
    }

    public static FirmwareIdentity ValidateReady(string json, string expectedNonce)
    {
        if (json.Length is 0 or > 1024 || !IsNonce(expectedNonce))
            throw new InvalidDataException("握手响应长度或 nonce 无效");

        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("握手响应不是 JSON 对象");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new InvalidDataException($"握手响应包含重复字段：{property.Name}");
        }

        RequireString(root, "type", "ready");
        RequireString(root, "product", BuildInfo.FirmwareProductId);
        string nonce = RequireString(root, "nonce");
        if (!nonce.Equals(expectedNonce, StringComparison.Ordinal))
            throw new InvalidDataException("握手 nonce 不匹配");

        int protocolMajor = RequireInteger(root, "protocol_major");
        int protocolRevision = RequireInteger(root, "protocol_revision");
        if (protocolMajor != BuildInfo.ProtocolMajor || protocolRevision != BuildInfo.ProtocolRevision)
            throw new InvalidDataException($"不兼容的固件协议 {protocolMajor}.{protocolRevision}");

        ProductVersion firmware = ProductVersion.Parse(RequireString(root, "firmware_version"));
        if (!firmware.IsInRange(BuildInfo.MinimumFirmware, BuildInfo.MaximumFirmwareMajorExclusive))
            throw new InvalidDataException($"固件版本 {firmware} 不在桌面端支持范围内");

        ProductVersion minimumDesktop = ProductVersion.Parse(
            RequireString(root, "minimum_desktop_version"));
        int maximumDesktopMajor = RequireInteger(root, "maximum_desktop_major_exclusive");
        if (maximumDesktopMajor <= 0 ||
            !BuildInfo.Current.IsInRange(minimumDesktop, checked((uint)maximumDesktopMajor)))
            throw new InvalidDataException("固件声明的桌面端版本范围不包含当前版本");

        return new FirmwareIdentity(firmware, nonce);
    }

    private static string RequireString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"握手响应缺少字符串字段 {name}");
        return value.GetString() ?? throw new InvalidDataException($"握手字段 {name} 为空");
    }

    private static void RequireString(JsonElement root, string name, string expected)
    {
        string actual = RequireString(root, name);
        if (!actual.Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException($"握手字段 {name} 不匹配");
    }

    private static int RequireInteger(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new InvalidDataException($"握手响应缺少整数字段 {name}");
        return result;
    }

    private static bool IsNonce(string nonce) =>
        nonce.Length == 32 && nonce.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private sealed class HelloFrame
    {
        public string type { get; init; } = "";
        public string product { get; init; } = "";
        public string desktop_version { get; init; } = "";
        public int protocol_major { get; init; }
        public int protocol_revision { get; init; }
        public string minimum_firmware_version { get; init; } = "";
        public int maximum_firmware_major_exclusive { get; init; }
        public string nonce { get; init; } = "";
    }
}
