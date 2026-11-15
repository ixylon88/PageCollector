using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PageCollector.Core;

namespace PageCollector.Core.Tests;

[TestClass]
public sealed class ArchiveNamingTests : TestWorkspace
{
    [TestMethod]
    public void WindowsFilenamesCannotEscapeTheOutputDirectory()
    {
        Assert.AreEqual("_.._Q12_34", ArchiveStore.SafeFileName("../../Q12:34"));
    }

    [TestMethod]
    public void FilenameEdgeCasesAreSafeOnWindows()
    {
        foreach (var value in new[]
        {
            "../../Q12:34",
            "CON",
            "NUL.txt",
            "LPT1",
            "COM¹.txt",
            "a\\b",
            "<>:\"/?*|",
            "...",
            ""
        })
        {
            var name = ArchiveStore.SafeFileName(value);

            Assert.IsFalse(name.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0);
            Assert.IsFalse(name.EndsWith('.'));
            Assert.IsFalse(name.Length == 0);
        }

        Assert.AreEqual("_CON", ArchiveStore.SafeFileName("CON"));
        Assert.AreEqual("_NUL.txt", ArchiveStore.SafeFileName("NUL.txt"));
    }

    [TestMethod]
    public void FilenamesCombineAffixesValueAndTitleWithoutAllowingPaths()
    {
        Assert.AreEqual("KB123456_DEU - Ein lesbarer Titel", ArchiveNaming.BuildStem("123456", "Ein lesbarer Titel", new("KB", "_DEU")));
        Assert.AreEqual("KB123456_DEU", ArchiveNaming.BuildStem("123456", "Titel", new("KB", "_DEU", false)));
        Assert.AreEqual("123456", ArchiveNaming.BuildStem("123456", "", new()));
        Assert.AreEqual("123456 - A_B_ _Test_", ArchiveNaming.BuildStem("123456", " A:B? \"Test\". ", new()));
        Assert.IsFalse(ArchiveNaming.BuildStem("../../123", "C:/..\\title", new("../", "/DEU")).Contains('/'));
        Assert.AreEqual("_NUL.txt", ArchiveNaming.BuildStem("NUL.txt", "", new()));
        Assert.Throws<ArgumentException>(() => ArchiveNaming.BuildStem("123", "Title", new(new string('x', 41))));
    }

    [TestMethod]
    public void TitlesAreCappedAt100CharactersAndCutAtCompleteWordBoundaries()
    {
        var firstWord = new string('a', 90);

        Assert.AreEqual(firstWord, ArchiveNaming.ShortTitle(firstWord + " incompleteword"));
        var exact = new string('a', 95) + " word";

        Assert.AreEqual(100, exact.Length);
        Assert.AreEqual(exact, ArchiveNaming.ShortTitle(exact + " next"));
        Assert.AreEqual(exact, ArchiveNaming.ShortTitle(exact));
        Assert.AreEqual(new string('x', 100), ArchiveNaming.ShortTitle(new string('x', 130)));
        Assert.AreEqual(new string('x', 98), ArchiveNaming.ShortTitle(new string('x', 98) + "e\u0301\u0301more"));
        var maximum = ArchiveNaming.BuildStem(new string('v', 100), new string('t', 200), new(new string('p', 40), new string('s', 40)));

        Assert.IsTrue(maximum.Length <= 220);
        Assert.IsTrue(maximum.Contains(new string('s', 40) + " - "));
    }
}