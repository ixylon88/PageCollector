using System.Security.Cryptography;

namespace PageCollector.Core;

public static class EngineInstaller
{
    public const string Version = "2.26.0";
    public const string WindowsSha256 = "55fd4a6275a5d4a3af5cdd86c742761cc1ac2c1c0d4ab9481b3fa600c003d5ff";
    public const string DownloadUrl = "https://github.com/gildas-lormeau/single-file-cli/releases/download/v2.26.0/single-file.exe";
    public const long DownloadBytes = 81871015;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PageCollector");
    public static string EnginePath => Path.Combine(DataDirectory, "tools", "single-file-" + Version + ".exe");

    public static async Task<string> EnsureInstalledAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(EnginePath) && await IsValidAsync(EnginePath, cancellationToken).ConfigureAwait(false)) return EnginePath;
        Directory.CreateDirectory(Path.GetDirectoryName(EnginePath)!);
        var temporary = EnginePath + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PageCollector/0.1");
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength ?? DownloadBytes;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                var lastProgress = DateTime.UtcNow;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    total += count;
                    if ((DateTime.UtcNow - lastProgress).TotalMilliseconds < 150) continue;
                    progress?.Report(Math.Clamp((double)total / length, 0, 1));
                    lastProgress = DateTime.UtcNow;
                }
            }
            if (!await IsValidAsync(temporary, cancellationToken).ConfigureAwait(false))
                throw new IOException("Die Prüfsumme der Speicherhilfe stimmt nicht. Die Datei wird nicht gestartet. Bitte erneut versuchen.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, EnginePath, overwrite: true);
            progress?.Report(1);
            return EnginePath;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<bool> IsValidAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexStringLower(hash), WindowsSha256, StringComparison.Ordinal);
    }
}
