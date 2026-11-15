namespace PageCollector.Core.Tests;

[TestClass]
public sealed class TitleFilterTests : TestWorkspace
{
    [TestMethod]
    public void TitleRulesSupportPrefixesAndLiteralSubstringsWithoutCaseSensitivity()
    {
        var defaults = new TitleFilter(TitleFilter.DefaultRules);

        Assert.IsTrue(defaults.RejectionReason("  WAYBACK\nMachine - Internet Archive") is not null);
        Assert.IsTrue(defaults.RejectionReason("Error 404 - Gone") is not null);
        Assert.IsNull(defaults.RejectionReason("Using the Wayback Machine"));
        Assert.IsNull(defaults.RejectionReason("The file was not found"));
        Assert.IsTrue(defaults.RejectionReason(new string('a', 600) + " 404") is not null);
        var custom = new TitleFilter("nicht gefunden\r\n^Access Denied\r\n[missing]");

        Assert.IsTrue(custom.RejectionReason("Seite NICHT GEFUNDEN") is not null);
        Assert.IsTrue(custom.RejectionReason("Article [missing]") is not null);
        Assert.IsNull(custom.RejectionReason("Article missing"));
        Assert.Throws<ArgumentException>(() => new TitleFilter("^"));
        Assert.IsTrue(new TitleFilter("").RejectionReason(" \n ") is not null);
        Assert.IsNull(new TitleFilter("").RejectionReason("404"));
    }
}