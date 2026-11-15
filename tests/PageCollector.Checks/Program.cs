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
    Check("One-based indexes, ranges and the original placeholder can be combined", () =>
    {
        Equal("https://example.com/asdf123/4/56", UrlTemplates.Expand("https://example.com/asdf{id:1,2,3}/{id:4}/{id:5,6}", "123456"));
        Equal("https://example.com/asdf123/4/56", UrlTemplates.Expand("https://example.com/asdf{id:1-3}/{id:4}/{id:5-6}", "123456"));
        Equal("https://example.com/ululu123/123456", UrlTemplates.Expand("https://example.com/ululu{id:1-3}/{id}", "123456"));
        Equal("https://example.com/313456", UrlTemplates.Expand("https://example.com/{SUCHWERT: 3, 1, 3-6 }", "123456"));
        Equal("https://example.com/000123/000", UrlTemplates.Expand("https://example.com/{id}/{id:1-3}", "000123"));
    });
    Check("Selection happens before URL encoding and keeps Unicode characters intact", () =>
    {
        Equal("https://example.com/%2F%26/%C3%A4", UrlTemplates.Expand("https://example.com/{id:3,2}/{id:1}", "ä&/"));
        Equal("https://example.com/%F0%9F%98%80/e%CC%81", UrlTemplates.Expand("https://example.com/{id:2}/{id:3}", "A😀e\u0301Z"));
    });
    Check("Malformed, out-of-range and unknown selections fail before capture", () =>
    {
        foreach (var selector in new[] { "", "0", "-1", "7", "3-1", "1-7", "1,,2", "1,", "x", "1:3", "99999999999999999" })
            Throws<ArgumentException>(() => UrlTemplates.Expand("https://example.com/{id:" + selector + "}", "123456"));
        Throws<ArgumentException>(() => UrlTemplates.Expand("https://{id:1-3}.example.com/", "123456"));
        Throws<ArgumentException>(() => new CollectionSession(["123456", "123"], ["https://example.com/{id:4-6}"]));
        var longId = new CollectionSession(["123456789"], ["https://example.com/{id:9}"]);
        Equal("https://example.com/9", longId.Current.Url);
    });
    Check("Title rules support prefixes and literal substrings without case sensitivity", () =>
    {
        var defaults = new TitleFilter(TitleFilter.DefaultRules);
        Equal(true, defaults.RejectionReason("  WAYBACK\nMachine - Internet Archive") is not null);
        Equal(true, defaults.RejectionReason("Error 404 - Gone") is not null);
        Equal<string?>(null, defaults.RejectionReason("Using the Wayback Machine"));
        Equal<string?>(null, defaults.RejectionReason("The file was not found"));
        Equal(true, defaults.RejectionReason(new string('a', 600) + " 404") is not null);
        var custom = new TitleFilter("nicht gefunden\r\n^Access Denied\r\n[missing]");
        Equal(true, custom.RejectionReason("Seite NICHT GEFUNDEN") is not null);
        Equal(true, custom.RejectionReason("Article [missing]") is not null);
        Equal<string?>(null, custom.RejectionReason("Article missing"));
        Throws<ArgumentException>(() => new TitleFilter("^"));
        Equal(true, new TitleFilter("").RejectionReason(" \n ") is not null);
        Equal<string?>(null, new TitleFilter("").RejectionReason("404"));
    });
    Check("Version 0.1 settings retain inputs and enable the new automatic defaults", () =>
    {
        const string json = """{"Values":"123456","Templates":"https://example.com/{id}","OutputDirectory":"C:\\Pages","BrowserPath":"","DelaySeconds":"1,5","AutoPreview":true}""";
        var settings = JsonSerializer.Deserialize<CollectorSettings>(json)!;
        Equal("123456", settings.Values);
        Equal(true, settings.Automatic);
        Equal(true, settings.IncludeTitle);
        Equal("", settings.Prefix);
        var edited = settings with { Automatic = false, ExcludedTitles = "", Prefix = "KB", Suffix = "_DEU", IncludeTitle = false };
        Equal(edited, JsonSerializer.Deserialize<CollectorSettings>(JsonSerializer.Serialize(edited))!);
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
    Check("Filenames combine affixes, value and title without allowing paths", () =>
    {
        Equal("KB123456_DEU - Ein lesbarer Titel", ArchiveNaming.BuildStem("123456", "Ein lesbarer Titel", new("KB", "_DEU")));
        Equal("KB123456_DEU", ArchiveNaming.BuildStem("123456", "Titel", new("KB", "_DEU", false)));
        Equal("123456", ArchiveNaming.BuildStem("123456", "", new()));
        Equal("123456 - A_B_ _Test_", ArchiveNaming.BuildStem("123456", " A:B? \"Test\". ", new()));
        Equal(false, ArchiveNaming.BuildStem("../../123", "C:/..\\title", new("../", "/DEU")).Contains('/'));
        Equal("_NUL.txt", ArchiveNaming.BuildStem("NUL.txt", "", new()));
        Throws<ArgumentException>(() => ArchiveNaming.BuildStem("123", "Title", new(new string('x', 41))));
    });
    Check("Titles are capped at 100 characters and cut at complete word boundaries", () =>
    {
        var firstWord = new string('a', 90);
        Equal(firstWord, ArchiveNaming.ShortTitle(firstWord + " incompleteword"));
        var exact = new string('a', 95) + " word";
        Equal(100, exact.Length);
        Equal(exact, ArchiveNaming.ShortTitle(exact + " next"));
        Equal(exact, ArchiveNaming.ShortTitle(exact));
        Equal(new string('x', 100), ArchiveNaming.ShortTitle(new string('x', 130)));
        Equal(new string('x', 98), ArchiveNaming.ShortTitle(new string('x', 98) + "e\u0301\u0301more"));
        var maximum = ArchiveNaming.BuildStem(new string('v', 100), new string('t', 200), new(new string('p', 40), new string('s', 40)));
        Equal(true, maximum.Length <= 220);
        Equal(true, maximum.Contains(new string('s', 40) + " - "));
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
        Equal("123456 - Überschrift.html", Path.GetFileName(one.HtmlPath));
        Equal("123456 - Überschrift (2).html", Path.GetFileName(two.HtmlPath));
        Equal(original, await File.ReadAllTextAsync(one.HtmlPath));
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(one.MetadataPath));
        Equal(candidate.Url, metadata.RootElement.GetProperty("requestedUrl").GetString());
        Equal("manual", metadata.RootElement.GetProperty("approval").GetString());
        Equal(64, metadata.RootElement.GetProperty("sha256").GetString()!.Length);
        Equal(true, File.Exists(fixture));
    });
    await CheckAsync("Long original titles are preserved in metadata and byte-identical HTML", async () =>
    {
        var longTitle = string.Join(' ', Enumerable.Repeat("Vollständiger Seitentitel", 20));
        var saved = await ArchiveStore.SaveAsync(Path.Combine(root, "long-title"), candidate, capture with { Title = longTitle },
            CancellationToken.None, new("KB", "_DEU"), automatic: true);
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(saved.MetadataPath));
        Equal(longTitle, metadata.RootElement.GetProperty("title").GetString());
        Equal("automatic-title-filter", metadata.RootElement.GetProperty("approval").GetString());
        Equal(original, await File.ReadAllTextAsync(saved.HtmlPath));
        Equal(true, Path.GetFileName(saved.HtmlPath).StartsWith("KB123456_DEU - "));
    });
    await CheckAsync("HTML title extraction ignores comments, styles, scripts and inert templates", async () =>
    {
        var file = Path.Combine(root, "title.html");
        await File.WriteAllTextAsync(file, "<!doctype html><!-- <title>404 fake</title> --><html><head>" +
            "<style>/* <title>404 fake</title> */" + new string('x', 300000) + "</style>" +
            "<script>const x = '<title>404 fake</title>';</script>" +
            "<template><template><title>404 fake</title></template></template>" +
            "<TITLE data-x='>'>  Original &amp; vollständiger\nTitel &#x1F600; </TiTlE></head><body></body></html>");
        Equal("Original & vollständiger Titel 😀", await HtmlTitleReader.ReadAsync(file, CancellationToken.None));
        await File.WriteAllTextAsync(file, "<html><head></head><body><title>404 in body</title></body></html>");
        Equal("", await HtmlTitleReader.ReadAsync(file, CancellationToken.None));
        await File.WriteAllTextAsync(file, "Not HTML");
        await ThrowsAsync<IOException>(() => HtmlTitleReader.ReadAsync(file, CancellationToken.None));
    });
    await CheckAsync("HTML raw title text and entities are interpreted once", async () =>
    {
        var file = Path.Combine(root, "raw-title.html");
        await File.WriteAllTextAsync(file, "<html><head><title>Use &lt;title&gt;, &amp;amp; and </titleX> as text</title></head></html>");
        Equal("Use <title>, &amp; and </titleX> as text", await HtmlTitleReader.ReadAsync(file, CancellationToken.None));
        await File.WriteAllTextAsync(file, "<html><head><title>Incomplete");
        await ThrowsAsync<IOException>(() => HtmlTitleReader.ReadAsync(file, CancellationToken.None));
    });
    await CheckAsync("Automatic collection rejects dead ends, saves the first good source and advances", async () =>
    {
        var output = Path.Combine(root, "automatic");
        var temporary = Path.Combine(root, "automatic-temp");
        var attempts = new List<string>();
        var events = new List<CollectionEvent>();
        var queue = new CollectionSession(["123456", "654321"], ["https://example.com/a/{id}", "https://example.com/b/{id}", "https://example.com/c/{id}", "https://example.com/d/{id}"]);
        var runner = new AutomaticCollector(async (current, target, token) =>
        {
            attempts.Add($"{current.Value}:{current.TemplateNumber}");
            await File.WriteAllTextAsync(target, original, token);
            var title = current.TemplateNumber switch { 1 => "Wayback Machine - Calendar", 2 => "404 Not Found", _ => "Original title" };
            return new(target, title, DateTimeOffset.UtcNow);
        }, TimeSpan.Zero);
        await runner.RunAsync(queue, output, temporary, new(TitleFilter.DefaultRules), new("KB", "_DEU"), events.Add, CancellationToken.None);
        Equal("123456:1,123456:2,123456:3,654321:1,654321:2,654321:3", string.Join(',', attempts));
        Equal(2, queue.AcceptedCount);
        Equal(true, queue.IsComplete);
        Equal(4, events.Count(e => e.Code == "TitleRejected"));
        Equal(2, Directory.GetFiles(output, "*.html").Length);
        Equal(0, Directory.GetFiles(temporary).Length);
        Equal(true, File.Exists(Path.Combine(output, "KB123456_DEU - Original title.html")));
    });
    await CheckAsync("Automatic failures and exhausted values continue without saving errors", async () =>
    {
        var output = Path.Combine(root, "automatic-empty");
        var temporary = Path.Combine(root, "automatic-empty-temp");
        Directory.CreateDirectory(output);
        var events = new List<CollectionEvent>();
        var queue = new CollectionSession(["one", "two"], ["https://example.com/a/{id}", "https://example.com/b/{id}"]);
        var runner = new AutomaticCollector(async (current, target, token) =>
        {
            if (current.TemplateNumber == 1) throw new TimeoutException("Deliberate timeout");
            await File.WriteAllTextAsync(target, original, token);
            return new(target, "", DateTimeOffset.UtcNow);
        }, TimeSpan.Zero);
        await runner.RunAsync(queue, output, temporary, new(TitleFilter.DefaultRules), new(), events.Add, CancellationToken.None);
        Equal(true, queue.IsComplete);
        Equal(0, queue.AcceptedCount);
        Equal(2, events.Count(e => e.Code == "NoAcceptedCandidate"));
        Equal(2, events.Count(e => e.Code == "CaptureFailed"));
        Equal(0, Directory.GetFiles(output).Length);
        Equal(0, Directory.GetFiles(temporary).Length);
    });
    await CheckAsync("Cancellation preserves accepted results and does not advance the next value", async () =>
    {
        var output = Path.Combine(root, "automatic-cancel");
        var temporary = Path.Combine(root, "automatic-cancel-temp");
        using var cancellation = new CancellationTokenSource();
        var queue = new CollectionSession(["one", "two", "three"], ["https://example.com/{id}"]);
        var runner = new AutomaticCollector(async (current, target, token) =>
        {
            await File.WriteAllTextAsync(target, original, token);
            if (current.Value == "two") cancellation.Cancel();
            return new(target, "Original title", DateTimeOffset.UtcNow);
        }, TimeSpan.Zero);
        await ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(queue, output, temporary, new(""), new(), _ => { }, cancellation.Token));
        Equal(1, queue.AcceptedCount);
        Equal("two", queue.Current.Value);
        Equal(1, Directory.GetFiles(output, "*.html").Length);
        Equal(0, Directory.GetFiles(temporary).Length);
    });
    await CheckAsync("Save failures stop automatic processing without falsely accepting or trying later sources", async () =>
    {
        var output = Path.Combine(root, "cannot-be-a-directory");
        await File.WriteAllTextAsync(output, "occupied");
        var temporary = Path.Combine(root, "automatic-save-failure-temp");
        var events = new List<CollectionEvent>();
        var queue = new CollectionSession(["one", "two"], ["https://example.com/a/{id}", "https://example.com/b/{id}"]);
        var runner = new AutomaticCollector(async (current, target, token) =>
        {
            await File.WriteAllTextAsync(target, original, token);
            return new(target, "Original title", DateTimeOffset.UtcNow);
        }, TimeSpan.Zero);
        await ThrowsAsync<IOException>(() => runner.RunAsync(queue, output, temporary, new(""), new(), events.Add, CancellationToken.None));
        Equal(0, queue.AcceptedCount);
        Equal("one", queue.Current.Value);
        Equal(1, queue.Current.TemplateNumber);
        Equal(1, events.Count(e => e.Code == "CaptureStarted"));
        Equal(1, events.Count(e => e.Code == "SaveFailed"));
        Equal(0, Directory.GetFiles(temporary).Length);
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
        await CheckAsync("The process capture pipeline applies title rules and saves without manual approval", async () =>
        {
            var script = Path.Combine(root, "successful-engine");
            await File.WriteAllTextAsync(script, """
                #!/bin/sh
                case "$1" in
                  */a/*) title='Wayback Machine - Test' ;;
                  *) title='Original &amp; article' ;;
                esac
                printf '<!doctype html><html><head><title>%s</title></head><body>Content</body></html>' "$title" > "$2"
                """ + "\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var queue = new CollectionSession(["123456"], ["https://example.com/a/{id:1-3}/{id}", "https://example.com/b/{id}"]);
            var output = Path.Combine(root, "process captures with spaces");
            var temporary = Path.Combine(root, "process temporary files");
            var runner = new AutomaticCollector((current, target, token) =>
                new CaptureRunner().CaptureAsync(current, target, new(script, "/unused browser"), token), TimeSpan.Zero);
            var events = new List<CollectionEvent>();
            await runner.RunAsync(queue, output, temporary, new(TitleFilter.DefaultRules), new("KB", "_DEU"), events.Add, CancellationToken.None);
            Equal(1, queue.AcceptedCount);
            Equal(1, events.Count(e => e.Code == "TitleRejected"));
            Equal(true, File.Exists(Path.Combine(output, "KB123456_DEU - Original & article.html")));
        });
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
        await CheckAsync("Real automatic capture skips archive and soft-404 pages, then saves the first article", async () =>
        {
            var fixtureUri = new Uri(args[2]);
            var templates = new[] { "wayback.html", "not-found.html", "index.html", "must-not-be-visited.html" }
                .Select(file => new Uri(fixtureUri, file).AbsoluteUri + "?article={id}&group={id:1-3}");
            var queue = new CollectionSession(["123456"], templates);
            var output = Path.Combine(Path.GetFullPath(args[3]), "automatic");
            var temporary = Path.Combine(Path.GetFullPath(args[3]), "temporary");
            var events = new List<CollectionEvent>();
            var browserArguments = OperatingSystem.IsLinux() ? new[] { "--no-sandbox" } : null;
            var runner = new AutomaticCollector((current, target, token) => new CaptureRunner().CaptureAsync(current,
                target, new(Path.GetFullPath(args[0]), args[1], 1500, 150, browserArguments), token));
            await runner.RunAsync(queue, output, temporary, new(TitleFilter.DefaultRules), new("KB", "_DEU"), progress =>
            {
                events.Add(progress);
                Console.WriteLine($"AUTO {progress.Candidate.TemplateNumber} {progress.Code} {progress.Details}");
            }, CancellationToken.None);
            Equal(1, queue.AcceptedCount);
            Equal(3, events.Count(e => e.Code == "CaptureStarted"));
            Equal(2, events.Count(e => e.Code == "TitleRejected"));
            var saved = events.Single(e => e.Code == "Accepted");
            Equal("KB123456_DEU - PageCollector fixture.html", Path.GetFileName(saved.FilePath));
            Equal(true, (await File.ReadAllTextAsync(saved.FilePath)).Contains("Dynamisch ergänzter Text"));
            Equal(0, Directory.GetFiles(temporary).Length);
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
