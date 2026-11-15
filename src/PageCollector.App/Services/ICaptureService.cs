using PageCollector.Core;

namespace PageCollector.App.Services;

public interface ICaptureService
{
    string ResolveBrowser(string configured);

    Task<string> EnsureEngineAsync(IProgress<double> progress, CancellationToken cancellationToken);

    Task<CaptureResult> CaptureAsync(Candidate candidate, string targetPath, CaptureOptions options, CancellationToken cancellationToken);
}