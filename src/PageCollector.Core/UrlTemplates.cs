using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PageCollector.Core;

public static partial class UrlTemplates
{
    [GeneratedRegex(@"\{(?:id|suchwert)(?::([^{}]*))?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^\s*([0-9]+)\s*(?:-\s*([0-9]+)\s*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Selection();

    public static string Expand(string template, string value)
    {
        if (!Placeholder().IsMatch(template))
        {
            throw new ArgumentException($"In dieser Vorlage fehlt ein {{id}}-Platzhalter: {template}");
        }

        // Select before encoding, so one position always refers to an input character.
        var expanded = Placeholder().Replace(template, match => Uri.EscapeDataString(
            match.Groups[1].Success ? Select(value, match.Groups[1].Value) : value));

        if (expanded.IndexOfAny(['{', '}']) >= 0 ||
            !Uri.TryCreate(expanded, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException($"Ungültige HTTP(S)-Vorlage: {template}");
        }

        var authorityEnd = template.IndexOfAny(['/', '?', '#'], template.IndexOf("://", StringComparison.Ordinal) + 3);
        var authority = authorityEnd < 0 ? template : template[..authorityEnd];

        if (Placeholder().IsMatch(authority))
        {
            throw new ArgumentException("Der Platzhalter muss im Pfad oder hinter einem Fragezeichen stehen.");
        }

        return expanded;
    }

    private static string Select(string value, string selector)
    {
        // Text elements also keep surrogate pairs and combining accents together.
        var positions = StringInfo.ParseCombiningCharacters(value);
        var result = new StringBuilder();

        foreach (var part in selector.Split(','))
        {
            var match = Selection().Match(part);

            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var first))
            {
                throw new ArgumentException($"Ungültige Zeichenauswahl „{selector}“. Beispiele: {{id:1-3}}, {{id:4}}, {{id:1,3,5}}.");
            }

            var last = first;

            if (match.Groups[2].Success && !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out last))
            {
                throw new ArgumentException($"Ungültiger Bereich „{part}“.");
            }

            if (first < 1 || last < first || last > positions.Length)
            {
                throw new ArgumentException($"Zeichenauswahl „{selector}“ passt nicht zu {positions.Length} Zeichen. Positionen beginnen bei 1; Bereiche laufen aufsteigend.");
            }

            for (var index = first - 1; index < last; index++)
            {
                var end = index + 1 < positions.Length ? positions[index + 1] : value.Length;
                result.Append(value, positions[index], end - positions[index]);
            }
        }

        return result.ToString();
    }

    public static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
}