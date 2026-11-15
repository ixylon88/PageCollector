using System.Text.RegularExpressions;

namespace PageCollector.Core;

public sealed record Candidate(string Value, string Template, string Url, int ValueNumber, int TemplateNumber);

/// <summary>Only explicit user decisions advance the queue. Capture failures do not.</summary>
public sealed class CollectionSession
{
    private readonly string[] values;
    private readonly string[] templates;
    private int valueIndex;
    private int templateIndex;

    public int ValueCount => values.Length;
    public int TemplateCount => templates.Length;
    public int AcceptedCount { get; private set; }
    public bool IsComplete => valueIndex >= values.Length;
    public Candidate Current => IsComplete
        ? throw new InvalidOperationException("Die Liste ist abgeschlossen.")
        : new(values[valueIndex], templates[templateIndex],
            UrlTemplates.Expand(templates[templateIndex], values[valueIndex]), valueIndex + 1, templateIndex + 1);

    public CollectionSession(IEnumerable<string> values, IEnumerable<string> templates)
    {
        this.values = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
        this.templates = templates.Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
        if (this.values.Length == 0) throw new ArgumentException("Bitte mindestens einen Suchwert eingeben.");
        if (this.templates.Length == 0) throw new ArgumentException("Bitte mindestens eine URL-Vorlage eingeben.");
        foreach (var template in this.templates) UrlTemplates.Expand(template, "example");
        foreach (var value in this.values)
            if (value.Length > 2048) throw new ArgumentException("Ein Suchwert ist länger als 2048 Zeichen.");
    }

    public void Accept()
    {
        _ = Current;
        AcceptedCount++;
        NextValue();
    }

    /// <returns>The exhausted value, or null if another template remains for the same value.</returns>
    public string? RejectCandidate()
    {
        var currentValue = Current.Value;
        if (++templateIndex < templates.Length) return null;
        NextValue();
        return currentValue;
    }

    public void SkipValue()
    {
        _ = Current;
        NextValue();
    }

    private void NextValue()
    {
        valueIndex++;
        templateIndex = 0;
    }
}

public static class UrlTemplates
{
    private static readonly Regex Placeholder = new(@"\{(?:id|suchwert)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Expand(string template, string value)
    {
        if (!Placeholder.IsMatch(template))
            throw new ArgumentException($"In dieser Vorlage fehlt {{id}}: {template}");
        // Encode only the inserted value, preserving the template's path/query separators.
        var expanded = Placeholder.Replace(template, _ => Uri.EscapeDataString(value));
        if (expanded.IndexOfAny(['{', '}']) >= 0 ||
            !Uri.TryCreate(expanded, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException($"Ungültige HTTP(S)-Vorlage: {template}");
        // Placeholders belong in paths/queries, not in a host name or URI scheme.
        var authorityEnd = template.IndexOfAny(['/', '?', '#'], template.IndexOf("://", StringComparison.Ordinal) + 3);
        var authority = authorityEnd < 0 ? template : template[..authorityEnd];
        if (Placeholder.IsMatch(authority))
            throw new ArgumentException("Der Platzhalter muss im Pfad oder hinter einem Fragezeichen stehen.");
        return expanded;
    }

    public static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
}
