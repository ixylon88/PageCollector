using System.Text.RegularExpressions;

namespace PageCollector.Core;

public sealed class TitleFilter
{
    public const string DefaultRules = "^Wayback Machine\n404";
    private readonly string[] rules;

    public TitleFilter(string text)
    {
        rules = UrlTemplates.Lines(text).Select(Normalize).ToArray();
        if (rules.Any(rule => rule == "^"))
            throw new ArgumentException("Nach ^ muss ein Titelanfang stehen, zum Beispiel ^Wayback Machine.");
    }

    /// <returns>The rejection reason, or null when the title passes these rules.</returns>
    public string? RejectionReason(string title)
    {
        title = Normalize(title);
        if (title.Length == 0) return "Kein Seitentitel vorhanden; keine automatische Übernahme.";
        foreach (var rule in rules)
        {
            var matches = rule.StartsWith('^')
                ? title.StartsWith(rule[1..].TrimStart(), StringComparison.OrdinalIgnoreCase)
                : title.Contains(rule, StringComparison.OrdinalIgnoreCase);
            if (matches) return $"Ausschlussregel „{rule}“: {title}";
        }
        return null;
    }

    public static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
