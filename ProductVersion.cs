using System.Globalization;
using System.Text.RegularExpressions;

namespace NexusDisplay;

internal readonly record struct ProductVersion(uint Major, uint Line, uint Channel)
    : IComparable<ProductVersion>
{
    private static readonly Regex Pattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.([0-9])$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ProductVersion Parse(string text)
    {
        if (!TryParse(text, out ProductVersion version))
            throw new InvalidDataException($"无效产品版本：{text}");
        return version;
    }

    public static bool TryParse(string? text, out ProductVersion version)
    {
        version = default;
        if (text is null) return false;
        Match match = Pattern.Match(text);
        if (!match.Success ||
            !uint.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint major) ||
            !uint.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint line) ||
            !uint.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint channel))
            return false;
        version = new ProductVersion(major, line, channel);
        return true;
    }

    public bool IsInRange(ProductVersion minimum, uint maximumExclusiveMajor) =>
        CompareTo(minimum) >= 0 && Major < maximumExclusiveMajor;

    public int CompareTo(ProductVersion other)
    {
        int comparison = Major.CompareTo(other.Major);
        if (comparison != 0) return comparison;
        comparison = Line.CompareTo(other.Line);
        if (comparison != 0) return comparison;
        if (Channel == other.Channel) return 0;
        if (Channel == 0) return 1;
        if (other.Channel == 0) return -1;
        return Channel.CompareTo(other.Channel);
    }

    public override string ToString() => $"{Major}.{Line}.{Channel}";

    public static bool operator <(ProductVersion left, ProductVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(ProductVersion left, ProductVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(ProductVersion left, ProductVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ProductVersion left, ProductVersion right) => left.CompareTo(right) >= 0;
}
