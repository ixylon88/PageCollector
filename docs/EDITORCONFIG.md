# Formatting and analyzers

The repository-root `.editorconfig` is the source of truth. The imported preferences remain in place; its header and stale comments now describe PageCollector. The YAML override uses LF for GitHub Actions files. `.gitattributes` ensures consistent checkout line endings on all operating systems.

| Area | Setting / effect |
| --- | --- |
| Encoding | UTF-8 without BOM. |
| Line endings | CRLF by default; YAML uses LF. |
| C# | Four spaces; no required final newline; braces for multiline statements. |
| XAML, project files, props, JSON | Two spaces. |
| Readability | Blank lines separate members, validation, I/O and assertions. Comments explain intent where the code alone is insufficient. |
| Analyzer default | Error, with the existing rule-specific overrides. |
| Formatting | `IDE0055` is a warning and checked by `dotnet format`. |
| Compiler warnings | `TreatWarningsAsErrors` remains enabled in `Directory.Build.props`. |
| Optional analyzers | StyleCop, AsyncFixer and VSTHRD settings only apply if their analyzer packages are installed; this solution does not add those packages. |

`Verify.ps1` checks formatting and diagnostics at warning/error severity, then builds and runs tests. Suggestions remain editor hints. To apply supported fixes before verifying:

```powershell
dotnet format PageCollector.sln --severity warn
.\Verify.ps1
```

The existing rules exposed `SYSLIB1045` and `CA1869` in the imported source. Fixed regex patterns now use generated regex methods, and JSON serialization reuses immutable options. These diagnostics were fixed in code rather than disabled.

`dotnet format` formats C#; XAML, PowerShell, Markdown and workflow edits must also follow their EditorConfig settings during review.
