using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PageCollector.Core;

namespace PageCollector.Core.Tests;

public abstract class TestWorkspace
{
    protected const string Original = "<!doctype html><html><head><title>Überschrift</title></head><body><h1>Lesbarer Inhalt</h1></body></html>";

    protected string Root { get; private set; } = "";

    protected string Fixture { get; private set; } = "";

    protected Candidate Candidate { get; private set; } = null!;

    protected CaptureResult Capture { get; private set; } = null!;

    [TestInitialize]
    public async Task CreateWorkspaceAsync()
    {
        Root = Path.Combine(Path.GetTempPath(), "pagecollector-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Fixture = Path.Combine(Root, "fixture.html");
        await File.WriteAllTextAsync(Fixture, Original, new UTF8Encoding(false));
        Candidate = new("123456", "https://example.com/{id}", "https://example.com/123456", 1, 1);
        Capture = new(Fixture, "Überschrift", DateTimeOffset.UtcNow);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}