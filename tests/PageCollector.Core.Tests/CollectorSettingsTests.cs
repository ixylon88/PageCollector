using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PageCollector.Core;

namespace PageCollector.Core.Tests;

[TestClass]
public sealed class CollectorSettingsTests : TestWorkspace
{
    [TestMethod]
    public void Version01SettingsRetainInputsAndEnableTheNewAutomaticDefaults()
    {
        const string json = """{"Values":"123456","Templates":"https://example.com/{id}","OutputDirectory":"C:\\Pages","BrowserPath":"","DelaySeconds":"1,5","AutoPreview":true}""";
        var settings = JsonSerializer.Deserialize<CollectorSettings>(json)!;

        Assert.AreEqual("123456", settings.Values);
        Assert.IsTrue(settings.Automatic);
        Assert.IsTrue(settings.IncludeTitle);
        Assert.AreEqual("", settings.Prefix);
        var edited = settings with
        {
            Automatic = false,
            ExcludedTitles = "",
            Prefix = "KB",
            Suffix = "_DEU",
            IncludeTitle = false
        };

        Assert.AreEqual(edited, JsonSerializer.Deserialize<CollectorSettings>(JsonSerializer.Serialize(edited))!);
    }
}