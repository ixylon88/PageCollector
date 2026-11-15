# Verification of prototype 0.1

Prepared on 2026-11-14. Tests ran in a Linux build environment; this is not a claim that the Windows UI was exercised.

| Check | Result |
| --- | --- |
| Release compilation of the WPF application | Passed; zero warnings, zero errors. |
| Self-contained `win-x64` publish | Passed. |
| Produced app host | PE x64, GUI subsystem; `.NET` runtime included. |
| Core automated checks | 11 passed on Linux. |
| Official Windows SingleFile 2.26.0 download and SHA-256 verification | Passed. |
| Actual SingleFile browser capture of the bundled local fixture | Blocked by the test host: Chromium cannot create required Unix-domain sockets (`Operation not permitted`). No end-to-end capture success is claimed. |
| Windows UI, file association, Edge/Chrome launch and end-to-end collection | Not run; requires a Windows machine. |

The 11 core checks cover special-character URL substitution, invalid-template rejection, source priority and first acceptance, exhaustion/retry semantics, Windows filename/path safety, byte-preserving saves with provenance and collision suffixes, cancelled saves, readable/escaped CSV logs, process failure and prompt cancellation of a running helper. The last two process checks use Unix test executables and are omitted when the checks run on Windows. The optional helper-download check is additional to the 11 core checks.

## First Windows smoke test

1. Start the packaged application and run **Demo laden → Starten**.
2. Confirm that the helper downloads and a saved `Example Domain` page opens.
3. Accept the first sample value, then skip or accept the second.
4. Confirm `.html`, `.source.json` and the session CSV in the output directory.
5. Repeat the first value: the previous HTML must remain unchanged and a suffixed file must be created.
6. Try two real source templates, reject the first candidate and accept the second. Confirm that no third template is fetched after acceptance.
7. Cancel a capture and confirm that already accepted files remain.

## Optional capture integration fixture

The `tests/fixtures` directory contains an HTML page with external CSS, SVG and delayed JavaScript text. Serve it locally, for example:

```text
python -m http.server 8765 --bind 127.0.0.1 --directory tests/fixtures
```

In another terminal, pass four arguments to the checks: helper path, browser path, fixture URL and output directory. On Windows, for example:

```bat
dotnet run --project tests/PageCollector.Checks -c Release -- "%LOCALAPPDATA%\PageCollector\tools\single-file-2.26.0.exe" "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" "http://127.0.0.1:8765/index.html" "capture-test"
```

Adjust the browser path if necessary. After a successful capture, stop the fixture server and reopen `capture-test/offline.html` to check the offline rendering. The integration test supplies a container-specific browser argument only on Linux; the Windows application does not disable the browser sandbox.
