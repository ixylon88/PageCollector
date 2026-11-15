using System.Text;

// Implements SingleFile's URL/output argument shape without a browser or network.
// The same executable exercises CaptureRunner on Windows and Linux.
if (args.Length < 2)
{
    return 2;
}

var url = new Uri(args[0]);
var output = args[1];
if (url.AbsolutePath.Contains("/fail/", StringComparison.Ordinal))
{
    await Console.Error.WriteAsync("Deliberate capture failure");

    return 7;
}

if (url.AbsolutePath.Contains("/slow/", StringComparison.Ordinal))
{
    await File.WriteAllTextAsync(output + ".started", "ready");
    await Task.Delay(TimeSpan.FromSeconds(30));
}

if (url.AbsolutePath.Contains("/empty/", StringComparison.Ordinal))
{
    return 0;
}

if (url.AbsolutePath.Contains("/flood/", StringComparison.Ordinal))
{
    await Task.WhenAll(Console.Out.WriteAsync(new string('o', 200000)), Console.Error.WriteAsync(new string('e', 200000)));
}

var title = url.AbsolutePath.Contains("/a/", StringComparison.Ordinal) ? "Wayback Machine - Test" : "Original &amp; article";
var html = url.AbsolutePath.Contains("/nonhtml/", StringComparison.Ordinal) ? "Not an HTML document" : $"<!doctype html><html><head><title>{title}</title></head><body>Content</body></html>";
await File.WriteAllTextAsync(output, html, new UTF8Encoding(false));
return 0;