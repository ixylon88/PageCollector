namespace PageCollector.Core;

public interface IEngineService
{
    string ToolsDirectory { get; }

    EngineState ReadState();

    Task<EngineState> RefreshAsync(CancellationToken cancellationToken);

    Task<EngineInstallation> EnsureInstalledAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    Task<EngineInstallation> InstallAsync(string? preferredVersion, IProgress<double>? progress, CancellationToken cancellationToken);
}