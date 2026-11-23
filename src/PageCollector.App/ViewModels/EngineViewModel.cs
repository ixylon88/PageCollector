using System.Collections.ObjectModel;
using System.IO;
using PageCollector.App.Commands;
using PageCollector.App.Services;
using PageCollector.Core;

namespace PageCollector.App.ViewModels;

public sealed record EngineChoice(string? Version, string Label);

public sealed class EngineViewModel : ObservableObject
{
    private readonly IEngineService service;
    private readonly IDesktopServices desktop;
    private CancellationTokenSource? cancellation;
    private bool checkedOnce;

    public EngineViewModel(IEngineService service, IDesktopServices desktop)
    {
        this.service = service;
        this.desktop = desktop;
        RefreshCommand = new(() => RunAsync(RefreshAsync), () => CanManage, HandleError);
        InstallCommand = new(() => RunAsync(InstallAsync), () => CanManage && SelectedChoice is not null, HandleError);
        CancelCommand = new(Cancel, () => IsBusy && cancellation?.IsCancellationRequested == false);
        OpenFolderCommand = new(OpenFolder);
        ReloadState();
    }

    public ObservableCollection<EngineChoice> Choices { get; } = [];

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand InstallCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public string ToolsDirectory => service.ToolsDirectory;

    public bool CanManage => !IsBusy && !IsCollectionActive;

    public bool IsBusy
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool IsCollectionActive
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RefreshCommands();
            }
        }
    }

    public EngineChoice? SelectedChoice
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ActiveVersion
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Noch nicht installiert";

    public string Status
    {
        get;
        private set => SetProperty(ref field, value);
    } = "„Versionen prüfen“ lädt die aktuelle Liste. Ein Versionswechsel erfolgt erst mit „Installieren / aktivieren“.";

    public double Progress
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public void LoadOnce()
    {
        if (!checkedOnce && CanManage)
        {
            checkedOnce = true;
            RefreshCommand.Execute(null);
        }
    }

    public void ReloadState()
    {
        try
        {
            var state = service.ReadState();
            ApplyState(state, state.PreferredVersion);
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
    }

    private void ApplyState(EngineState state, string? selectedVersion)
    {
        ActiveVersion = state.ActiveRelease is null ? "Noch nicht installiert" : "SingleFile CLI " + state.ActiveRelease.Version;
        Choices.Clear();
        Choices.Add(new(null, state.LatestVersion is null ? "Neueste stabile Version (Standard)" : $"Neueste stabile Version ({state.LatestVersion}, Standard)"));

        foreach (var release in state.Releases)
        {
            var installed = File.Exists(Path.Combine(ToolsDirectory, "single-file-" + release.Version + ".exe"));
            var label = release.Version + (installed ? " · installiert" : "");

            if (release.Version == state.ActiveRelease?.Version)
            {
                label += " · aktiv";
            }

            Choices.Add(new(release.Version, label));
        }

        SelectedChoice = Choices.FirstOrDefault(choice => choice.Version == selectedVersion) ?? Choices[0];
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        var selected = SelectedChoice?.Version;
        Status = "Verfügbare Versionen bei GitHub abfragen…";
        var state = await service.RefreshAsync(token);
        ApplyState(state, selected);
        Status = $"{state.Releases.Count} Versionen verfügbar. Neueste stabile Version: {state.LatestVersion}.";
    }

    private async Task InstallAsync(CancellationToken token)
    {
        var selected = SelectedChoice?.Version;
        Status = selected is null ? "Neueste stabile Version vorbereiten…" : $"Version {selected} vorbereiten…";
        var progress = new Progress<double>(fraction =>
        {
            if (IsBusy && !token.IsCancellationRequested)
            {
                Progress = fraction * 100;
            }
        });
        var installed = await service.InstallAsync(selected, progress, token);
        ReloadState();
        Status = $"SingleFile CLI {installed.Version} ist aktiv. Bisherige Installationen bleiben für einen Wechsel zurück erhalten.";
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        using var operation = new CancellationTokenSource();
        cancellation = operation;
        IsBusy = true;
        Progress = 0;

        try
        {
            await action(operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            Status = "Abgebrochen. Die bisher aktive Version bleibt ausgewählt.";
        }
        catch (OperationCanceledException)
        {
            Status = "Die Verbindung hat zu lange gedauert. Bitte erneut versuchen.";
        }
        finally
        {
            cancellation = null;
            IsBusy = false;
            Progress = 0;
        }
    }

    private void Cancel()
    {
        cancellation?.Cancel();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public async Task StopAsync()
    {
        Cancel();
        await Task.WhenAll(RefreshCommand.ExecutionTask, InstallCommand.ExecutionTask);
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(ToolsDirectory);
            desktop.OpenPath(ToolsDirectory);
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
    }

    private void HandleError(Exception exception) => Status = "Vorgang fehlgeschlagen: " + exception.Message;

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanManage));
        RefreshCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}