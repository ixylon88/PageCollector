namespace PageCollector.Core;

// Defaults for added fields also migrate a settings.json written by version 0.1.
public sealed record CollectorSettings(string Values, string Templates, string OutputDirectory, string BrowserPath,
    string DelaySeconds, bool AutoPreview, bool Automatic = true, string? ExcludedTitles = null,
    string Prefix = "", string Suffix = "", bool IncludeTitle = true);
