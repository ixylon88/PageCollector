using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PageCollector.Core;

public sealed record SavedArchive(string HtmlPath, string MetadataPath);

public static class ArchiveStore
{
    public static async Task<SavedArchive> SaveAsync(string directory, Candidate candidate,
        CaptureResult capture, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var stem = SafeFileName(candidate.Value);
        for (var index = 1; index <= 10000; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suffix = index == 1 ? "" : $" ({index})";
            var html = Path.Combine(directory, stem + suffix + ".html");
            var metadata = Path.Combine(directory, stem + suffix + ".source.json");
            if (File.Exists(metadata)) continue;
            FileStream target;
            try { target = new FileStream(html, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true); }
            catch (IOException) when (File.Exists(html)) { continue; }
            bool metadataCreated = false;
            try
            {
                await using (target)
                await using (var source = File.OpenRead(capture.FilePath))
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                string hash;
                await using (var saved = File.OpenRead(html))
                    hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, cancellationToken).ConfigureAwait(false));
                await using var sidecar = new FileStream(metadata, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                metadataCreated = true;
                await JsonSerializer.SerializeAsync(sidecar, new
                {
                    formatVersion = 1, searchValue = candidate.Value, urlTemplate = candidate.Template,
                    requestedUrl = candidate.Url, title = capture.Title,
                    capturedAtUtc = capture.CapturedAtUtc, acceptedAtUtc = DateTimeOffset.UtcNow,
                    fileName = Path.GetFileName(html), sha256 = hash,
                    captureEngine = "SingleFile CLI " + EngineInstaller.Version,
                    approval = "manual"
                }, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
                return new(html, metadata);
            }
            catch
            {
                // Roll back only this operation's new files. Existing archives are never replaced.
                TryDelete(html);
                if (metadataCreated) TryDelete(metadata);
                throw;
            }
        }
        throw new IOException("Für diesen Suchwert existieren bereits zu viele Dateien.");
    }

    public static string SafeFileName(string value)
    {
        var result = Regex.Replace(value, "[<>:\"/\\\\|?*\\x00-\\x1F]", "_").Trim(' ', '.');
        if (result.Length > 80) result = result[..80].TrimEnd(' ', '.');
        if (result.Length == 0) result = "page";
        if (Regex.IsMatch(result, @"^(?:CON|PRN|AUX|NUL|CLOCK\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase))
            result = "_" + result;
        return result;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class SessionLog : IDisposable
{
    private readonly StreamWriter writer;
    public string FilePath { get; }

    public SessionLog(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        FilePath = Path.Combine(outputDirectory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.csv");
        writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(true))
            { AutoFlush = true };
        writer.WriteLine("TimestampUtc;SearchValue;TemplateNumber;RequestedUrl;Result;File;Details");
    }

    public void Append(Candidate candidate, string result, string file = "", string details = "") => writer.WriteLine(
        string.Join(';', new[] { DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), candidate.Value,
            candidate.TemplateNumber.ToString(CultureInfo.InvariantCulture), candidate.Url, result, file, details }.Select(CsvCell)));

    public static string CsvCell(string value)
    {
        // Keep user-entered values from becoming formulas when the log is opened in Excel.
        if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public void Dispose() => writer.Dispose();
}
