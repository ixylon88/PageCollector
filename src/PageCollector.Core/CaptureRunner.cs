using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PageCollector.Core;

public sealed record CaptureOptions(string EnginePath, string BrowserPath, int WaitMilliseconds = 1500, int TimeoutSeconds = 150, IReadOnlyList<string>? BrowserArguments = null);
public sealed record CaptureResult(string FilePath, string Title, DateTimeOffset CapturedAtUtc);
public sealed class CaptureRunner
{
    public async Task<CaptureResult> CaptureAsync(Candidate candidate, string targetPath, CaptureOptions options, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(options.EnginePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath))!
        };

        foreach (var argument in Arguments(candidate.Url, targetPath, options))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = start
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        cancellationToken.ThrowIfCancellationRequested();

        if (!process.Start())
        {
            throw new IOException("Die Speicherhilfe konnte nicht gestartet werden.");
        }

        // Drain both pipes concurrently; some pages generate enough diagnostics to fill an OS pipe.
        var stdout = ReadBoundedAsync(process.StandardOutput);
        var stderr = ReadBoundedAsync(process.StandardError);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Die Aufnahme wurde nach {options.TimeoutSeconds} Sekunden beendet.");
        }

        var diagnostics = (await stderr.ConfigureAwait(false)) + "\n" + (await stdout.ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            throw new IOException($"SingleFile meldet Fehler {process.ExitCode}. {diagnostics.Trim()}");
        }

        if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
        {
            throw new IOException("Die Speicherhilfe hat keine HTML-Datei erzeugt.");
        }

        var title = await HtmlTitleReader.ReadAsync(targetPath, cancellationToken).ConfigureAwait(false);

        return new(targetPath, title, DateTimeOffset.UtcNow);
    }

    public static IReadOnlyList<string> Arguments(string url, string output, CaptureOptions options)
    {
        var args = new List<string>
        {
            url,
            Path.GetFullPath(output),
            "--browser-headless=true",
            "--dump-content=false",
            "--compress-content=false",
            "--block-scripts=true",
            "--block-videos=true",
            "--block-audios=true",
            "--load-deferred-content=true",
            "--browser-width=1440",
            "--browser-height=1000",
            "--browser-load-max-time=60000",
            "--browser-capture-max-time=60000",
            "--browser-wait-delay=" + options.WaitMilliseconds.ToString(CultureInfo.InvariantCulture),
            "--browser-executable-path=" + options.BrowserPath
        };

        if (options.BrowserArguments is { Count: > 0 })
        {
            args.Add("--browser-args=" + JsonSerializer.Serialize(options.BrowserArguments));
        }

        return args;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var output = new StringBuilder();
        var buffer = new char[2048];
        int count;

        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            output.Append(buffer, 0, count);

            if (output.Length > 12000)
            {
                output.Remove(0, output.Length - 12000);
            }
        }

        return output.ToString();
    }
}