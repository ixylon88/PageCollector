using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PageCollector.Core;

var passed = 0;
var root = Path.Combine(Path.GetTempPath(), "pagecollector-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    Check("Template substitution preserves suffixes and encodes only values", () =>
    {
        Equal("https://example.com/k/Q123456.html", UrlTemplates.Expand("https://example.com/k/Q{id}.html", "123456"));
        Equal("https://example.com/?q=A%26B%2F%C3%A4%20%231", UrlTemplates.Expand("https://example.com/?q={Suchwert}", "A&B/ä #1"));
        Equal("https://example.com/%241?q=%241", UrlTemplates.Expand("https://example.com/{ID}?q={id}", "$1"));
    });
    Check("Invalid and unsafe templates fail before a run starts", () =>
    {
        Throws<ArgumentException>(() => UrlTemplates.Expand("file:///tmp/{id}", "1"));
        Throws<ArgumentException>(() => UrlTemplates.Expand("https://example.com/no-placeholder", "1"));
        Throws<ArgumentException>(() => UrlTemplates.Expand("https://{id}.example.com/", "1"));
        Throws<ArgumentException>(() => UrlTemplates.Expand("https://example.com/{other}/{id}", "1"));
        Throws<ArgumentException>(() => UrlTemplates.Expand("https://user:secret@example.com/{id}", "1"));
    });
    Check("The first accepted candidate stops that value's template sequence", () =>
    {
        var session = new CollectionSession(["123", "456"], ["https://example.com/a/{id}", "https://example.com/b/{id}", "https://example.com/c/{id}"]);
        Equal("https://example.com/a/123", session.Current.Url);
        Equal<string?>(null, session.RejectCandidate());
        Equal("https://example.com/b/123", session.Current.Url);
        session.Accept();
        Equal("https://example.com/a/456", session.Current.Url);
        Equal(1, session.AcceptedCount);
        session.SkipValue();
        Equal(true, session.IsComplete);
    });
    Check("Exhausted values advance; merely reading or retrying does not", () =>
    {
        var session = new CollectionSession(["first", "second"], ["https://example.com/{id}"]);
        var retry = session.Current;
        Equal(retry, session.Current);
        Equal("first", session.RejectCandidate());
        Equal("second", session.Current.Value);
        Equal("second", session.RejectCandidate());
        Equal(true, session.IsComplete);
        Equal(0, session.AcceptedCount);
    });
    Check("Windows filenames cannot escape the output directory", () =>
    {
        Equal("_.._Q12_34", ArchiveStore.SafeFileName("../../Q12:34"));
    });
    Check("Filename edge cases are safe on Windows", () =>
    {
        foreach (var value in new[] { "../../Q12:34", "CON", "NUL.txt", "LPT1", "COM¹.txt", "a\\b", "<>:\"/?*|", "...", "" })
        {
            var name = ArchiveStore.SafeFileName(value);
            Equal(false, name.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0);
            Equal(false, name.EndsWith('.'));
            Equal(false, name.Length == 0);
        }
        Equal("_CON", ArchiveStore.SafeFileName("CON"));
        Equal("_NUL.txt", ArchiveStore.SafeFileName("NUL.txt"));
    });
    var fixture = Path.Combine(root, "fixture.html");
    const string original = "<!doctype html><html><head><title>Überschrift</title></head><body><h1>Lesbarer Inhalt</h1></body></html>";
    await File.WriteAllTextAsync(fixture, original, new UTF8Encoding(false));
    var candidate = new Candidate("123456", "https://example.com/{id}", "https://example.com/123456", 1, 1);
    var capture = new CaptureResult(fixture, "Überschrift", DateTimeOffset.UtcNow);
    await CheckAsync("Accepted files preserve bytes, include provenance and never overwrite", async () =>
    {
        var output = Path.Combine(root, "archives");
        var one = await ArchiveStore.SaveAsync(output, candidate, capture, CancellationToken.None);
        var two = await ArchiveStore.SaveAsync(output, candidate, capture, CancellationToken.None);
        Equal("123456.html", Path.GetFileName(one.HtmlPath));
        Equal("123456 (2).html", Path.GetFileName(two.HtmlPath));
        Equal(original, await File.ReadAllTextAsync(one.HtmlPath));
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(one.MetadataPath));
        Equal(candidate.Url, metadata.RootElement.GetProperty("requestedUrl").GetString());
        Equal("manual", metadata.RootElement.GetProperty("approval").GetString());
        Equal(64, metadata.RootElement.GetProperty("sha256").GetString()!.Length);
        Equal(true, File.Exists(fixture));
    });
    await CheckAsync("Cancelled saves never create an accepted archive", async () =>
    {
        var output = Path.Combine(root, "cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => ArchiveStore.SaveAsync(output, candidate, capture, cancellation.Token));
        Equal(0, Directory.GetFiles(output).Length);
    });
    Check("CSV is UTF-8, quoted, Excel-safe and readable before the session ends", () =>
    {
        using var log = new SessionLog(Path.Combine(root, "logs"));
        log.Append(candidate with { Value = "=1+1" }, "Accepted", "C:\\file.html", "a;\"b\"\nÄ");
        var bytes = File.ReadAllBytes(log.FilePath);
        Equal(true, bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
        var contents = Encoding.UTF8.GetString(bytes);
        Equal(true, contents.Contains("\"'=1+1\""));
        Equal(true, contents.Contains("\"a;\"\"b\"\"\nÄ\""));
    });
    if (!OperatingSystem.IsWindows())
    {
        await CheckAsync("A failed capture cannot become an accepted result", async () =>
        {
            var script = Path.Combine(root, "failed-engine");
            await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 'Deliberate capture failure' >&2\nexit 7\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await ThrowsAsync<IOException>(() => new CaptureRunner().CaptureAsync(candidate, Path.Combine(root, "failed.html"), new(script, "/unused"), CancellationToken.None));
        });
        await CheckAsync("Cancellation terminates a running capture promptly", async () =>
        {
            var script = Path.Combine(root, "slow-engine");
            await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 30\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var clock = Stopwatch.StartNew();
            await ThrowsAsync<OperationCanceledException>(() => new CaptureRunner().CaptureAsync(candidate, Path.Combine(root, "slow.html"), new(script, "/unused"), cancellation.Token));
            Equal(true, clock.Elapsed < TimeSpan.FromSeconds(5));
        });
    }
    if (args.Length == 4)
    {
        await CheckAsync("Real SingleFile capture preserves CSS, images and delayed DOM content", async () =>
        {
            Directory.CreateDirectory(args[3]);
            var url = args[2];
            var target = Path.Combine(Path.GetFullPath(args[3]), "offline.html");
            // The integration fixture runs in a Linux container without a usable browser sandbox.
            // Production captures never pass this switch.
            var browserArguments = OperatingSystem.IsLinux() ? new[] { "--no-sandbox" } : null;
            var result = await new CaptureRunner().CaptureAsync(candidate with { Url = url }, target,
                new(Path.GetFullPath(args[0]), args[1], 1500, 150, browserArguments), CancellationToken.None);
            var html = await File.ReadAllTextAsync(result.FilePath);
            Equal("PageCollector fixture", result.Title);
            Equal(true, html.Contains("Dynamisch ergänzter Text"));
            Equal(true, html.Contains("data:image/svg+xml"));
            Equal(true, html.Contains("#edf3fb") || html.Contains("rgb(237,243,251)") || html.Contains("237, 243, 251"));
            Console.WriteLine("CAPTURE " + target);
        });
    }
    if (args.Length == 1 && args[0] == "--download-engine")
    {
        await CheckAsync("The pinned Windows engine downloads and matches its published SHA-256", async () =>
        {
            var path = await EngineInstaller.EnsureInstalledAsync(null, CancellationToken.None);
            Equal(true, await EngineInstaller.IsValidAsync(path, CancellationToken.None));
        });
    }
    Console.WriteLine($"PASS: {passed} checks.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}

void Check(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine("PASS " + name);
}
async Task CheckAsync(string name, Func<Task> action)
{
    await action();
    passed++;
    Console.WriteLine("PASS " + name);
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected: {expected}; actual: {actual}");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
