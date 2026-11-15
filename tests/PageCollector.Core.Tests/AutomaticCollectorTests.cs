using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PageCollector.Core;

namespace PageCollector.Core.Tests;

[TestClass]
public sealed class AutomaticCollectorTests : TestWorkspace
{
    [TestMethod]
    public async Task AutomaticCollectionRejectsDeadEndsSavesTheFirstGoodSourceAndAdvancesAsync()
    {
        var output = Path.Combine(Root, "automatic");
        var temporary = Path.Combine(Root, "automatic-temp");
        var attempts = new List<string>();
        var events = new List<CollectionEvent>();
        var queue = new CollectionSession(["123456", "654321"], ["https://example.com/a/{id}", "https://example.com/b/{id}", "https://example.com/c/{id}", "https://example.com/d/{id}"]);
        var runner = new AutomaticCollector(async (current, target, token) =>
        {
            attempts.Add($"{current.Value}:{current.TemplateNumber}");
            await File.WriteAllTextAsync(target, Original, token);
            var title = current.TemplateNumber switch
            {
                1 => "Wayback Machine - Calendar",
                2 => "404 Not Found",
                _ => "Original title"
            };

            return new(target, title, DateTimeOffset.UtcNow);
        }, TimeSpan.Zero);
        await runner.RunAsync(queue, output, temporary, new(TitleFilter.DefaultRules), new("KB", "_DEU"), events.Add, CancellationToken.None);

        Assert.AreEqual("123456:1,123456:2,123456:3,654321:1,654321:2,654321:3", string.Join(',', attempts));
        Assert.AreEqual(2, queue.AcceptedCount);
        Assert.IsTrue(queue.IsComplete);
        Assert.AreEqual(4, events.Count(e => e.Code == "TitleRejected"));
        Assert.AreEqual(2, Directory.GetFiles(output, "*.html").Length);
        Assert.AreEqual(0, Directory.GetFiles(temporary).Length);
        Assert.IsTrue(File.Exists(Path.Combine(output, "KB123456_DEU - Original title.html")));
    }

    [TestMethod]
    public async Task AutomaticFailuresAndExhaustedValuesContinueWithoutSavingErrorsAsync()
    {
        var output = Path.Combine(Root, "automatic-empty");
        var temporary = Path.Combine(Root, "automatic-empty-temp");
        Directory.CreateDirectory(output);
        var events = new List<CollectionEvent>();
        var queue = new CollectionSession(["one", "two"], ["https://example.com/a/{id}", "https://example.com/b/{id}"]);
        var runner = new AutomaticCollector(async (current, target, token) =>
        {
            if (current.TemplateNumber == 1)
            {
                throw new TimeoutException("Deliberate timeout");
            }

            await File.WriteAllTextAsync(target, Original, token);

            return new(target, "", DateTimeOffset.UtcNow);
        }, TimeSpan.Zero);
        await runner.RunAsync(queue, output, temporary, new(TitleFilter.DefaultRules), new(), events.Add, CancellationToken.None);

        Assert.IsTrue(queue.IsComplete);
        Assert.AreEqual(0, queue.AcceptedCount);
        Assert.AreEqual(2, events.Count(e => e.Code == "NoAcceptedCandidate"));
        Assert.AreEqual(2, events.Count(e => e.Code == "CaptureFailed"));
        Assert.AreEqual(0, Directory.GetFiles(output).Length);
        Assert.AreEqual(0, Directory.GetFiles(temporary).Length);
    }

    [TestMethod]
    public async Task CancellationPreservesAcceptedResultsAndDoesNotAdvanceTheNextValueAsync()
    {
        var output = Path.Combine(Root, "automatic-cancel");
        var temporary = Path.Combine(Root, "automatic-cancel-temp");
        using var cancellation = new CancellationTokenSource();
        var queue = new CollectionSession(["one", "two", "three"], ["https://example.com/{id}"]);
        var runner = new AutomaticCollector(async (current, target, token) =>
        {
            await File.WriteAllTextAsync(target, Original, token);

            if (current.Value == "two")
            {
                cancellation.Cancel();
            }

            return new(target, "Original title", DateTimeOffset.UtcNow);
        }, TimeSpan.Zero);
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(queue, output, temporary, new(""), new(), _ =>
        {
        }, cancellation.Token));

        Assert.AreEqual(1, queue.AcceptedCount);
        Assert.AreEqual("two", queue.Current.Value);
        Assert.AreEqual(1, Directory.GetFiles(output, "*.html").Length);
        Assert.AreEqual(0, Directory.GetFiles(temporary).Length);
    }

    [TestMethod]
    public async Task SaveFailuresStopAutomaticProcessingWithoutFalselyAcceptingOrTryingLaterSourcesAsync()
    {
        var output = Path.Combine(Root, "cannot-be-a-directory");
        await File.WriteAllTextAsync(output, "occupied");
        var temporary = Path.Combine(Root, "automatic-save-failure-temp");
        var events = new List<CollectionEvent>();
        var queue = new CollectionSession(["one", "two"], ["https://example.com/a/{id}", "https://example.com/b/{id}"]);
        var runner = new AutomaticCollector(async (current, target, token) =>
        {
            await File.WriteAllTextAsync(target, Original, token);

            return new(target, "Original title", DateTimeOffset.UtcNow);
        }, TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => runner.RunAsync(queue, output, temporary, new(""), new(), events.Add, CancellationToken.None));

        Assert.AreEqual(0, queue.AcceptedCount);
        Assert.AreEqual("one", queue.Current.Value);
        Assert.AreEqual(1, queue.Current.TemplateNumber);
        Assert.AreEqual(1, events.Count(e => e.Code == "CaptureStarted"));
        Assert.AreEqual(1, events.Count(e => e.Code == "SaveFailed"));
        Assert.AreEqual(0, Directory.GetFiles(temporary).Length);
    }
}