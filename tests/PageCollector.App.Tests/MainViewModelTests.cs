using System.IO;
using System.Text.Json;
using PageCollector.App.ViewModels;

namespace PageCollector.App.Tests;

[TestClass]
public sealed class MainViewModelTests : ViewModelWorkspace
{
    [TestMethod]
    public void NamingAndModeChangesNotifyDependentProperties()
    {
        var changed = new List<string?>();
        ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        ViewModel.Prefix = "KB";
        ViewModel.Suffix = "_DEU";
        ViewModel.Automatic = false;

        Assert.AreEqual("Beispiel: KB123456_DEU - Beispiel Seitentitel.html", ViewModel.FilenameExample);
        CollectionAssert.Contains(changed, nameof(MainViewModel.FilenameExample));
        CollectionAssert.Contains(changed, nameof(MainViewModel.IsManual));
        Assert.IsTrue(ViewModel.CanAutoPreview);
    }

    [TestMethod]
    public async Task AutomaticRunSavesWithoutOpeningPreviewAsync()
    {
        ViewModel.AutoPreview = true;
        await ViewModel.StartCommand.ExecuteAsync();

        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(ViewModel.OutputDirectory, "*.source.json").Single()));

        Assert.AreEqual("SingleFile CLI " + FakeEngine.Latest.Version, metadata.RootElement.GetProperty("captureEngine").GetString());
        Assert.AreEqual(1, Capture.Attempts.Count);
        Assert.HasCount(1, Directory.GetFiles(ViewModel.OutputDirectory, "*.html"));
        Assert.IsEmpty(Desktop.OpenedPaths);
        Assert.IsEmpty(Desktop.Errors);
        Assert.IsTrue(ViewModel.CanEdit);
        Assert.IsFalse(ViewModel.IsBusy);
        Assert.Contains("1 von 1", ViewModel.Status);
        Assert.IsFalse(ViewModel.AcceptCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ManualRunWaitsForAcceptanceBeforeSavingAsync()
    {
        ViewModel.Automatic = false;
        await ViewModel.StartCommand.ExecuteAsync();

        Assert.IsFalse(ViewModel.CanEdit);
        Assert.IsFalse(ViewModel.Engines.InstallCommand.CanExecute(null));
        Assert.IsFalse(ViewModel.Engines.RefreshCommand.CanExecute(null));
        Assert.IsTrue(ViewModel.AcceptCommand.CanExecute(null));
        Assert.IsEmpty(Directory.GetFiles(ViewModel.OutputDirectory, "*.html"));
        await ViewModel.AcceptCommand.ExecuteAsync();

        Assert.HasCount(1, Directory.GetFiles(ViewModel.OutputDirectory, "*.html"));
        Assert.IsTrue(ViewModel.CanEdit);
        Assert.IsFalse(ViewModel.CancelCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ManualRejectionTriesNextSourceAndNeverSavesRejectedCaptureAsync()
    {
        ViewModel.Automatic = false;
        ViewModel.Templates = "https://example.com/a/{id}\nhttps://example.com/b/{id}";
        await ViewModel.StartCommand.ExecuteAsync();
        await ViewModel.NextCommand.ExecuteAsync();

        Assert.AreEqual(2, Capture.Attempts.Count);
        Assert.AreEqual(2, Capture.Attempts[1].TemplateNumber);
        Assert.IsEmpty(Directory.GetFiles(ViewModel.OutputDirectory, "*.html"));
        Assert.IsTrue(ViewModel.AcceptCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task InvalidInputDoesNotPrepareEngineOrCreateSessionAsync()
    {
        ViewModel.Values = "123";
        ViewModel.Templates = "https://example.com/{id:4-6}";
        await ViewModel.StartCommand.ExecuteAsync();

        Assert.AreEqual(0, Engine.PreparationCount);
        Assert.HasCount(1, Desktop.Errors);
        Assert.IsTrue(ViewModel.CanEdit);
        Assert.IsFalse(Directory.Exists(ViewModel.OutputDirectory));
    }

    [TestMethod]
    public async Task PreparationFailureRestoresStartCommandAsync()
    {
        Engine.PreparationError = new IOException("Test preparation failure");
        await ViewModel.StartCommand.ExecuteAsync();

        Assert.IsTrue(ViewModel.StartCommand.CanExecute(null));
        Assert.IsFalse(ViewModel.CancelCommand.CanExecute(null));
        Assert.IsEmpty(Capture.Attempts);
        Assert.Contains("Test preparation failure", ViewModel.Details);
    }

    [TestMethod]
    public async Task StopCancelsCaptureAndPreventsConcurrentStartAsync()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Capture.Handler = async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Capture should have been cancelled.");
        };
        var run = ViewModel.StartCommand.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsFalse(ViewModel.StartCommand.CanExecute(null));
        Assert.IsFalse(ViewModel.CanEdit);
        await ViewModel.StartCommand.ExecuteAsync();
        await ViewModel.StopAsync();
        await run;

        Assert.AreEqual(1, Capture.Attempts.Count);
        Assert.IsFalse(ViewModel.IsBusy);
        Assert.IsTrue(ViewModel.CanEdit);
        Assert.IsEmpty(Directory.GetFiles(ViewModel.OutputDirectory, "*.html"));
        Assert.IsEmpty(Desktop.Errors);
    }

    [TestMethod]
    public void SettingsRoundTripAndInvalidAffixesAreClampedOnLoad()
    {
        ViewModel.Prefix = "KB";
        ViewModel.Suffix = "_DEU";
        ViewModel.ExcludedTitles = "";
        ViewModel.Automatic = false;
        ViewModel.SaveSettings();
        using var restored = new MainViewModel(Desktop, Capture, Settings, Root, Engine);

        Assert.AreEqual("KB", restored.Prefix);
        Assert.AreEqual("_DEU", restored.Suffix);
        Assert.AreEqual("", restored.ExcludedTitles);
        Assert.IsFalse(restored.Automatic);
        Settings.Value = Settings.Value! with
        {
            Prefix = new string('x', 60)
        };
        using var clamped = new MainViewModel(Desktop, Capture, Settings, Root, Engine);

        Assert.AreEqual(40, clamped.Prefix.Length);
    }

    [TestMethod]
    public async Task FailedManualSaveKeepsCaptureAvailableForRetryAsync()
    {
        string? temporaryFile = null;
        Capture.Handler = async (_, target, token) =>
        {
            temporaryFile = target;
            await File.WriteAllTextAsync(target, "<html><title>Article</title></html>", token);

            return new(target, "Article", DateTimeOffset.UtcNow);
        };
        ViewModel.Automatic = false;
        await ViewModel.StartCommand.ExecuteAsync();
        File.Delete(temporaryFile!);
        await ViewModel.AcceptCommand.ExecuteAsync();

        Assert.IsTrue(ViewModel.AcceptCommand.CanExecute(null));
        Assert.IsFalse(ViewModel.CanEdit);
        Assert.Contains("Speichern fehlgeschlagen", ViewModel.Status);
        await File.WriteAllTextAsync(temporaryFile!, "<html><title>Article</title></html>");
        await ViewModel.AcceptCommand.ExecuteAsync();

        Assert.HasCount(1, Directory.GetFiles(ViewModel.OutputDirectory, "*.html"));
    }
}