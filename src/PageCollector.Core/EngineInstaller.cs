using System.Security.Cryptography;
using System.Text.Json;

namespace PageCollector.Core;

public sealed class EngineInstaller(string dataDirectory, HttpClient? client = null) : IEngineService
{
    public static readonly EngineRelease LegacyRelease = new("2.26.0", "55fd4a6275a5d4a3af5cdd86c742761cc1ac2c1c0d4ab9481b3fa600c003d5ff", 81871015);

    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private static readonly JsonSerializerOptions StateOptions = new()
    {
        WriteIndented = true
    };

    private readonly HttpClient client = client ?? SharedClient;

    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PageCollector");

    public string ToolsDirectory => Path.Combine(dataDirectory, "tools");

    private string StatePath => Path.Combine(ToolsDirectory, "engine.json");

    public EngineState ReadState()
    {
        if (!File.Exists(StatePath))
        {
            // Keep existing installations usable without a network request or a forced update.
            return File.Exists(EnginePath(LegacyRelease)) ? new(LegacyRelease, null, null, [LegacyRelease]) : new(null, null, null, []);
        }

        try
        {
            var state = JsonSerializer.Deserialize<EngineState>(File.ReadAllText(StatePath));

            if (state is null || state.Releases is null || state.Releases.Any(release => release is null || !release.IsValid) ||
                state.ActiveRelease is { IsValid: false } ||
                (state.PreferredVersion is not null && !EngineRelease.IsVersion(state.PreferredVersion)) ||
                (state.LatestVersion is not null && !state.Releases.Any(release => release.Version == state.LatestVersion)))
            {
                throw new IOException("Die gespeicherte Versionsverwaltung ist ungültig: " + StatePath);
            }

            return state;
        }
        catch (JsonException exception)
        {
            throw new IOException("Die gespeicherte Versionsverwaltung konnte nicht gelesen werden: " + StatePath, exception);
        }
    }

    public async Task<EngineState> RefreshAsync(CancellationToken cancellationToken)
    {
        var state = ReadState();
        IReadOnlyList<EngineRelease> releases;

        try
        {
            releases = await SingleFileReleases.LoadAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Die Versionsabfrage hat zu lange gedauert. Bitte erneut versuchen.", exception);
        }

        var retained = state.Releases.Where(release => File.Exists(EnginePath(release)));

        if (state.ActiveRelease is not null)
        {
            retained = retained.Append(state.ActiveRelease);
        }

        state = state with
        {
            LatestVersion = releases[0].Version,
            Releases = releases.Concat(retained).DistinctBy(release => release.Version).OrderByDescending(release => Version.Parse(release.Version)).ToArray()
        };
        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);

        return state;
    }

    public async Task<EngineInstallation> EnsureInstalledAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var state = ReadState();

        // Checking for releases never changes the engine used by an existing collection setup.
        return state.ActiveRelease is null
            ? await InstallAsync(state.PreferredVersion, progress, cancellationToken).ConfigureAwait(false)
            : await InstallReleaseAsync(state, state.ActiveRelease, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EngineInstallation> InstallAsync(string? preferredVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (preferredVersion is not null && !EngineRelease.IsVersion(preferredVersion))
        {
            throw new ArgumentException("Bitte eine gültige Version aus der Liste wählen.", nameof(preferredVersion));
        }

        var state = ReadState();
        var version = preferredVersion ?? state.LatestVersion;

        if (version is null || !state.Releases.Any(release => release.Version == version))
        {
            state = await RefreshAsync(cancellationToken).ConfigureAwait(false);
            version = preferredVersion ?? state.LatestVersion;
        }

        var release = state.Releases.FirstOrDefault(release => release.Version == version)
            ?? throw new IOException("Die gewählte Version ist nicht als prüfbarer Windows-Download verfügbar.");

        return await InstallReleaseAsync(state with { PreferredVersion = preferredVersion }, release, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EngineInstallation> InstallReleaseAsync(EngineState state, EngineRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var path = EnginePath(release);

        if (!await IsValidAsync(path, release, cancellationToken).ConfigureAwait(false))
        {
            await DownloadAsync(path, release, progress, cancellationToken).ConfigureAwait(false);
        }

        // Commit the selection only after validation. Failed downloads leave the active version intact.
        await SaveStateAsync(state with { ActiveRelease = release }, cancellationToken).ConfigureAwait(false);
        progress?.Report(1);

        return new(path, release.Version);
    }

    private async Task DownloadAsync(string path, EngineRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ToolsDirectory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var token = deadline.Token;

        try
        {
            using var request = SingleFileReleases.CreateRequest(release.DownloadUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            {
                await using var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long total = 0;
                int count;
                var lastProgress = DateTime.UtcNow;

                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    total += count;

                    if (total > release.Size)
                    {
                        throw new IOException("Die Größe der Speicherhilfe stimmt nicht mit der Veröffentlichung überein.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);

                    if ((DateTime.UtcNow - lastProgress).TotalMilliseconds >= 150)
                    {
                        progress?.Report((double)total / release.Size);
                        lastProgress = DateTime.UtcNow;
                    }
                }
            }

            if (!await IsValidAsync(temporary, release, token).ConfigureAwait(false))
            {
                throw new IOException("Die Prüfsumme oder Größe der Speicherhilfe stimmt nicht. Die Datei wird nicht gestartet. Bitte erneut versuchen.");
            }

            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Der Download hat zu lange gedauert. Bitte erneut versuchen.", exception);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static async Task<bool> IsValidAsync(string path, EngineRelease release, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!release.IsValid || !File.Exists(path) || new FileInfo(path).Length != release.Size)
        {
            return false;
        }

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return string.Equals(Convert.ToHexStringLower(hash), release.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private string EnginePath(EngineRelease release) => Path.Combine(ToolsDirectory, "single-file-" + release.Version + ".exe");

    private async Task SaveStateAsync(EngineState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ToolsDirectory);
        var temporary = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, state, StateOptions, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, StatePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}