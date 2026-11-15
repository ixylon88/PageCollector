using System.Globalization;
using System.Text.RegularExpressions;

namespace PageCollector.Core;

public sealed record ArchiveNamingOptions(string Prefix = "", string Suffix = "", bool IncludeTitle = true);
public static partial class ArchiveNaming
{
    public static void Validate(ArchiveNamingOptions options)
    {
        if (options.Prefix.Length > 40 || options.Suffix.Length > 40)
        {
            throw new ArgumentException("Präfix und Suffix dürfen jeweils höchstens 40 Zeichen lang sein.");
        }
    }

    public static string BuildStem(string value, string title, ArchiveNamingOptions options)
    {
        Validate(options);
        // Keep the affixes even when a long search value needs shortening.
        var stem = Sanitize(options.Prefix + Truncate(value, 80) + options.Suffix).Trim(' ', '.');

        if (stem.Length == 0)
        {
            stem = "page";
        }

        if (options.IncludeTitle && !string.IsNullOrWhiteSpace(title))
        {
            // Leave room for the collision counter and .source.json on Windows.
            var titleBudget = Math.Min(100, 220 - stem.Length - 3);
            var safeTitle = ShortTitle(Sanitize(TitleFilter.Normalize(title)), titleBudget).Trim(' ', '.');

            if (safeTitle.Length > 0)
            {
                stem += " - " + safeTitle;
            }
        }

        return ProtectDeviceName(stem);
    }

    public static string ShortTitle(string title, int maximumLength = 100)
    {
        title = TitleFilter.Normalize(title);

        if (title.Length <= maximumLength)
        {
            return title;
        }

        var prefix = Truncate(title, maximumLength);

        // A boundary exactly before whitespace already ends a complete word.
        if (prefix.Length < title.Length && char.IsWhiteSpace(title[prefix.Length]))
        {
            return prefix.TrimEnd();
        }

        var boundary = prefix.LastIndexOf(' ');

        return boundary > 0 ? prefix[..boundary].TrimEnd() : prefix.TrimEnd();
    }

    [GeneratedRegex("[<>:\"/\\\\|?*\\x00-\\x1F]")]
    private static partial Regex InvalidFileCharacters();

    [GeneratedRegex(@"^(?:CON|PRN|AUX|NUL|CLOCK\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceName();

    internal static string Sanitize(string value) => InvalidFileCharacters().Replace(value, "_");

    internal static string ProtectDeviceName(string value) => DeviceName().IsMatch(value) ? "_" + value : value;

    public static string Truncate(string value, int maximumLength)
    {
        if (maximumLength <= 0)
        {
            return "";
        }

        if (value.Length <= maximumLength)
        {
            return value;
        }

        var ends = StringInfo.ParseCombiningCharacters(value);
        var length = 0;

        foreach (var end in ends)
        {
            if (end > maximumLength)
            {
                break;
            }

            length = end;
        }

        return value[..length];
    }
}