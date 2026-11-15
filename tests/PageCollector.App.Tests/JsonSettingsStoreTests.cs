using System.IO;
using System.Text.Json;
using PageCollector.App.Services;
using PageCollector.Core;

namespace PageCollector.App.Tests;

[TestClass]
public sealed class JsonSettingsStoreTests : ViewModelWorkspace
{
    [TestMethod]
    public void SettingsAreWrittenAtomicallyAndLeaveNoTemporaryFiles()
    {
        var directory = Path.Combine(Root, "settings");
        var path = Path.Combine(directory, "settings.json");
        var store = new JsonSettingsStore(path);
        var settings = new CollectorSettings("123456", "https://example.com/{id}", "output", "", "1,5", true);

        Assert.IsNull(store.Load());
        store.Save(settings);

        Assert.AreEqual(settings, store.Load());
        var updated = settings with
        {
            Prefix = "KB",
            Suffix = "_DEU",
            Automatic = false
        };
        store.Save(updated);

        Assert.AreEqual(updated, store.Load());
        Assert.HasCount(1, Directory.GetFiles(directory));
    }

    [TestMethod]
    public void MalformedSettingsProduceARecoverableReadError()
    {
        var path = Path.Combine(Root, "settings.json");
        File.WriteAllText(path, "{invalid");

        Assert.Throws<JsonException>(() => new JsonSettingsStore(path).Load());
    }
}