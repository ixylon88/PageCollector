using System.ComponentModel;

namespace PageCollector.Core;

public sealed record CollectionEvent(Candidate Candidate, string Code, string Label, string Details, string FilePath = "");
/// <summary>Sequentially saves the first captured page whose title passes the configured rules.</summary>
public sealed class AutomaticCollector(Func<Candidate, string, CancellationToken, Task<CaptureResult>> capturePage, TimeSpan? attemptDelay = null)
{
    public async Task RunAsync(CollectionSession session, string outputDirectory, string temporaryDirectory, TitleFilter titles, ArchiveNamingOptions naming, Action<CollectionEvent> report, CancellationToken cancellationToken)
    {
        ArchiveNaming.Validate(naming);
        Directory.CreateDirectory(temporaryDirectory);

        while (!session.IsComplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = session.Current;
            var target = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".html");
            report(new(candidate, "CaptureStarted", "Aufnahme läuft", candidate.Url));

            try
            {
                await Task.Delay(attemptDelay ?? TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(false);
                CaptureResult capture;

                try
                {
                    capture = await capturePage(candidate, target, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or TimeoutException or Win32Exception)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    report(new(candidate, "CaptureFailed", "Aufnahme fehlgeschlagen", exception.Message));
                    Reject();
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var rejection = titles.RejectionReason(capture.Title);

                if (rejection is not null)
                {
                    report(new(candidate, "TitleRejected", "Titel ausgeschlossen", rejection));
                    Reject();
                    continue;
                }

                SavedArchive saved;

                try
                {
                    saved = await ArchiveStore.SaveAsync(outputDirectory, candidate, capture, cancellationToken, naming, automatic: true).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    report(new(candidate, "SaveFailed", "Speichern fehlgeschlagen", exception.Message));
                    // A destination failure must stop the run; it is not a failed source.
                    throw;
                }

                session.Accept();
                report(new(candidate, "Accepted", "Automatisch gespeichert", capture.Title, saved.HtmlPath));
            }
            finally
            {
                try
                {
                    File.Delete(target);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            void Reject()
            {
                if (session.RejectCandidate() is not null)
                {
                    report(new(candidate, "NoAcceptedCandidate", "Kein Treffer", "Alle Vorlagen wurden durchlaufen; keine Seite gespeichert."));
                }
            }
        }
    }
}