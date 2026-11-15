# PageCollector 0.1

A small Windows application for trying ordered URL templates and keeping the first **manually approved** page for each search value. The application is written in C# / WPF. SingleFile CLI performs the webpage capture.

## Quick start

1. Extract the entire ZIP to a writable folder.
2. Run `Start.cmd` (or `app/PageCollector.exe`).
3. Click **Demo laden**, then **Starten**. The demo uses `example.com` and two sample values; it is only a functional test.
4. On the first run, the application downloads SingleFile CLI 2.26.0 from its official GitHub release (about 82 MB) and verifies its published SHA-256 digest.
5. The saved candidate opens in the default browser. Return to PageCollector and choose **Übernehmen & weiter** to retain it or **Nächste URL** to try the next template.

The packaged application targets **Windows x64** and includes its .NET runtime. No separate .NET installation, administrative rights or PowerShell script is required to run the package. Microsoft Edge or Google Chrome must be installed for capture. The default browser used for the preview may also be Firefox.

See [START-HIER.txt](START-HIER.txt) for the short German walkthrough and [TEST-REPORT.md](TEST-REPORT.md) for the exact verification limits of this prototype.

## Inputs

Enter one search value per line. Values can also be loaded from a UTF-8 TXT file or pasted as a column from a spreadsheet. The prototype does not parse multi-column CSV input. Blank lines are ignored; duplicate values are retained.

Enter one URL template per line, in priority order. `{id}` and `{Suchwert}` are equivalent and case-insensitive. Examples below are schematic, not real archive addresses:

```text
https://archive.example/kb/Q{id}.html
https://archive.example/articles/{id}/
https://archive.example/search?article={id}
```

With the value `123456`, the first template becomes `https://archive.example/kb/Q123456.html`. The substituted value is URL-encoded; the surrounding path and query remain intact. Templates must use HTTP or HTTPS. The placeholder belongs in the path or query, not the host name. Existing Wayback replay URLs can be used as templates; this prototype does not look up timestamps through the Wayback API.

Real source URLs were not provided with the initial request. Replace the demo template with the actual source templates before collecting articles.

## Review and decisions

| Button | Effect |
| --- | --- |
| Starten | Start a new run with a snapshot of the current inputs. |
| Vorschau öffnen | Open the currently captured HTML file again. |
| Übernehmen & weiter | Save the approved HTML and provenance, then start the first template for the next value. |
| Nächste URL | Reject the current candidate and try the next template. After the last template, continue with the next value. |
| Erneut versuchen | Capture the same URL again, for example after a timeout. |
| Wert überspringen | Skip all remaining templates for the current search value. |
| Abbrechen | Stop the current capture/run. Already approved files remain. |

A captured page is **not** automatically considered a valid article. A 404 page, a soft error page with HTTP 200, an archive calendar, a login screen or a robot check can also be captured. Check the page content before accepting it. A process failure or timeout leaves the current source selected; the application does not silently move to a lower-priority source.

The preview is the already saved HTML, not a second live visit. Acceptance copies that same file without recapturing the page. Preview tabs are temporary; reopen accepted files by double-clicking their `Gespeichert` row or from the output folder. The prototype opens a separate browser tab for each preview and does not close those tabs automatically.

## Output

For an approved value, the output directory contains:

```text
123456.html
123456.source.json
session-<timestamp>-<unique-id>.csv
```

The HTML is the SingleFile archive. The JSON sidecar records the search value, template, requested URL, title, capture/approval timestamps and SHA-256 of the saved HTML. `requestedUrl` is deliberately not labelled as the final redirected URL. SingleFile also normally embeds its own source information in the HTML.

The session CSV uses semicolons and UTF-8 with BOM for convenient opening in Excel. It records attempts, approvals, rejections, retries, failures and skipped values. Existing files are never overwritten: repeated or colliding values produce names such as `123456 (2).html`. Unsafe Windows filename characters are replaced.

Inputs and settings are retained in `%LOCALAPPDATA%\PageCollector\settings.json`. The verified helper is stored in `%LOCALAPPDATA%\PageCollector\tools`. Temporary previews are kept in an application-owned session folder and removed at normal session completion/cancellation. A crash may leave that session's previews behind.

## Capture behaviour and current limits

- A separate temporary Edge/Chrome profile is used. Existing browser logins and Firefox extensions are not reused.
- The capture uses a desktop viewport of 1440 × 1000 pixels, loads deferred content, embeds available resources and removes page scripts from the saved copy. Audio and video are excluded.
- The additional wait is configurable from 0 to 30 seconds (default 1.5). Loading and capture have their own 60-second limits; PageCollector ends the whole helper process after 150 seconds.
- Formatting should remain close to the rendered page, but missing archive resources, cookie banners, interactive widgets and robot checks still require judgment. This is a readable snapshot, not a complete functioning website.
- The first version is entirely manual at the approval step. It has no per-site validity rules, automatic retries, pause/resume checkpoint or automatic skipping of existing output.
- Changing values, templates, browser path or timing requires stopping the run. After restarting, use the CSV to select remaining values; rerunning an accepted value creates an additional file rather than overwriting it.
- Windows ARM64 is not a separately built/tested target in this package.

## Build from source

Install a .NET 10 SDK on Windows. Then run:

```bat
Build.cmd
```

This publishes a self-contained Windows x64 build to `dist\win-x64`. `Run-Source.cmd` builds and runs directly from source. `Verify.cmd` runs the platform-independent checks. The scripts use CMD and the .NET CLI, not PowerShell.

Equivalent commands:

```text
dotnet build src/PageCollector.App/PageCollector.App.csproj -c Release
dotnet run --project tests/PageCollector.Checks -c Release
dotnet publish src/PageCollector.App/PageCollector.App.csproj -c Release -r win-x64 --self-contained true -o dist/win-x64
```

The code has no NuGet dependencies beyond the framework. UI event handling stays in the WPF application; queue progression, process management, storage and helper download are in `PageCollector.Core`. No dependency-injection container or application framework is involved.

## Troubleshooting

- **Capture fails:** read the details in the session CSV. Use **Erneut versuchen** for a transient error or **Nächste URL** to change source.
- **Browser not found:** select `msedge.exe` or `chrome.exe` using **Auswählen…**. Leave the field empty for automatic detection.
- **Helper cannot download:** verify GitHub is reachable. Alternatively download the pinned [official Windows executable](https://github.com/gildas-lormeau/single-file-cli/releases/download/v2.26.0/single-file.exe) and place it at `%LOCALAPPDATA%\PageCollector\tools\single-file-2.26.0.exe`. The application still verifies the hash before using it.
- **Windows blocks this unsigned prototype:** build locally from the included source; the package is not code-signed.
- **Incomplete text:** increase the additional wait before starting a new run. Content requiring login or manual page interaction is outside this first version's capture flow.

## Third-party projects

[SingleFile CLI](https://github.com/gildas-lormeau/single-file-cli) by Gildas Lormeau is a separate AGPL-licensed executable downloaded on first use. It is not bundled in this ZIP. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the pinned release, digest and runtime notices.
