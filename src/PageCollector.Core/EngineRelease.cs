using System.Text.Json.Serialization;

namespace PageCollector.Core;

public sealed record EngineRelease(string Version, string Sha256, long Size)
{
    [JsonIgnore]
    public string DownloadUrl => $"https://github.com/gildas-lormeau/single-file-cli/releases/download/v{Version}/single-file.exe";

    [JsonIgnore]
    public bool IsValid => IsVersion(Version) && Sha256 is { Length: 64 } && Sha256.All(char.IsAsciiHexDigit) && Size > 0;

    public static bool IsVersion(string? value) => System.Version.TryParse(value, out var version) && version.Build >= 0 && version.Revision == -1 && version.ToString() == value;
}

public sealed record EngineInstallation(string FilePath, string Version);

/// <summary>A null preference selects the latest known stable release; the active release changes only after a successful install.</summary>
public sealed record EngineState(EngineRelease? ActiveRelease, string? PreferredVersion, string? LatestVersion, IReadOnlyList<EngineRelease> Releases);