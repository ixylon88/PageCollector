using System.IO;
using PageCollector.App.Services;
using PageCollector.App.ViewModels;
using PageCollector.Core;

namespace PageCollector.App.Tests;

public abstract class ViewModelWorkspace
{
    protected string Root { get; private set; } = "";

    protected FakeDesktop Desktop { get; private set; } = null!;

    protected FakeCapture Capture { get; private set; } = null!;

    protected FakeEngine Engine { get; private set; } = null!;

    protected MemorySettings Settings { get; private set; } = null!;

    protected MainViewModel ViewModel { get; private set; } = null!;

    [TestInitialize]
    public void CreateWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "pagecollector-ui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Desktop = new();
        Capture = new();
        Engine = new(Path.Combine(Root, "tools"));
        Settings = new();
        ViewModel = new(Desktop, Capture, Settings, Root, Engine)
        {
            OutputDirectory = Path.Combine(Root, "saved"),
            Values = "123456",
            Templates = "https://example.com/{id}",
            AutoPreview = false
        };
    }

    [TestCleanup]
    public async Task RemoveWorkspaceAsync()
    {
        await ViewModel.StopAsync();
        ViewModel.Dispose();
        Directory.Delete(Root, recursive: true);
    }

    protected sealed class FakeDesktop : IDesktopServices
    {
        public List<string> Errors { get; } = [];

        public List<string> OpenedPaths { get; } = [];

        public string? ChosenFolder { get; set; }

        public string? ChosenBrowser { get; set; }

        public string? ChosenValuesFile { get; set; }

        public string? ChooseOutputFolder() => ChosenFolder;

        public string? ChooseBrowser() => ChosenBrowser;

        public string? ChooseValuesFile() => ChosenValuesFile;

        public void OpenPath(string path) => OpenedPaths.Add(path);

        public void ShowError(string message) => Errors.Add(message);

        public void Invoke(Action action) => action();
    }

    protected sealed class FakeCapture : ICaptureService
    {
        public List<Candidate> Attempts { get; } = [];

        public Func<Candidate, string, CancellationToken, Task<CaptureResult>>? Handler { get; set; }

        public string ResolveBrowser(string configured) => "test-browser";

        public async Task<CaptureResult> CaptureAsync(Candidate candidate, string targetPath, CaptureOptions options, CancellationToken cancellationToken)
        {
            Attempts.Add(candidate);

            if (Handler is not null)
            {
                return await Handler(candidate, targetPath, cancellationToken);
            }

            await File.WriteAllTextAsync(targetPath, "<!doctype html><html><title>Original title</title><body>Article</body></html>", cancellationToken);

            return new(targetPath, "Original title", DateTimeOffset.UtcNow, options.EngineVersion);
        }
    }

    protected sealed class FakeEngine(string toolsDirectory) : IEngineService
    {
        public static readonly EngineRelease Older = new("2.9.0", new string('a', 64), 100);
        public static readonly EngineRelease Latest = new("2.10.0", new string('b', 64), 100);

        public string ToolsDirectory => toolsDirectory;

        public EngineState State { get; set; } = new(null, null, null, []);

        public int PreparationCount { get; private set; }

        public int RefreshCount { get; private set; }

        public Exception? PreparationError { get; set; }

        public Func<string?, CancellationToken, Task<EngineInstallation>>? InstallHandler { get; set; }

        public EngineState ReadState() => State;

        public Task<EngineState> RefreshAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            State = State with { LatestVersion = Latest.Version, Releases = [Latest, Older] };

            return Task.FromResult(State);
        }

        public Task<EngineInstallation> EnsureInstalledAsync(IProgress<double>? progress, CancellationToken cancellationToken)
        {
            PreparationCount++;

            return PreparationError is null
                ? InstallAsync(State.ActiveRelease?.Version, progress, cancellationToken)
                : Task.FromException<EngineInstallation>(PreparationError);
        }

        public async Task<EngineInstallation> InstallAsync(string? preferredVersion, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (InstallHandler is not null)
            {
                return await InstallHandler(preferredVersion, cancellationToken);
            }

            var release = preferredVersion == Older.Version ? Older : Latest;
            State = new(release, preferredVersion, Latest.Version, [Latest, Older]);

            return new("test-engine", release.Version);
        }
    }

    protected sealed class MemorySettings : ISettingsStore
    {
        public CollectorSettings? Value { get; set; }

        public CollectorSettings? Load() => Value;

        public void Save(CollectorSettings settings) => Value = settings;
    }
}