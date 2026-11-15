using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PageCollector.Core;

namespace PageCollector.Core.Tests;

[TestClass]
public sealed class ArchiveStoreTests : TestWorkspace
{
    [TestMethod]
    public async Task AcceptedFilesPreserveBytesIncludeProvenanceAndNeverOverwriteAsync()
    {
        var output = Path.Combine(Root, "archives");
        var one = await ArchiveStore.SaveAsync(output, Candidate, Capture, CancellationToken.None);
        var two = await ArchiveStore.SaveAsync(output, Candidate, Capture, CancellationToken.None);

        Assert.AreEqual("123456 - Überschrift.html", Path.GetFileName(one.HtmlPath));
        Assert.AreEqual("123456 - Überschrift (2).html", Path.GetFileName(two.HtmlPath));
        Assert.AreEqual(Original, await File.ReadAllTextAsync(one.HtmlPath));
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(one.MetadataPath));

        Assert.AreEqual(Candidate.Url, metadata.RootElement.GetProperty("requestedUrl").GetString());
        Assert.AreEqual("manual", metadata.RootElement.GetProperty("approval").GetString());
        Assert.AreEqual(64, metadata.RootElement.GetProperty("sha256").GetString()!.Length);
        Assert.IsTrue(File.Exists(Fixture));
    }

    [TestMethod]
    public async Task LongOriginalTitlesArePreservedInMetadataAndByteIdenticalHTMLAsync()
    {
        var longTitle = string.Join(' ', Enumerable.Repeat("Vollständiger Seitentitel", 20));
        var saved = await ArchiveStore.SaveAsync(Path.Combine(Root, "long-title"), Candidate, Capture with { Title = longTitle }, CancellationToken.None, new("KB", "_DEU"), automatic: true);
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(saved.MetadataPath));

        Assert.AreEqual(longTitle, metadata.RootElement.GetProperty("title").GetString());
        Assert.AreEqual("automatic-title-filter", metadata.RootElement.GetProperty("approval").GetString());
        Assert.AreEqual(Original, await File.ReadAllTextAsync(saved.HtmlPath));
        Assert.IsTrue(Path.GetFileName(saved.HtmlPath).StartsWith("KB123456_DEU - ", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CancelledSavesNeverCreateAnAcceptedArchiveAsync()
    {
        var output = Path.Combine(Root, "cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ArchiveStore.SaveAsync(output, Candidate, Capture, cancellation.Token));

        Assert.AreEqual(0, Directory.GetFiles(output).Length);
    }
}