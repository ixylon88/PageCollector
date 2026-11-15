# Verification of PageCollector 0.2

Prepared on 2026-11-15. Verification ran in a Linux build environment using .NET SDK 10.0.100. The Windows UI was not exercised here.

| Check | Result |
| --- | --- |
| Release compilation of the WPF application | Passed; zero warnings, zero errors. |
| Self-contained `win-x64` publish | Passed. |
| Core automated checks | 26 passed on Linux. |
| Process-based capture / filter / automatic storage using a controlled test executable | Passed; the first excluded title is skipped and the second page is saved without approval. |
| Real SingleFile 2.26.0 / Chromium capture | Blocked by this host: Chromium cannot create its required Unix-domain socket (`Operation not permitted`). No successful real browser integration run is claimed. |
| Windows UI, file associations, Edge/Chrome and real archive collection | Not run for 0.2; requires a Windows machine. |

## Automated coverage

The checks exercise:

- Existing placeholder compatibility and encoded special characters.
- One-based single positions, comma lists, inclusive ranges, mixed selections, leading zeros and Unicode text elements.
- Invalid selectors, unknown placeholders, unsafe URL schemes/hosts and too-short values rejected before a run starts.
- Title prefix / substring rules, case-insensitive comparison, literal punctuation, missing titles and matches beyond the filename/title display limit.
- Loading a 0.1 settings file with the new automatic defaults, and round-tripping edited settings.
- First accepted source stopping further attempts for that value; resetting priority for the next value.
- Prefix/suffix placement, Windows filename safety, 100-character title limits, word boundaries and long single-word/Unicode cases.
- Full titles in metadata, unchanged captured HTML bytes, collision numbering and no overwrites.
- Title extraction after large stylesheets, ignoring comments/scripts/inert templates, entity decoding and incomplete HTML titles.
- Automatic rejection of dead ends, failures/timeouts and exhausted values; removal of temporary captures.
- Cancellation retaining completed results without accepting the interrupted value.
- Destination failures stopping the queue without falsely accepting a source.
- UTF-8 CSV escaping and a readable log during a run.
- Successful and failed helper processes and prompt termination on cancellation.

Three process checks use Unix shell test executables and are skipped on Windows; `Verify.cmd` runs the other 23 core checks there. The optional real-browser checks are separate from the 26 successful checks above.

## Windows smoke test

1. Extract the complete package to a new folder and run `Start.cmd`.
2. Check that old search values, templates, output directory and browser settings were retained. The new automatic and title-filename options should initially be enabled when upgrading from 0.1.
3. Click **Demo laden → Starten**. Two HTML files should be saved with ` - Example Domain` in their filenames. The run should finish without opening preview tabs or requesting acceptance.
4. In **Dateinamen**, set prefix `KB` and suffix `_DEU`; inspect the filename example and repeat the demo. Confirm the affixes and unchanged earlier files.
5. Add `^Example Domain` to the exclusions and rerun the demo. Both values should finish with no accepted page; no new HTML should appear.
6. Remove that rule and try the real source templates, including indexed placeholders. Check the CSV for ordered attempts and that later templates are not requested after a passing page is saved.
7. Try a too-short value for an indexed template. The start should fail with the affected value and template number.
8. Disable automation. Check that the manual review buttons and optional automatic preview work as before, with title exclusions displayed as hints.
9. Cancel a multi-value run. Previously saved files should remain; no partial new archive should count as accepted.
10. Close and reopen the application; check that mode, rules and naming settings persist. Resize the window and inspect all three settings tabs.

## Optional browser integration fixture

Serve the bundled fixture directory, for example with Python:

```text
python -m http.server 8765 --bind 127.0.0.1 --directory tests/fixtures
```

In another terminal on Windows, run:

```bat
dotnet run --project tests/PageCollector.Checks -c Release -- "%LOCALAPPDATA%\PageCollector\tools\single-file-2.26.0.exe" "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" "http://127.0.0.1:8765/index.html" "capture-test"
```

Adjust the browser path if necessary and use a fresh output directory. This adds two real-browser checks:

1. Capturing an article with external CSS, SVG and delayed JavaScript text. After capture, stop the server and open `capture-test/offline.html` to inspect the offline rendering.
2. Trying a Wayback-titled fixture, an HTTP-200 soft-404 fixture, the actual article, and a final trap URL in that order. The first two must be rejected, the article saved as `KB123456_DEU - PageCollector fixture.html`, and the final URL never requested.

The Linux integration fixture supplies a container-specific browser argument. The Windows application does not disable the browser sandbox. The blocked Linux browser run is an environment limitation, not evidence of successful capture on Windows.
