# Third-party components

## SingleFile CLI (downloaded separately)

- Project: https://github.com/gildas-lormeau/single-file-cli
- Author: Gildas Lormeau and contributors.
- Pinned release: https://github.com/gildas-lormeau/single-file-cli/releases/tag/v2.26.0
- Corresponding source and license: https://github.com/gildas-lormeau/single-file-cli/tree/v2.26.0
- License: GNU Affero General Public License version 3; see the upstream license and notices for its dependencies.
- Download: https://github.com/gildas-lormeau/single-file-cli/releases/download/v2.26.0/single-file.exe
- Size reported by the release: 81,871,015 bytes.
- SHA-256: `55fd4a6275a5d4a3af5cdd86c742761cc1ac2c1c0d4ab9481b3fa600c003d5ff`.

The helper executable and its source are not included in this package. PageCollector launches the separately downloaded, checksum-verified executable with command-line arguments. No SingleFile source code has been copied into PageCollector.

## Microsoft .NET runtime (included in the Windows application folder)

The self-contained Windows build includes Microsoft .NET and Windows Desktop runtime binaries. Their license/notice files are included in the `app` folder as `DOTNET-LICENSE.txt`, `DOTNET-THIRD-PARTY-NOTICES.txt` and `WINDOWSDESKTOP-LICENSE.txt`. These components retain their own licenses.

The PageCollector source itself is covered by the accompanying MIT license.
