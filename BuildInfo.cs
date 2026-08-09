namespace NexusDisplay;

internal static class BuildInfo
{
    public const string ReleaseChannel = "development";
    public const int ProtocolMajor = 1;
    public const int ProtocolRevision = 1;
    public const string SupportedFirmwareVersion = "1.1.0";

    private static readonly Version AssemblyVersion =
        typeof(BuildInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public static Version Current { get; } = new(
        AssemblyVersion.Major, AssemblyVersion.Minor, Math.Max(AssemblyVersion.Build, 0));

    public static string DisplayVersion => $"{Current.Major}.{Current.Minor}.{Current.Build}";
    public static string ProtocolVersion => $"{ProtocolMajor}.{ProtocolRevision}";
}
