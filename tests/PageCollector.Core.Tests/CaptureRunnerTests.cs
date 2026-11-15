using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PageCollector.Core;

namespace PageCollector.Core.Tests;

[TestClass]
public sealed class CaptureRunnerTests : TestWorkspace
{
    private static string EnginePath => Path.Combine(AppContext.BaseDirectory, "CaptureFixture", OperatingSystem.IsWindows() ? "PageCollector.CaptureFixture.exe" : "PageCollector.CaptureFixture");

    [TestMethod]
    public async Task ProcessPipelineFiltersTitlesAndSavesAutomaticallyAsync()
    {
        var queue = new CollectionSession(["123456"], ["https://example.com/a/{id:1-3}/{id}", "https://example.com/b/{id}"]);
        var output = Path.Combine(Root, "process captures with spaces");
        var temporary = Path.Combine(Root, "process temporary files");
        var runner = new AutomaticCollector((current, target, token) => new CaptureRunner().CaptureAsync(current, target, new(EnginePath, "/unused browser"), token), TimeSpan.Zero);
        var events = new List<CollectionEvent>();
        await runner.RunAsync(queue, output, temporary, new(TitleFilter.DefaultRules), new("KB", "_DEU"), events.Add, CancellationToken.None);

        Assert.AreEqual(1, queue.AcceptedCount);
        Assert.AreEqual(1, events.Count(e => e.Code == "TitleRejected"));
        Assert.IsTrue(File.Exists(Path.Combine(output, "KB123456_DEU - Original & article.html")));
    }

    [TestMethod]
    [DataRow("fail")]
    [DataRow("empty")]
    [DataRow("nonhtml")]
    public async Task InvalidCaptureCannotBeAcceptedAsync(string mode)
    {
        await Assert.ThrowsAsync<IOException>(() => CaptureAsync(mode, CancellationToken.None));
    }

    [TestMethod]
    public async Task CancellationTerminatesRunningProcessPromptlyAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var target = Path.Combine(Root, "process.html");
        var task = CaptureAsync("slow", cancellation.Token);

        try
        {
            await WaitForStartedAsync(target + ".started");
            var clock = Stopwatch.StartNew();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => task);

            Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(5));
            Assert.IsFalse(File.Exists(target));
        }
        finally
        {
            cancellation.Cancel();

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [TestMethod]
    public async Task TimeoutIsDistinctFromUserCancellationAsync()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => new CaptureRunner().CaptureAsync(Candidate with { Url = "https://example.com/slow/123456" }, Path.Combine(Root, "timeout.html"), new(EnginePath, "/unused", TimeoutSeconds: 1), CancellationToken.None));
    }

    [TestMethod]
    public async Task BothDiagnosticStreamsAreDrainedWithoutDeadlockAsync()
    {
        var result = await CaptureAsync("flood", CancellationToken.None);

        Assert.AreEqual("Original & article", result.Title);
    }

    [TestMethod]
    public async Task InvalidEngineHashIsRejectedAsync()
    {
        Assert.IsFalse(await EngineInstaller.IsValidAsync(Fixture, CancellationToken.None));
    }

    private Task<CaptureResult> CaptureAsync(string mode, CancellationToken cancellationToken)
    {
        return new CaptureRunner().CaptureAsync(Candidate with { Url = $"https://example.com/{mode}/123456" }, Path.Combine(Root, "process.html"), new(EnginePath, "/unused", TimeoutSeconds: 10), cancellationToken);
    }

    private static async Task WaitForStartedAsync(string marker)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!File.Exists(marker))
        {
            await Task.Delay(25, deadline.Token);
        }
    }
}