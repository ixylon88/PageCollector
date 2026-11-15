namespace PageCollector.Core.Tests;

[TestClass]
public sealed class CollectionSessionTests : TestWorkspace
{
    [TestMethod]
    public void TheFirstAcceptedCandidateStopsThatValueSTemplateSequence()
    {
        var session = new CollectionSession(["123", "456"], ["https://example.com/a/{id}", "https://example.com/b/{id}", "https://example.com/c/{id}"]);

        Assert.AreEqual("https://example.com/a/123", session.Current.Url);
        Assert.IsNull(session.RejectCandidate());
        Assert.AreEqual("https://example.com/b/123", session.Current.Url);
        session.Accept();

        Assert.AreEqual("https://example.com/a/456", session.Current.Url);
        Assert.AreEqual(1, session.AcceptedCount);
        session.SkipValue();

        Assert.IsTrue(session.IsComplete);
    }

    [TestMethod]
    public void ExhaustedValuesAdvanceMerelyReadingOrRetryingDoesNot()
    {
        var session = new CollectionSession(["first", "second"], ["https://example.com/{id}"]);
        var retry = session.Current;

        Assert.AreEqual(retry, session.Current);
        Assert.AreEqual("first", session.RejectCandidate());
        Assert.AreEqual("second", session.Current.Value);
        Assert.AreEqual("second", session.RejectCandidate());
        Assert.IsTrue(session.IsComplete);
        Assert.AreEqual(0, session.AcceptedCount);
    }
}