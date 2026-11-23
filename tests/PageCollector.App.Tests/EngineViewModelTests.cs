using System.IO;
using PageCollector.App.ViewModels;

namespace PageCollector.App.Tests;

[TestClass]
public sealed class EngineViewModelTests : ViewModelWorkspace
{
    [TestMethod]
    public async Task LatestIsDefaultAndExplicitOlderChoiceSurvivesRefreshAndRestartAsync()
    {
        Assert.IsNotNull(ViewModel.Engines.SelectedChoice);
        Assert.IsNull(ViewModel.Engines.SelectedChoice.Version);
        await ViewModel.Engines.RefreshCommand.ExecuteAsync();
        ViewModel.Engines.SelectedChoice = ViewModel.Engines.Choices.Single(choice => choice.Version == FakeEngine.Older.Version);
        await ViewModel.Engines.RefreshCommand.ExecuteAsync();

        Assert.AreEqual(FakeEngine.Older.Version, ViewModel.Engines.SelectedChoice?.Version);
        await ViewModel.Engines.InstallCommand.ExecuteAsync();
        var restored = new EngineViewModel(Engine, Desktop);

        Assert.AreEqual(FakeEngine.Older.Version, restored.SelectedChoice?.Version);
        Assert.Contains(FakeEngine.Older.Version, restored.ActiveVersion);
        await restored.RefreshCommand.ExecuteAsync();

        Assert.AreEqual(FakeEngine.Older.Version, restored.SelectedChoice?.Version);
        Assert.Contains(FakeEngine.Latest.Version, restored.Choices[0].Label);
        Assert.IsNull(restored.Choices[0].Version);
    }

    [TestMethod]
    public async Task InstallationBlocksCollectionAndStopAwaitsCancellationAsync()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.InstallHandler = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Installation should have been cancelled.");
        };
        var install = ViewModel.Engines.InstallCommand.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(ViewModel.Engines.IsBusy);
        Assert.IsFalse(ViewModel.StartCommand.CanExecute(null));
        Assert.IsFalse(ViewModel.Engines.RefreshCommand.CanExecute(null));
        Assert.IsFalse(ViewModel.Engines.InstallCommand.CanExecute(null));
        Assert.IsTrue(ViewModel.Engines.CancelCommand.CanExecute(null));
        await ViewModel.StartCommand.ExecuteAsync();
        await ViewModel.StopAsync();
        await install;

        Assert.IsEmpty(Capture.Attempts);
        Assert.IsFalse(ViewModel.Engines.IsBusy);
        Assert.IsTrue(ViewModel.CanEdit);
        Assert.Contains("Abgebrochen", ViewModel.Engines.Status);
        Assert.IsEmpty(Desktop.Errors);
    }

    [TestMethod]
    public async Task FailedActivationKeepsActiveVersionVisibleAndAllowsRetryAsync()
    {
        await ViewModel.Engines.RefreshCommand.ExecuteAsync();
        ViewModel.Engines.SelectedChoice = ViewModel.Engines.Choices.Single(choice => choice.Version == FakeEngine.Older.Version);
        await ViewModel.Engines.InstallCommand.ExecuteAsync();
        ViewModel.Engines.SelectedChoice = ViewModel.Engines.Choices[0];
        Engine.InstallHandler = (_, _) => throw new IOException("Checksum mismatch");
        await ViewModel.Engines.InstallCommand.ExecuteAsync();

        Assert.Contains(FakeEngine.Older.Version, ViewModel.Engines.ActiveVersion);
        Assert.Contains("Checksum mismatch", ViewModel.Engines.Status);
        Assert.IsTrue(ViewModel.Engines.InstallCommand.CanExecute(null));
        Assert.IsTrue(ViewModel.StartCommand.CanExecute(null));
        Assert.AreEqual(FakeEngine.Older.Version, Engine.State.PreferredVersion);
    }

    [TestMethod]
    public void OpeningToolsFolderUsesManagedDirectoryAndInitialCheckRunsOnlyOnce()
    {
        ViewModel.Engines.OpenFolderCommand.Execute(null);
        ViewModel.Engines.LoadOnce();
        ViewModel.Engines.LoadOnce();

        CollectionAssert.AreEqual(new[] { Engine.ToolsDirectory }, Desktop.OpenedPaths);
        Assert.IsTrue(Directory.Exists(Engine.ToolsDirectory));
        Assert.AreEqual(1, Engine.RefreshCount);
    }

    [TestMethod]
    public async Task StopAlsoCancelsFirstUseEnginePreparationAsync()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.InstallHandler = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Preparation should have been cancelled.");
        };
        var run = ViewModel.StartCommand.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ViewModel.StopAsync();
        await run;

        Assert.IsEmpty(Capture.Attempts);
        Assert.IsTrue(ViewModel.CanEdit);
        Assert.IsEmpty(Desktop.Errors);
    }
}