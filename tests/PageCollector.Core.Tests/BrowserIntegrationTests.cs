using PageCollector.Core;

namespace PageCollector.Core.Tests;

[TestClass]
[TestCategory("BrowserIntegration")]
public sealed class BrowserIntegrationTests : TestWorkspace
{
    private static readonly string[] FixtureNames = ["wayback.html", "not-found.html", "index.html", "must-not-be-visited.html"];

    [TestMethod]
    public async Task RealCapturePreservesStylesImagesAndDelayedContentAsync()
    {
        var (options, fixture) = GetConfiguration();
        var result = await new CaptureRunner().CaptureAsync(Candidate with { Url = fixture.AbsoluteUri }, Path.Combine(Root, "offline.html"), options, CancellationToken.None);
        var html = await File.ReadAllTextAsync(result.FilePath);

        Assert.AreEqual("PageCollector fixture", result.Title);
        Assert.Contains("Dynamisch ergänzter Text", html);
        Assert.Contains("data:image/svg+xml", html);
        Assert.IsTrue(html.Contains("#edf3fb", StringComparison.Ordinal) || html.Contains("rgb(237,243,251)", StringComparison.Ordinal) || html.Contains("237, 243, 251", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RealAutomaticCaptureRejectsDeadEndsAndStopsAtFirstArticleAsync()
    {
        var (options, fixture) = GetConfiguration();
        var templates = FixtureNames.Select(file => new Uri(fixture, file).AbsoluteUri + "?article={id}&group={id:1-3}");
        var queue = new CollectionSession(["123456"], templates);
        var temporary = Path.Combine(Root, "temporary");
        var events = new List<CollectionEvent>();
        var runner = new AutomaticCollector((current, target, token) => new CaptureRunner().CaptureAsync(current, target, options, token));
        await runner.RunAsync(queue, Path.Combine(Root, "saved"), temporary, new(TitleFilter.DefaultRules), new("KB", "_DEU"), events.Add, CancellationToken.None);

        Assert.AreEqual(1, queue.AcceptedCount);
        Assert.AreEqual(3, events.Count(e => e.Code == "CaptureStarted"));
        Assert.AreEqual(2, events.Count(e => e.Code == "TitleRejected"));
        var saved = events.Single(e => e.Code == "Accepted");

        Assert.AreEqual("KB123456_DEU - PageCollector fixture.html", Path.GetFileName(saved.FilePath));
        Assert.Contains("Dynamisch ergänzter Text", await File.ReadAllTextAsync(saved.FilePath));
        Assert.IsEmpty(Directory.GetFiles(temporary));
    }

    private static (CaptureOptions Options, Uri Fixture) GetConfiguration()
    {
        var engine = Environment.GetEnvironmentVariable("PAGECOLLECTOR_TEST_ENGINE");
        var browser = Environment.GetEnvironmentVariable("PAGECOLLECTOR_TEST_BROWSER");
        var fixture = Environment.GetEnvironmentVariable("PAGECOLLECTOR_TEST_URL");

        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(browser) || string.IsNullOrWhiteSpace(fixture))
        {
            Assert.Inconclusive("Set PAGECOLLECTOR_TEST_ENGINE, PAGECOLLECTOR_TEST_BROWSER and PAGECOLLECTOR_TEST_URL to enable browser integration tests.");
        }

        return (new CaptureOptions(Path.GetFullPath(engine), browser), new Uri(fixture));
    }
}