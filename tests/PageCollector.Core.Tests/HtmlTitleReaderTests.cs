namespace PageCollector.Core.Tests;

[TestClass]
public sealed class HtmlTitleReaderTests : TestWorkspace
{
    [TestMethod]
    public async Task HTMLTitleExtractionIgnoresCommentsStylesScriptsAndInertTemplatesAsync()
    {
        var file = Path.Combine(Root, "title.html");
        await File.WriteAllTextAsync(file, "<!doctype html><!-- <title>404 fake</title> --><html><head>" + "<style>/* <title>404 fake</title> */" + new string('x', 300000) + "</style>" + "<script>const x = '<title>404 fake</title>';</script>" + "<template><template><title>404 fake</title></template></template>" + "<TITLE data-x='>'>  Original &amp; vollständiger\nTitel &#x1F600; </TiTlE></head><body></body></html>");

        Assert.AreEqual("Original & vollständiger Titel 😀", await HtmlTitleReader.ReadAsync(file, CancellationToken.None));
        await File.WriteAllTextAsync(file, "<html><head></head><body><title>404 in body</title></body></html>");

        Assert.AreEqual("", await HtmlTitleReader.ReadAsync(file, CancellationToken.None));
        await File.WriteAllTextAsync(file, "Not HTML");
        await Assert.ThrowsAsync<IOException>(() => HtmlTitleReader.ReadAsync(file, CancellationToken.None));
    }

    [TestMethod]
    public async Task HTMLRawTitleTextAndEntitiesAreInterpretedOnceAsync()
    {
        var file = Path.Combine(Root, "raw-title.html");
        await File.WriteAllTextAsync(file, "<html><head><title>Use &lt;title&gt;, &amp;amp; and </titleX> as text</title></head></html>");

        Assert.AreEqual("Use <title>, &amp; and </titleX> as text", await HtmlTitleReader.ReadAsync(file, CancellationToken.None));
        await File.WriteAllTextAsync(file, "<html><head><title>Incomplete");
        await Assert.ThrowsAsync<IOException>(() => HtmlTitleReader.ReadAsync(file, CancellationToken.None));
    }
}