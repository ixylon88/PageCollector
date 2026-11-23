using System.Globalization;
using System.Net;
using System.Text.Json;

namespace PageCollector.Core;

internal static class SingleFileReleases
{
    public static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("PageCollector/0.2");

        return request;
    }

    public static async Task<IReadOnlyList<EngineRelease>> LoadAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var releases = new Dictionary<string, EngineRelease>(StringComparer.Ordinal);

        for (var page = 1; ; page++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = CreateRequest("https://api.github.com/repos/gildas-lormeau/single-file-cli/releases?per_page=100&page=" + page.ToString(CultureInfo.InvariantCulture));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException("GitHub erlaubt derzeit keine Versionsabfrage. Bitte später erneut versuchen. Die aktive Version bleibt nutzbar.");
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token).ConfigureAwait(false);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new IOException("GitHub hat keine gültige Versionsliste geliefert.");
            }

            foreach (var item in root.EnumerateArray())
            {
                var release = ParseRelease(item);

                if (release is not null)
                {
                    releases.TryAdd(release.Version, release);
                }
            }

            if (root.GetArrayLength() < 100)
            {
                break;
            }
        }

        if (releases.Count == 0)
        {
            throw new IOException("Keine stabile Windows-Version mit SHA-256-Prüfsumme gefunden.");
        }

        return releases.Values.OrderByDescending(release => Version.Parse(release.Version)).ToArray();
    }

    private static EngineRelease? ParseRelease(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !item.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False)
        {
            return null;
        }

        var tag = ReadString(item, "tag_name");

        if (tag is null || !tag.StartsWith('v') || !EngineRelease.IsVersion(tag[1..]) ||
            !item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || ReadString(asset, "name") != "single-file.exe")
            {
                continue;
            }

            var digest = ReadString(asset, "digest");

            if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes))
            {
                continue;
            }

            var release = new EngineRelease(tag[1..], digest[7..], bytes);

            if (release.IsValid && ReadString(asset, "browser_download_url") == release.DownloadUrl)
            {
                return release;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}