using PageCollector.Core;

namespace PageCollector.App.Services;

public interface ICaptureService
{
    string ResolveBrowser(string configured);

    Task<CaptureResult> CaptureAsync(Candidate candidate, string targetPath, CaptureOptions options, CancellationToken cancellationToken);
}