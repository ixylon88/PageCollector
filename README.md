# PageCollector 0.2

A small C# / WPF application for Windows that tries URL templates in order and saves the first captured page whose title passes an editable exclusion list. SingleFile CLI captures the rendered page as one HTML file. Manual review remains available.

## Quick start

1. Extract the entire ZIP and run `Start.cmd` (or `app/PageCollector.exe`).
2. Enter search values and URL templates in the first tab, or use **Demo laden** for a functional test with two values on `example.com`.
3. The **Automatik und Ausschlusstitel** tab enables automatic collection by default. Review its title exclusions before a real collection.
4. Optionally set a filename prefix and suffix in **Dateinamen**. Appending the page title is enabled by default.
5. Select an output directory and click **Starten**. The application finishes each value before moving to the next; no browser tabs or approval prompts are opened in automatic mode.
6. Open saved results by double-clicking their rows, or use **Ausgabeordner öffnen**.

The package targets **Windows x64** and includes its .NET runtime. Edge or Chrome must be installed. The first run downloads the pinned SingleFile CLI 2.26.0 helper (about 82 MB) from its official GitHub release and verifies its SHA-256 digest. An already downloaded, verified helper is reused. A preview can be opened in any default browser, including Firefox.

For the German walkthrough, see [START-HIER.txt](START-HIER.txt). Build and test details are in [TEST-REPORT.md](TEST-REPORT.md).

## Changes from 0.1

- One-based character selections, inclusive ranges and lists inside URL placeholders.
- Automatic collection with editable title exclusions; manual review remains selectable.
- Configurable filename prefix and suffix, with an optional page title capped at 100 characters.
- Full title extraction even after large embedded resources; the full title is retained in provenance.
- Automatic failures, title rejections and exhausted values are logged; temporary captures are removed after every attempt.
- Existing 0.1 settings are retained. Missing new settings default to automatic collection, the two standard exclusions, empty affixes and title filenames.

## Search values and URL templates

Enter one value per line, paste a column, or load a UTF-8 TXT file. Blank lines are ignored; duplicates and leading zeros are retained. Multi-column CSV input is not parsed.

Enter one HTTP(S) URL template per line, in priority order. `{id}` and `{Suchwert}` are equivalent and case-insensitive, including indexed forms. Positions start at **1** and range endpoints are inclusive.

For the value `123456`:

| Placeholder | Result |
| --- | --- |
| `{id}` | `123456` |
| `{id:1-3}` | `123` |
| `{id:1,2,3}` | `123` |
| `{id:4}` | `4` |
| `{id:5-6}` | `56` |
| `{id:3,1,3}` | `313` |
| `{Suchwert:1-3}` | `123` |

The following are schematic examples; replace their hosts with real source addresses:

```text
https://www.testurl.com/asdf{id:1-3}/{id:4}/{id:5-6}
https://www.testurl2.com/ululu{id:1-3}/{id}
```

They expand to:

```text
https://www.testurl.com/asdf123/4/56
https://www.testurl2.com/ululu123/123456
```

Lists may mix individual positions and ranges, for example `{id:1-3,5,6}`. Selection happens before URL encoding. Each inserted selection is encoded separately; the template's separators remain intact. Unicode text elements stay together. Placeholders belong in the path or query, not the host or URI scheme.

Every template is validated against every supplied value before the run. Position 0, descending ranges, unknown placeholders and out-of-range selections produce a clear error instead of silently generating a wrong URL. A too-short value therefore needs correcting or collecting in a separate run with suitable templates.

Existing Wayback replay URLs can be used as templates. PageCollector does not query the Wayback API to find snapshot timestamps.

## Automatic title rules

The default list contains:

```text
^Wayback Machine
404
```

One rule goes on each line. Empty lines are ignored. Matching ignores case and normalizes whitespace, using the **full original title**, before filename shortening.

| Rule | Meaning |
| --- | --- |
| `^Wayback Machine` | Reject titles starting with `Wayback Machine`. |
| `404` | Reject titles containing the literal text `404` anywhere. |
| `nicht gefunden` | Optional: reject titles containing this text. |
| `^Access Denied` | Optional: reject titles starting with this text. |

Only the leading `^` has a special meaning; other characters are literal, not regular expressions or wildcards. The rules apply globally to all templates. Remove all lines to disable these exclusions; a page without a title is still not automatically accepted.

Broad exclusions can also reject useful articles. For example, `404` matches a title containing `KB123404` or an article about HTTP 404. Likewise, an article about a "file not found" error may be useful, so that phrase is deliberately **not** a default exclusion. Adjust the list for the source material.

For each value, the automatic runner:

1. Captures the first URL to a temporary file.
2. On capture failure or timeout, logs the error and tries the next template.
3. On an excluded or missing title, discards the temporary capture and tries the next template.
4. On a passing title, saves the HTML and provenance. Only successful storage ends the search for this value.
5. Starts again at the first template for the next value. When all templates fail, logs **Kein Treffer** and continues.

A destination write failure stops the run. **Abbrechen** cancels capture or storage and preserves all previously saved results. There is a short delay between attempts, but no automatic retry of a failed URL in this version. Use the log to collect missing values again.

Title checks are a heuristic, not a content or HTTP-status validator. A saved error page whose title passes the list can still be accepted. Errors mentioned only in the body, login pages and robot checks may require later inspection. HTTP 404 status is not independently evaluated; the default `404` rule checks the title.

## Manual review

Clear **Automatisch speichern und weiterlaufen** to use the original review flow. Manual buttons appear; the optional automatic preview setting applies only in this mode. Title rules are shown as hints and do not prevent manual acceptance.

| Button | Effect |
| --- | --- |
| Vorschau öffnen | Open the currently captured temporary HTML again. |
| Übernehmen & weiter | Save that captured file, then start the next value. |
| Nächste URL | Try the next template; after the last template, move to the next value. |
| Erneut versuchen | Capture the same URL again. |
| Wert überspringen | Skip all remaining templates for this value. |
| Abbrechen | Stop the run; previously saved files remain. |

A capture failure in manual mode keeps the current source selected. Previewing does not visit the live site again. Browser tabs are not automatically closed. Temporary preview files are deleted at session completion; reopen the permanent saved copy afterwards.

## Filenames and output

The filename layout is:

```text
<prefix><search value><suffix> - <page title>.html
```

For prefix `KB`, value `123456`, suffix `_DEU` and title `Installing the update`:

```text
KB123456_DEU - Installing the update.html
KB123456_DEU - Installing the update.source.json
session-<timestamp>-<unique-id>.csv
```

Affixes are literal text; separators such as `_` or spaces are entered explicitly. The suffix follows the search value, before the optional title. The title option removes both ` - ` and the title when disabled. A live example is shown in the settings tab.

- The filename title is at most **100 characters**, shortened at the last complete word. If a single word exceeds the limit, it is cut at a Unicode text-element boundary.
- Windows-forbidden filename characters are replaced with underscores; trailing spaces/dots and reserved device names are handled safely.
- Each affix may contain up to 40 characters. Very long search values are shortened to 80 characters in filenames. The combined filename stem is bounded to leave room for extensions and collision counters; this may shorten the title further.
- The **full title and search value** remain in `.source.json`, and the captured HTML bytes are unchanged.
- Existing files are never overwritten. Collisions produce `... (2).html`, with a matching sidecar, then `(3)`, etc.

The JSON sidecar records the value, template, requested URL, full title, capture/acceptance timestamps, saved filename and SHA-256. `approval` is `automatic-title-filter` or `manual`. `requestedUrl` is not labelled as the final redirected URL. SingleFile normally also embeds source information in the HTML.

The semicolon-separated UTF-8 CSV (with BOM for Excel) records attempted URLs, title rejections, capture/save errors, accepted files and values with no accepted candidate. Settings are retained in `%LOCALAPPDATA%\PageCollector\settings.json`; the helper is in `%LOCALAPPDATA%\PageCollector\tools`. Existing settings from 0.1 are read from the same location.

## Capture behaviour and limits

- A fresh browser profile is used; existing browser logins and extensions are not reused.
- The viewport is 1440 × 1000 pixels. Available resources and deferred content are captured; scripts are removed from the saved copy. Audio/video are excluded.
- Additional wait: 0–30 seconds (default 1.5). Loading and capture each have a 60-second limit; the entire helper process has a 150-second limit.
- This is a readable snapshot. Missing archive resources and server-side interactions cannot be restored.
- Inputs/settings cannot change during a run. There is no pause/resume checkpoint or automatic skip of existing archives. Rerunning a value creates an additional file.
- Normal completion/cancellation removes session previews. A crash may leave temporary files behind.
- Windows ARM64 is not a separately built or tested target.

## Build and verification

Install a .NET 10 SDK on Windows, then run:

```bat
Build.cmd
Verify.cmd
```

`Build.cmd` publishes a self-contained build to `dist\win-x64`; `Run-Source.cmd` runs from source. Equivalent commands:

```text
dotnet build src/PageCollector.App/PageCollector.App.csproj -c Release
dotnet run --project tests/PageCollector.Checks -c Release
dotnet publish src/PageCollector.App/PageCollector.App.csproj -c Release -r win-x64 --self-contained true -o dist/win-x64
```

There are no application NuGet dependencies beyond the framework. `PageCollector.Core` contains URL expansion, title reading/filtering, sequential collection, storage and process/download handling. The WPF application owns the UI and settings. See [TEST-REPORT.md](TEST-REPORT.md) for automated checks and the optional local browser fixture.

## Troubleshooting

- **No accepted page:** check the session CSV for title exclusions, timeouts or capture errors. Narrow overly broad rules or try the affected value in manual mode.
- **Browser not found:** select `msedge.exe` or `chrome.exe`, or leave the browser field empty for automatic detection.
- **Incomplete content:** increase the additional wait before starting again.
- **Helper cannot download:** check GitHub access, or download the pinned [official Windows executable](https://github.com/gildas-lormeau/single-file-cli/releases/download/v2.26.0/single-file.exe) to `%LOCALAPPDATA%\PageCollector\tools\single-file-2.26.0.exe`. Its hash is still checked before use.
- **Windows blocks the unsigned build:** the included source can be built locally.

## Third-party projects

[SingleFile CLI](https://github.com/gildas-lormeau/single-file-cli) by Gildas Lormeau is a separate AGPL-licensed executable downloaded on first use. It is not bundled in this ZIP. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the pinned release, digest and runtime notices.
