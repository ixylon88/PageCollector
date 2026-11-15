using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using PageCollector.App.Commands;
using PageCollector.App.Services;
using PageCollector.Core;

namespace PageCollector.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDesktopServices desktop;
    private readonly ICaptureService captureService;
    private readonly ISettingsStore settingsStore;
    private readonly string workDirectory;
    private CollectionSession? session;
    private SessionLog? log;
    private CaptureResult? capture;
    private CaptureOptions? captureOptions;
    private CancellationTokenSource? cancellation;
    private string outputDirectory = "";
    private string previewDirectory = "";
    private bool autoPreview;
    private bool logWarningShown;
    private bool automatic;
    private TitleFilter titleFilter = new(TitleFilter.DefaultRules);
    private ArchiveNamingOptions naming = new();

    public MainViewModel(IDesktopServices desktop, ICaptureService captureService, ISettingsStore settingsStore, string workDirectory)
    {
        this.desktop = desktop;
        this.captureService = captureService;
        this.settingsStore = settingsStore;
        this.workDirectory = workDirectory;
        StartCommand = new(StartAsync, () => CanEdit, HandleCommandError);
        AcceptCommand = new(AcceptAsync, () => CanDecide && capture is not null, HandleCommandError);
        NextCommand = new(NextAsync, () => CanDecide, HandleCommandError);
        RetryCommand = new(RetryAsync, () => CanDecide, HandleCommandError);
        SkipCommand = new(SkipAsync, () => CanDecide, HandleCommandError);
        CancelCommand = new(Cancel, () => session is not null && cancellation?.IsCancellationRequested != true);
        PreviewCommand = new(Preview, () => CanDecide && capture is not null);
        DemoCommand = new(LoadDemo, () => CanEdit);
        ChooseOutputCommand = new(ChooseOutput, () => CanEdit);
        ChooseBrowserCommand = new(ChooseBrowser, () => CanEdit);
        LoadValuesCommand = new(LoadValues, () => CanEdit);
        OpenOutputCommand = new(OpenOutput);
        LoadSettings();
    }

    public ObservableCollection<ResultRow> Results { get; } = [];

    public AsyncCommand StartCommand { get; }

    public AsyncCommand AcceptCommand { get; }

    public AsyncCommand NextCommand { get; }

    public AsyncCommand RetryCommand { get; }

    public AsyncCommand SkipCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public RelayCommand DemoCommand { get; }

    public RelayCommand ChooseOutputCommand { get; }

    public RelayCommand ChooseBrowserCommand { get; }

    public RelayCommand LoadValuesCommand { get; }

    public RelayCommand OpenOutputCommand { get; }

    public bool IsBusy { get; private set; }

    public bool CanEdit => session is null && !IsBusy;

    public bool IsManual => !Automatic;

    public bool CanAutoPreview => CanEdit && IsManual;

    private bool CanDecide => session is not null && !IsBusy && !Automatic;

    public string FilenameExample => "Beispiel: " + ArchiveNaming.BuildStem("123456", "Beispiel Seitentitel", new(Prefix, Suffix, IncludeTitle)) + ".html";

    public string Values
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string Templates
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string OutputDirectory
    {
        get;
        set => SetProperty(ref field, value);
    } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PageCollector");

    public string BrowserPath
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string DelaySeconds
    {
        get;
        set => SetProperty(ref field, value);
    } = "1,5";

    public bool AutoPreview
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool Automatic
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsManual));
                OnPropertyChanged(nameof(CanAutoPreview));
                RefreshState();
            }
        }
    } = true;

    public string ExcludedTitles
    {
        get;
        set => SetProperty(ref field, value);
    } = TitleFilter.DefaultRules;

    public string Prefix
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(FilenameExample));
            }
        }
    } = "";

    public string Suffix
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(FilenameExample));
            }
        }
    } = "";

    public bool IncludeTitle
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(FilenameExample));
            }
        }
    } = true;

    public string Status
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit. Zum Ausprobieren oben „Demo laden“ wählen.";

    public string Details
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Beim ersten Start wird SingleFile CLI einmalig von GitHub geladen (ca. 82 MB).";

    public string CurrentUrl
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    public bool IsIndeterminate
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public double Progress
    {
        get;
        private set => SetProperty(ref field, value);
    }

    private async Task StartAsync()
    {
        try
        {
            var nextSession = new CollectionSession(UrlTemplates.Lines(Values), UrlTemplates.Lines(Templates));
            naming = new(Prefix, Suffix, IncludeTitle);
            ArchiveNaming.Validate(naming);
            titleFilter = new(ExcludedTitles);
            automatic = Automatic;

            if (!double.TryParse(DelaySeconds.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) || seconds < 0 || seconds > 30)
            {
                throw new ArgumentException("Bitte eine zusätzliche Wartezeit zwischen 0 und 30 Sekunden eingeben.");
            }

            var browser = captureService.ResolveBrowser(BrowserPath);

            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                throw new ArgumentException("Bitte einen Ausgabeordner wählen.");
            }

            outputDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(OutputDirectory.Trim()));
            Directory.CreateDirectory(outputDirectory);
            // Open the session log first: this also checks that the destination is writable.
            log = new SessionLog(outputDirectory);
            session = nextSession;
            cancellation = new CancellationTokenSource();
            captureOptions = new("", browser, checked((int)(seconds * 1000)));
            previewDirectory = Path.Combine(workDirectory, "previews", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(previewDirectory);
            autoPreview = !automatic && AutoPreview;
            logWarningShown = false;
            Results.Clear();
            SaveSettings();
        }
        catch (Exception exception)
        {
            FinishSession("Start nicht möglich.");
            ShowError(exception.Message);

            return;
        }

        await RunOperationAsync(async () =>
        {
            Status = "Speicherhilfe prüfen / einmalig herunterladen…";
            Details = "SingleFile CLI " + EngineInstaller.Version + " · offizieller GitHub-Download · SHA-256-Prüfung";
            IsIndeterminate = false;
            var progress = new Progress<double>(fraction =>
            {
                Progress = fraction * 100;
                Details = $"Speicherhilfe herunterladen: {fraction:P0} von ca. 82 MB";
            });

            try
            {
                var engine = await captureService.EnsureEngineAsync(progress, cancellation.Token);
                captureOptions = captureOptions with
                {
                    EnginePath = engine
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                FinishSession("Speicherhilfe konnte nicht vorbereitet werden. Mit „Starten“ erneut versuchen.");
                Details = exception.Message;

                return;
            }

            if (automatic)
            {
                await RunAutomaticAsync();
            }
            else
            {
                await CaptureCurrentAsync();
            }
        });
    }

    private async Task RunAutomaticAsync()
    {
        var activeSession = session!;
        var options = captureOptions!;
        IsIndeterminate = true;
        var runner = new AutomaticCollector((candidate, target, token) => captureService.CaptureAsync(candidate, target, options, token));
        await runner.RunAsync(activeSession, outputDirectory, previewDirectory, titleFilter, naming, progress => desktop.Invoke(() =>
        {
            Record(progress.Candidate, progress.Code, progress.Label, progress.Details, progress.FilePath);
            CurrentUrl = progress.Candidate.Url;
            Status = $"{progress.Candidate.Value} · Wert {progress.Candidate.ValueNumber}/{activeSession.ValueCount} · Vorlage {progress.Candidate.TemplateNumber}/{activeSession.TemplateCount} · {progress.Label}";
            Details = progress.Details;
        }), cancellation!.Token);
        FinishSession($"Fertig: {activeSession.AcceptedCount} von {activeSession.ValueCount} Suchwerten automatisch gespeichert; {activeSession.ValueCount - activeSession.AcceptedCount} ohne Treffer.");
    }

    private async Task CaptureCurrentAsync()
    {
        capture = null;

        if (session is null)
        {
            return;
        }

        if (session.IsComplete)
        {
            FinishSession($"Fertig: {session.AcceptedCount} von {session.ValueCount} Suchwerten gespeichert.");

            return;
        }

        var current = session.Current;
        CurrentUrl = current.Url;
        Status = $"{current.Value} · Wert {current.ValueNumber}/{session.ValueCount} · Vorlage {current.TemplateNumber}/{session.TemplateCount} wird aufgenommen…";
        Details = "Die Seite lädt im Hintergrund. Danach kannst du die gespeicherte Kopie prüfen.";
        IsIndeterminate = true;
        Record(current, "CaptureStarted", "Aufnahme läuft", current.Url);

        try
        {
            // A fresh filename keeps browser caching from showing a previous retry's HTML.
            var target = Path.Combine(previewDirectory, Guid.NewGuid().ToString("N") + ".html");
            await Task.Delay(700, cancellation!.Token);
            capture = await captureService.CaptureAsync(current, target, captureOptions!, cancellation.Token);
            var title = string.IsNullOrWhiteSpace(capture.Title) ? "(ohne Titel)" : capture.Title;
            var rejection = titleFilter.RejectionReason(capture.Title);
            Record(current, "AwaitingApproval", "Bitte prüfen", rejection ?? title);
            Status = $"{current.Value} · Vorschau prüfen · Vorlage {current.TemplateNumber}/{session.TemplateCount}";
            Details = (rejection ?? title) + " — Manuell: „Übernehmen & weiter“ oder „Nächste URL“.";

            if (autoPreview)
            {
                OpenPath(capture.FilePath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not silently skip a potentially valid source after a transient error.
            Record(current, "CaptureFailed", "Aufnahme fehlgeschlagen", exception.Message);
            Status = $"{current.Value} · Aufnahme fehlgeschlagen";
            Details = exception.Message;
        }
    }

    private async Task AcceptAsync()
    {
        if (session is null || capture is null || IsBusy)
        {
            return;
        }

        await RunOperationAsync(async () =>
        {
            var current = session.Current;
            SavedArchive saved;

            try
            {
                Status = "Bestätigte Seite speichern…";
                saved = await ArchiveStore.SaveAsync(outputDirectory, current, capture, cancellation!.Token, naming);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Record(current, "SaveFailed", "Speichern fehlgeschlagen", exception.Message);
                Status = "Speichern fehlgeschlagen — die Vorschau bleibt verfügbar.";
                Details = exception.Message;

                return;
            }

            Record(current, "Accepted", "Gespeichert", capture.Title, saved.HtmlPath);
            session.Accept();
            await CaptureCurrentAsync();
        });
    }

    private async Task NextAsync()
    {
        if (session is null || IsBusy)
        {
            return;
        }

        var current = session.Current;
        Record(current, "Rejected", "Nächste URL", current.Url);
        var exhausted = session.RejectCandidate();

        if (exhausted is not null)
        {
            Record(current, "NoAcceptedCandidate", "Kein Treffer bestätigt", "Alle Vorlagen wurden durchlaufen.");
        }

        await RunOperationAsync(CaptureCurrentAsync);
    }

    private async Task RetryAsync()
    {
        if (session is null || IsBusy)
        {
            return;
        }

        Record(session.Current, "RetryRequested", "Erneuter Versuch", session.Current.Url);
        await RunOperationAsync(CaptureCurrentAsync);
    }

    private async Task SkipAsync()
    {
        if (session is null || IsBusy)
        {
            return;
        }

        Record(session.Current, "ValueSkipped", "Wert übersprungen", "Manuell übersprungen.");
        session.SkipValue();
        await RunOperationAsync(CaptureCurrentAsync);
    }

    private void Cancel()
    {
        cancellation?.Cancel();
        RefreshState();

        if (IsBusy)
        {
            Status = "Wird abgebrochen…";
        }
        else
        {
            FinishSession("Abgebrochen. Bereits gespeicherte Seiten bleiben erhalten.");
        }
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        IsBusy = true;
        RefreshState();

        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            if (session is { IsComplete: false })
            {
                Record(session.Current, "Cancelled", "Abgebrochen", "Aufnahme oder Speichern abgebrochen.");
            }

            FinishSession("Abgebrochen. Bereits gespeicherte Seiten bleiben erhalten.");
        }
        catch (Exception exception)
        {
            FinishSession("Vorgang beendet. Bereits gespeicherte Seiten bleiben erhalten.");
            ShowError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            RefreshState();
        }
    }

    private void FinishSession(string message)
    {
        session = null;
        capture = null;
        captureOptions = null;

        try
        {
            log?.Dispose();
        }
        catch (IOException)
        {
        }

        log = null;
        cancellation?.Dispose();
        cancellation = null;

        if (!string.IsNullOrEmpty(previewDirectory))
        {
            try
            {
                if (Directory.Exists(previewDirectory))
                {
                    Directory.Delete(previewDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        previewDirectory = "";
        Status = message;
        CurrentUrl = "";
        Details = "Gespeicherte Dateien lassen sich per Doppelklick in der Ergebnisliste öffnen.";
        RefreshState();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanAutoPreview));

        foreach (var command in new[]
        {
            StartCommand,
            AcceptCommand,
            NextCommand,
            RetryCommand,
            SkipCommand
        })
        {
            command.NotifyCanExecuteChanged();
        }

        foreach (var command in new[]
        {
            CancelCommand,
            PreviewCommand,
            DemoCommand,
            ChooseOutputCommand,
            ChooseBrowserCommand,
            LoadValuesCommand
        })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private void Record(Candidate candidate, string code, string label, string details, string file = "")
    {
        Results.Add(new(candidate.Value, label, file.Length > 0 ? file : details, file));

        try
        {
            log?.Append(candidate, code, file, details);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (logWarningShown)
            {
                return;
            }

            logWarningShown = true;
            ShowError("Das CSV-Protokoll konnte nicht weitergeschrieben werden. Bestätigte HTML-Dateien bleiben erhalten. " + exception.Message);
        }
    }

    private void Preview()
    {
        if (capture is not null)
        {
            OpenPath(capture.FilePath);
        }
    }

    public void OpenResult(ResultRow row)
    {
        if (row.FilePath.Length > 0)
        {
            OpenPath(row.FilePath);
        }
    }

    private void OpenOutput()
    {
        try
        {
            var directory = session is not null ? outputDirectory : Path.GetFullPath(Environment.ExpandEnvironmentVariables(OutputDirectory.Trim()));
            Directory.CreateDirectory(directory);
            OpenPath(directory);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            desktop.OpenPath(path);
        }
        catch (Exception exception)
        {
            ShowError("Öffnen fehlgeschlagen: " + exception.Message);
        }
    }

    private void ChooseOutput()
    {
        var path = desktop.ChooseOutputFolder();

        if (path is not null)
        {
            OutputDirectory = path;
        }
    }

    private void ChooseBrowser()
    {
        var path = desktop.ChooseBrowser();

        if (path is not null)
        {
            BrowserPath = path;
        }
    }

    private void LoadValues()
    {
        var path = desktop.ChooseValuesFile();

        if (path is null)
        {
            return;
        }

        try
        {
            Values = File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void LoadDemo()
    {
        Values = "123456\n654321";
        Templates = "https://example.com/?article={id}&group={id:1-3}";
        Status = "Demo geladen: zwei Suchwerte auf example.com.";
        Details = "Automatik ist standardmäßig aktiv. Diese Vorlage dient nur dem Funktionstest; für echte Treffer eigene URL-Vorlagen eintragen.";
    }

    private void LoadSettings()
    {
        try
        {
            var settings = settingsStore.Load();

            if (settings is null)
            {
                return;
            }

            Values = settings.Values ?? "";
            Templates = settings.Templates ?? "";

            if (!string.IsNullOrWhiteSpace(settings.OutputDirectory))
            {
                OutputDirectory = settings.OutputDirectory;
            }

            BrowserPath = settings.BrowserPath ?? "";
            DelaySeconds = settings.DelaySeconds ?? "1,5";
            AutoPreview = settings.AutoPreview;
            Automatic = settings.Automatic;
            ExcludedTitles = settings.ExcludedTitles ?? TitleFilter.DefaultRules;
            Prefix = ArchiveNaming.Truncate(settings.Prefix ?? "", 40);
            Suffix = ArchiveNaming.Truncate(settings.Suffix ?? "", 40);
            IncludeTitle = settings.IncludeTitle;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Details = "Einstellungen konnten nicht gelesen werden; Standardwerte werden verwendet.";
        }
    }

    public void SaveSettings()
    {
        try
        {
            settingsStore.Save(new(Values, Templates, OutputDirectory, BrowserPath, DelaySeconds, AutoPreview, Automatic, ExcludedTitles, Prefix, Suffix, IncludeTitle));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError("Einstellungen konnten nicht gespeichert werden: " + exception.Message);
        }
    }

    public async Task StopAsync()
    {
        Cancel();
        await Task.WhenAll(StartCommand.ExecutionTask, AcceptCommand.ExecutionTask, NextCommand.ExecutionTask, RetryCommand.ExecutionTask, SkipCommand.ExecutionTask);
    }

    public void Dispose()
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("StopAsync must finish before disposing the view model.");
        }

        FinishSession("Beendet.");
    }

    private void HandleCommandError(Exception exception)
    {
        FinishSession("Vorgang beendet. Bereits gespeicherte Seiten bleiben erhalten.");
        ShowError(exception.Message);
    }

    private void ShowError(string message) => desktop.ShowError(message);
}