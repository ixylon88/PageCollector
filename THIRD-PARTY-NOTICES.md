# Third-party components

## SingleFile CLI (downloaded separately)

- Project: https://github.com/gildas-lormeau/single-file-cli
- Author: Gildas Lormeau and contributors.
- Releases and download assets: https://github.com/gildas-lormeau/single-file-cli/releases
- Corresponding source and license: select the same `v<version>` tag at https://github.com/gildas-lormeau/single-file-cli/tags
- License: GNU Affero General Public License version 3; see the chosen upstream release's license and notices for its dependencies.
- The app offers stable Windows releases with a published SHA-256 digest and verifies each executable against its release metadata.
- Cached version, size and digest information is stored in `%LOCALAPPDATA%\PageCollector\tools\engine.json`.
- Legacy migration still recognizes release 2.26.0 (81,871,015 bytes, SHA-256 `55fd4a6275a5d4a3af5cdd86c742761cc1ac2c1c0d4ab9481b3fa600c003d5ff`).

The helper executable and its source are not included in this package. PageCollector launches the separately downloaded, checksum-verified executable with command-line arguments. No SingleFile source code has been copied into PageCollector.

## Microsoft .NET runtime (included in the Windows application folder)

The self-contained Windows build includes Microsoft .NET and Windows Desktop runtime binaries. Their license/notice files are included in the `app` folder as `DOTNET-LICENSE.txt`, `DOTNET-THIRD-PARTY-NOTICES.txt` and `WINDOWSDESKTOP-LICENSE.txt`. These components retain their own licenses.

The PageCollector source itself is covered by the accompanying MIT license.
