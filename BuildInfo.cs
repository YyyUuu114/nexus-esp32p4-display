namespace NexusDisplay;

internal static class BuildInfo
{
    public const string ProductId = "NEXUS_DESKTOP";
    public const string FirmwareProductId = "NEXUS_ESP32P4_DISPLAY";
    public const string ReleaseChannel = "development";
    public const int ProtocolMajor = 2;
    public const int ProtocolRevision = 0;
    public const string MinimumFirmwareVersion = "2.1.1";
    public const int MaximumFirmwareMajorExclusive = 3;
    public const string UpdateSigningKeyId = "nexus-dev-2026-01";
    public const string UpdateSigningPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEc4RE/HiHi9E/NRgE6G9Ze/TEf6CNQlT2r+lDBcN95k++Xq9mNdi+TtCTCpS9R5EG65/YT05O7WguwIp2oGuCAQ==";
    public const string OfficialManifestUrl =
        "https://raw.githubusercontent.com/YyyUuu114/nexus-esp32p4-display/desktop-dev/package/latest-update.json";

    private static readonly Version AssemblyVersion =
        typeof(BuildInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public static ProductVersion Current { get; } = new(
        checked((uint)AssemblyVersion.Major),
        checked((uint)AssemblyVersion.Minor),
        checked((uint)Math.Max(AssemblyVersion.Build, 0)));

    public static ProductVersion MinimumFirmware { get; } =
        ProductVersion.Parse(MinimumFirmwareVersion);
    public static string DisplayVersion => Current.ToString();
    public static string ProtocolVersion => $"{ProtocolMajor}.{ProtocolRevision}";
}
