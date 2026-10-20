# Windows ZIP installation

The release is a self-contained, signed Windows x64 ZIP. It includes the app,
.NET 10 runtime, Windows App SDK runtime, native CPU transcription libraries,
and third-party notices. It does not include the Whisper models or optional
CUDA runtime: Biscotti downloads those to `%LOCALAPPDATA%\BiscottiWin` when
needed.

## Install

1. Download and extract `BiscottiWin-<version>-win-x64.zip`.
2. Move the extracted `BiscottiWin` folder to
   `%LOCALAPPDATA%\Programs\BiscottiWin`.
3. Open `Biscotti.Win.exe` from that folder. No administrator rights or
   separately installed .NET runtime are required.

Windows may show a SmartScreen prompt for a new release while reputation
builds. Before running it, check that the executable's digital signature names
the expected publisher and that the ZIP came from the official release page.
Do not disable SmartScreen or run the file if the publisher or source is
unexpected.

## Update

Close Biscotti, extract the newer ZIP to a temporary folder, then copy its
contents over `%LOCALAPPDATA%\Programs\BiscottiWin`, replacing existing files.
The next launch uses the new version. Updates are manual and require another
release download.

## Uninstall and retained data

Close Biscotti and delete `%LOCALAPPDATA%\Programs\BiscottiWin`. This removes
the application files only. Meetings, settings, Whisper models, speaker model,
and optional GPU runtime remain under `%LOCALAPPDATA%\BiscottiWin`; back up or
remove that data separately if desired.

## Dependencies and licensing

The release contains the runtime files for the Windows x64 application,
`THIRD-PARTY-NOTICES.md`, and a `LICENSES/` directory with resolved NuGet
licenses plus version-matched .NET runtime and WPF notices. Whisper model files
and the CUDA runtime download on demand to the local data folder, as described
in the notices and [performance matrix](transcription-performance-matrix.md).

## Publisher signing setup

The release workflow runs when a `v<version>` tag is pushed. Before the first
release, create a GitHub Environment named `windows-release` and add these
Environment secrets:

- `WINDOWS_SIGNING_PFX_BASE64`: base64-encoded PFX for a publicly trusted
  Authenticode publisher certificate.
- `WINDOWS_SIGNING_PFX_PASSWORD`: the PFX password.

The release job signs `Biscotti.Win.exe` with SHA-256 and an RFC 3161
timestamp, verifies the signature, then publishes the ZIP and its SHA-256
checksum. It fails before publication if either secret is missing. Keep the
PFX and password out of the repository, tags, logs, and release assets. A new
publisher or file hash may still receive a SmartScreen warning while reputation
builds; the workflow does not suppress or advise bypassing that warning.
