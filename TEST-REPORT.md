# Tests and verification

Run `.\Verify.ps1` on Windows to reproduce the CI checks: restore, `dotnet format --verify-no-changes --severity warn`, Release build of the complete solution, and MSTest. TRX files are written to `artifacts/TestResults` and uploaded by GitHub Actions. `Build.ps1 -Publish` additionally produces a self-contained Windows x64 application.

## Automated coverage

| Area | Checks |
| --- | --- |
| URL templates | Original placeholders, indexed lists/ranges, leading zeros, Unicode text elements, URL encoding and validation before capture. |
| Title rules and filenames | Prefix/substring exclusions, full-title matching, literal punctuation, missing titles, affixes, Windows filename safety and word-boundary truncation. |
| Collection and storage | Priority/reset behavior, automatic rejection/failure/exhaustion, cancellation, destination failures, unchanged HTML, full provenance, collisions and CSV escaping. |
| HTML reader | Large embedded resources, comments/scripts/inert templates, entity decoding and incomplete titles. |
| Capture process | Real child-process success/failure, missing/invalid output, cancellation, timeout and draining both diagnostic streams. The controlled executable runs on Windows and Linux. |
| View model | Automatic/manual collection, next source, invalid inputs, preparation failure, command availability, cancellation, save retries, property notifications and old/new settings. |
| Windows UI | Real XAML bindings, two-way input, command bindings and manual-control visibility on an STA thread. |
| Settings storage | Replacement/readback without leftover temporary files, and malformed JSON. |

The deterministic suite does not download SingleFile or contact external websites. Each test owns its temporary files. Core test classes may run in parallel; WPF tests are serial because tracing and dispatcher state are shared.

The core suite was executed in the Linux development environment: **31 passed, 2 optional browser tests skipped**. The complete solution also compiled in Release with **zero warnings and zero errors**. Windows runtime checks are delegated to the Windows CI job. A cross-platform build alone is not evidence that the WPF window ran successfully.

## Optional real-browser integration

Start the checked-in HTTP fixture in a separate terminal:

```powershell
python -m http.server 8765 --bind 127.0.0.1 --directory tests/fixture
```

In the test terminal, configure an existing SingleFile CLI executable and an installed Edge/Chrome browser:

```powershell
$env:PAGECOLLECTOR_TEST_ENGINE = "$env:LOCALAPPDATA\PageCollector\tools\single-file-2.26.0.exe"
$env:PAGECOLLECTOR_TEST_BROWSER = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
$env:PAGECOLLECTOR_TEST_URL = 'http://127.0.0.1:8765/index.html'
dotnet test tests/PageCollector.Core.Tests -c Release --filter TestCategory=BrowserIntegration
```

Without all three variables, the two tests are reported as skipped. They verify embedded styling/images, delayed content and automatic rejection of Wayback/404 fixture titles before accepting the article. The default CI does not claim a successful real-browser capture.

## Manual Windows smoke test

1. Publish with `.\Build.ps1 -Publish`, start with `.\Start.cmd` and check that existing settings were retained.
2. Click **Demo laden → Starten**. Two files with ` - Example Domain` should be saved without previews or approval prompts.
3. Set prefix `KB` and suffix `_DEU`, inspect the example, and repeat. Existing files must not be overwritten.
4. Collect representative real URLs with your exclusion list and review the saved HTML and CSV. Title checks cannot detect every error page.
5. Disable automatic mode. Verify preview, acceptance, next URL, retry and skipping.
6. Cancel an active capture, then start again. Also close the window during capture and confirm that no helper process remains.
7. Check browser/file dialogs, output-folder opening and result double-clicks. These OS interactions are not covered by the binding test.
