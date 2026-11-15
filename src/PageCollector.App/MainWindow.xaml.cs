using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PageCollector.Core;

namespace PageCollector.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ResultRow> rows = [];
    private CollectionSession? session;
    private SessionLog? log;
    private CaptureResult? capture;
    private CaptureOptions? captureOptions;
    private CancellationTokenSource? cancellation;
    private string outputDirectory = "";
    private string previewDirectory = "";
    private bool busy;
    private bool closeRequested;
    private bool autoPreview;
    private bool logWarningShown;

    public MainWindow()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = rows;
        LoadSettings();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var nextSession = new CollectionSession(UrlTemplates.Lines(ValuesBox.Text), UrlTemplates.Lines(TemplatesBox.Text));
            if (!double.TryParse(DelayBox.Text.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var seconds) || seconds < 0 || seconds > 30)
                throw new ArgumentException("Bitte eine zusätzliche Wartezeit zwischen 0 und 30 Sekunden eingeben.");
            var browser = ResolveBrowser(BrowserBox.Text);
            if (string.IsNullOrWhiteSpace(OutputBox.Text)) throw new ArgumentException("Bitte einen Ausgabeordner wählen.");
            outputDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(OutputBox.Text.Trim()));
            Directory.CreateDirectory(outputDirectory);
            // Open the session log first: this also checks that the destination is writable.
            log = new SessionLog(outputDirectory);
            session = nextSession;
            cancellation = new CancellationTokenSource();
            captureOptions = new("", browser, checked((int)(seconds * 1000)));
            previewDirectory = Path.Combine(EngineInstaller.DataDirectory, "previews", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(previewDirectory);
            autoPreview = AutoPreviewBox.IsChecked == true;
            logWarningShown = false;
            rows.Clear();
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
            ProgressText.Text = "Speicherhilfe prüfen / einmalig herunterladen…";
            DetailText.Text = "SingleFile CLI " + EngineInstaller.Version + " · offizieller GitHub-Download · SHA-256-Prüfung";
            WorkProgress.IsIndeterminate = false;
            var progress = new Progress<double>(fraction =>
            {
                WorkProgress.Value = fraction * 100;
                DetailText.Text = $"Speicherhilfe herunterladen: {fraction:P0} von ca. 82 MB";
            });
            try
            {
                var engine = await EngineInstaller.EnsureInstalledAsync(progress, cancellation!.Token);
                captureOptions = captureOptions! with { EnginePath = engine };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                FinishSession("Speicherhilfe konnte nicht vorbereitet werden. Mit „Starten“ erneut versuchen.");
                DetailText.Text = exception.Message;
                return;
            }
            await CaptureCurrentAsync();
        });
    }

    private async Task CaptureCurrentAsync()
    {
        capture = null;
        if (session is null) return;
        if (session.IsComplete)
        {
            FinishSession($"Fertig: {session.AcceptedCount} von {session.ValueCount} Suchwerten gespeichert.");
            return;
        }
        var current = session.Current;
        CurrentUrlBox.Text = current.Url;
        ProgressText.Text = $"{current.Value} · Wert {current.ValueNumber}/{session.ValueCount} · Vorlage {current.TemplateNumber}/{session.TemplateCount} wird aufgenommen…";
        DetailText.Text = "Die Seite lädt im Hintergrund. Danach kannst du die gespeicherte Kopie prüfen.";
        WorkProgress.IsIndeterminate = true;
        Record(current, "CaptureStarted", "Aufnahme läuft", current.Url);
        try
        {
            // A fresh filename keeps browser caching from showing a previous retry's HTML.
            var target = Path.Combine(previewDirectory, Guid.NewGuid().ToString("N") + ".html");
            await Task.Delay(700, cancellation!.Token);
            capture = await new CaptureRunner().CaptureAsync(current, target, captureOptions!, cancellation.Token);
            Record(current, "AwaitingApproval", "Bitte prüfen", capture.Title);
            ProgressText.Text = $"{current.Value} · Vorschau prüfen · Vorlage {current.TemplateNumber}/{session.TemplateCount}";
            DetailText.Text = capture.Title + " — Passender Inhalt? „Übernehmen & weiter“. Sonst „Nächste URL“.";
            if (autoPreview) OpenPath(capture.FilePath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            // Do not silently skip a potentially valid source after a transient error.
            Record(current, "CaptureFailed", "Aufnahme fehlgeschlagen", exception.Message);
            ProgressText.Text = $"{current.Value} · Aufnahme fehlgeschlagen";
            DetailText.Text = exception.Message;
        }
    }

    private async void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (session is null || capture is null || busy) return;
        await RunOperationAsync(async () =>
        {
            var current = session.Current;
            SavedArchive saved;
            try
            {
                ProgressText.Text = "Bestätigte Seite speichern…";
                saved = await ArchiveStore.SaveAsync(outputDirectory, current, capture, cancellation!.Token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                Record(current, "SaveFailed", "Speichern fehlgeschlagen", exception.Message);
                ProgressText.Text = "Speichern fehlgeschlagen — die Vorschau bleibt verfügbar.";
                DetailText.Text = exception.Message;
                return;
            }
            Record(current, "Accepted", "Gespeichert", saved.HtmlPath, saved.HtmlPath);
            session.Accept();
            await CaptureCurrentAsync();
        });
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (session is null || busy) return;
        var current = session.Current;
        Record(current, "Rejected", "Nächste URL", current.Url);
        var exhausted = session.RejectCandidate();
        if (exhausted is not null) Record(current, "NoAcceptedCandidate", "Kein Treffer bestätigt", "Alle Vorlagen wurden durchlaufen.");
        await RunOperationAsync(CaptureCurrentAsync);
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (session is null || busy) return;
        Record(session.Current, "RetryRequested", "Erneuter Versuch", session.Current.Url);
        await RunOperationAsync(CaptureCurrentAsync);
    }

    private async void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (session is null || busy) return;
        Record(session.Current, "ValueSkipped", "Wert übersprungen", "Manuell übersprungen.");
        session.SkipValue();
        await RunOperationAsync(CaptureCurrentAsync);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        if (busy) ProgressText.Text = "Wird abgebrochen…";
        else FinishSession("Abgebrochen. Bereits bestätigte Seiten bleiben gespeichert.");
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        busy = true;
        WorkProgress.Visibility = Visibility.Visible;
        UpdateButtons();
        try { await operation(); }
        catch (OperationCanceledException)
        {
            if (session is { IsComplete: false }) Record(session.Current, "Cancelled", "Abgebrochen", "Aufnahme oder Speichern abgebrochen.");
            FinishSession("Abgebrochen. Bereits bestätigte Seiten bleiben gespeichert.");
        }
        catch (Exception exception)
        {
            FinishSession("Vorgang beendet. Bereits bestätigte Seiten bleiben gespeichert.");
            ShowError(exception.Message);
        }
        finally
        {
            busy = false;
            WorkProgress.Visibility = Visibility.Collapsed;
            WorkProgress.Value = 0;
            UpdateButtons();
            if (closeRequested) Close();
        }
    }

    private void FinishSession(string message)
    {
        session = null;
        capture = null;
        captureOptions = null;
        try { log?.Dispose(); }
        catch (IOException) { }
        log = null;
        cancellation?.Dispose();
        cancellation = null;
        if (!string.IsNullOrEmpty(previewDirectory))
        {
            try { if (Directory.Exists(previewDirectory)) Directory.Delete(previewDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        previewDirectory = "";
        ProgressText.Text = message;
        CurrentUrlBox.Text = "";
        DetailText.Text = "Bestätigte Dateien lassen sich per Doppelklick in der Ergebnisliste öffnen.";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var running = session is not null;
        InputsPanel.IsEnabled = SettingsPanel.IsEnabled = DemoButton.IsEnabled = !running && !busy;
        StartButton.IsEnabled = !running && !busy;
        PreviewButton.IsEnabled = AcceptButton.IsEnabled = running && !busy && capture is not null;
        NextButton.IsEnabled = RetryButton.IsEnabled = SkipButton.IsEnabled = running && !busy;
        CancelButton.IsEnabled = running && cancellation?.IsCancellationRequested != true;
    }

    private void Record(Candidate candidate, string code, string label, string details, string file = "")
    {
        rows.Add(new(candidate.Value, label, details, file));
        ResultsGrid.ScrollIntoView(rows[^1]);
        try { log?.Append(candidate, code, file, details); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (logWarningShown) return;
            logWarningShown = true;
            ShowError("Das CSV-Protokoll konnte nicht weitergeschrieben werden. Bestätigte HTML-Dateien bleiben erhalten. " + exception.Message);
        }
    }

    private void Preview_Click(object sender, RoutedEventArgs e) { if (capture is not null) OpenPath(capture.FilePath); }
    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is ResultRow { FilePath.Length: > 0 } row) OpenPath(row.FilePath);
    }
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = session is not null ? outputDirectory : Path.GetFullPath(Environment.ExpandEnvironmentVariables(OutputBox.Text.Trim()));
            Directory.CreateDirectory(directory);
            OpenPath(directory);
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }
    private void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception) { ShowError("Öffnen fehlgeschlagen: " + exception.Message); }
    }
    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Ausgabeordner auswählen" };
        if (dialog.ShowDialog(this) == true) OutputBox.Text = dialog.FolderName;
    }
    private void ChooseBrowser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Edge oder Chrome auswählen", Filter = "Browser (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true) BrowserBox.Text = dialog.FileName;
    }
    private void LoadValues_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Suchwerte laden (UTF-8, ein Wert pro Zeile)", Filter = "Textdateien (*.txt)|*.txt|Alle Dateien|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try { ValuesBox.Text = File.ReadAllText(dialog.FileName); }
        catch (Exception exception) { ShowError(exception.Message); }
    }
    private void Demo_Click(object sender, RoutedEventArgs e)
    {
        ValuesBox.Text = "123456\n654321";
        TemplatesBox.Text = "https://example.com/?article={id}";
        ProgressText.Text = "Demo geladen: zwei Suchwerte auf example.com.";
        DetailText.Text = "Diese Vorlage dient nur dem Funktionstest. Für echte Treffer eigene URL-Vorlagen eintragen.";
    }

    private static string ResolveBrowser(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"')));
            if (!File.Exists(path)) throw new FileNotFoundException("Die angegebene Browser-Datei wurde nicht gefunden.", path);
            if (Path.GetFileName(path).Contains("firefox", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Für die Aufnahme verwendet dieser Entwurf Edge oder Chrome. Die Vorschau kann auch in Firefox geöffnet werden.");
            return path;
        }
        foreach (var directory in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
        foreach (var relative in new[] { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe", @"Chromium\Application\chrome.exe" })
        {
            var path = Path.Combine(directory, relative);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Edge oder Chrome wurde nicht gefunden. Bitte den Browser-Pfad auswählen.");
    }

    private static string SettingsPath => Path.Combine(EngineInstaller.DataDirectory, "settings.json");
    private void LoadSettings()
    {
        OutputBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PageCollector");
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath));
            if (settings is null) return;
            ValuesBox.Text = settings.Values;
            TemplatesBox.Text = settings.Templates;
            OutputBox.Text = settings.OutputDirectory;
            BrowserBox.Text = settings.BrowserPath;
            DelayBox.Text = settings.DelaySeconds;
            AutoPreviewBox.IsChecked = settings.AutoPreview;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { DetailText.Text = "Einstellungen konnten nicht gelesen werden; Standardwerte werden verwendet."; }
    }
    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(EngineInstaller.DataDirectory);
            var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new UserSettings(ValuesBox.Text, TemplatesBox.Text,
                OutputBox.Text, BrowserBox.Text, DelayBox.Text, AutoPreviewBox.IsChecked == true), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { ShowError("Einstellungen konnten nicht gespeichert werden: " + exception.Message); }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (busy)
        {
            closeRequested = true;
            e.Cancel = true;
            cancellation?.Cancel();
            return;
        }
        SaveSettings();
        FinishSession("Beendet.");
    }
    private void ShowError(string message) => MessageBox.Show(this, message, "PageCollector", MessageBoxButton.OK, MessageBoxImage.Information);

    private sealed record ResultRow(string Value, string Status, string Details, string FilePath = "");
    private sealed record UserSettings(string Values, string Templates, string OutputDirectory, string BrowserPath,
        string DelaySeconds, bool AutoPreview);
}
