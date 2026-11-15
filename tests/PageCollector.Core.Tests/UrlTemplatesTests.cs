using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PageCollector.Core;

namespace PageCollector.Core.Tests;

[TestClass]
public sealed class UrlTemplatesTests : TestWorkspace
{
    [TestMethod]
    public void TemplateSubstitutionPreservesSuffixesAndEncodesOnlyValues()
    {
        Assert.AreEqual("https://example.com/k/Q123456.html", UrlTemplates.Expand("https://example.com/k/Q{id}.html", "123456"));
        Assert.AreEqual("https://example.com/?q=A%26B%2F%C3%A4%20%231", UrlTemplates.Expand("https://example.com/?q={Suchwert}", "A&B/ä #1"));
        Assert.AreEqual("https://example.com/%241?q=%241", UrlTemplates.Expand("https://example.com/{ID}?q={id}", "$1"));
    }

    [TestMethod]
    public void InvalidAndUnsafeTemplatesFailBeforeARunStarts()
    {
        Assert.Throws<ArgumentException>(() => UrlTemplates.Expand("file:///tmp/{id}", "1"));
        Assert.Throws<ArgumentException>(() => UrlTemplates.Expand("https://example.com/no-placeholder", "1"));
        Assert.Throws<ArgumentException>(() => UrlTemplates.Expand("https://{id}.example.com/", "1"));
        Assert.Throws<ArgumentException>(() => UrlTemplates.Expand("https://example.com/{other}/{id}", "1"));
        Assert.Throws<ArgumentException>(() => UrlTemplates.Expand("https://user:secret@example.com/{id}", "1"));
    }

    [TestMethod]
    public void OneBasedIndexesRangesAndTheOriginalPlaceholderCanBeCombined()
    {
        Assert.AreEqual("https://example.com/asdf123/4/56", UrlTemplates.Expand("https://example.com/asdf{id:1,2,3}/{id:4}/{id:5,6}", "123456"));
        Assert.AreEqual("https://example.com/asdf123/4/56", UrlTemplates.Expand("https://example.com/asdf{id:1-3}/{id:4}/{id:5-6}", "123456"));
        Assert.AreEqual("https://example.com/ululu123/123456", UrlTemplates.Expand("https://example.com/ululu{id:1-3}/{id}", "123456"));
        Assert.AreEqual("https://example.com/313456", UrlTemplates.Expand("https://example.com/{SUCHWERT: 3, 1, 3-6 }", "123456"));
        Assert.AreEqual("https://example.com/000123/000", UrlTemplates.Expand("https://example.com/{id}/{id:1-3}", "000123"));
    }

    [TestMethod]
    public void SelectionHappensBeforeURLEncodingAndKeepsUnicodeCharactersIntact()
    {
        Assert.AreEqual("https://example.com/%2F%26/%C3%A4", UrlTemplates.Expand("https://example.com/{id:3,2}/{id:1}", "ä&/"));
        Assert.AreEqual("https://example.com/%F0%9F%98%80/e%CC%81", UrlTemplates.Expand("https://example.com/{id:2}/{id:3}", "A😀e\u0301Z"));
    }

    [TestMethod]
    public void MalformedOutOfRangeAndUnknownSelectionsFailBeforeCapture()
    {
        foreach (var selector in new[]
        {
            "",
            "0",
            "-1",
            "7",
            "3-1",
            "1-7",
            "1,,2",
            "1,",
            "x",
            "1:3",
            "99999999999999999"
        })
        {
            Assert.Throws<ArgumentException>(() => UrlTemplates.Expand("https://example.com/{id:" + selector + "}", "123456"));
        }

        Assert.Throws<ArgumentException>(() => UrlTemplates.Expand("https://{id:1-3}.example.com/", "123456"));
        Assert.Throws<ArgumentException>(() => new CollectionSession(["123456", "123"], ["https://example.com/{id:4-6}"]));
        var longId = new CollectionSession(["123456789"], ["https://example.com/{id:9}"]);

        Assert.AreEqual("https://example.com/9", longId.Current.Url);
    }
}