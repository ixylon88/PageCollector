using System.IO;
using PageCollector.Core;

namespace PageCollector.App.Services;

public sealed class SingleFileCaptureService : ICaptureService
{
    public Task<string> EnsureEngineAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return EngineInstaller.EnsureInstalledAsync(progress, cancellationToken);
    }

    public Task<CaptureResult> CaptureAsync(Candidate candidate, string targetPath, CaptureOptions options, CancellationToken cancellationToken)
    {
        return new CaptureRunner().CaptureAsync(candidate, targetPath, options, cancellationToken);
    }

    public string ResolveBrowser(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"')));

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Die angegebene Browser-Datei wurde nicht gefunden.", path);
            }

            if (Path.GetFileName(path).Contains("firefox", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Für die Aufnahme verwendet dieser Entwurf Edge oder Chrome. Die Vorschau kann auch in Firefox geöffnet werden.");
            }

            return path;
        }

        foreach (var directory in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        })
        {
            foreach (var relative in new[]
            {
                @"Microsoft\Edge\Application\msedge.exe",
                @"Google\Chrome\Application\chrome.exe",
                @"Chromium\Application\chrome.exe"
            })
            {
                var path = Path.Combine(directory, relative);

                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        throw new FileNotFoundException("Edge oder Chrome wurde nicht gefunden. Bitte den Browser-Pfad auswählen.");
    }
}