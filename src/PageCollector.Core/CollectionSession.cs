namespace PageCollector.Core;

public sealed record Candidate(string Value, string Template, string Url, int ValueNumber, int TemplateNumber);
/// <summary>Advances only when a caller accepts, rejects or skips a candidate.</summary>
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

    public Candidate Current => IsComplete ? throw new InvalidOperationException("Die Liste ist abgeschlossen.") : new(values[valueIndex], templates[templateIndex], UrlTemplates.Expand(templates[templateIndex], values[valueIndex]), valueIndex + 1, templateIndex + 1);

    public CollectionSession(IEnumerable<string> values, IEnumerable<string> templates)
    {
        this.values = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
        this.templates = templates.Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();

        if (this.values.Length == 0)
        {
            throw new ArgumentException("Bitte mindestens einen Suchwert eingeben.");
        }

        if (this.templates.Length == 0)
        {
            throw new ArgumentException("Bitte mindestens eine URL-Vorlage eingeben.");
        }

        foreach (var value in this.values)
        {
            if (value.Length > 2048)
            {
                throw new ArgumentException("Ein Suchwert ist länger als 2048 Zeichen.");
            }
        }

        // Validate the real values: a fixed example cannot validate indexed placeholders.
        for (var templateIndex = 0; templateIndex < this.templates.Length; templateIndex++)
        {
            foreach (var value in this.values)
            {
                try
                {
                    UrlTemplates.Expand(this.templates[templateIndex], value);
                }
                catch (ArgumentException exception)
                {
                    throw new ArgumentException($"Vorlage {templateIndex + 1}, Suchwert „{value}“: {exception.Message}", exception);
                }
            }
        }
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

        if (++templateIndex < templates.Length)
        {
            return null;
        }

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