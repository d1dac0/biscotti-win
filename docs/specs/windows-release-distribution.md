# Windows releases use a signed per-user ZIP

**Status:** Accepted

## Decision

Distribute the first Windows release as a signed `win-x64` ZIP. Publish the WPF
app self-contained for .NET 10 and Windows App SDK, and sign its executable with
a trusted Authenticode certificate supplied only to the GitHub release job.
Users extract it under `%LOCALAPPDATA%\Programs\BiscottiWin`; updates overlay a
new ZIP after closing the app; uninstall deletes that program folder. Keep
`%LOCALAPPDATA%\BiscottiWin` intact so recordings, settings, model downloads,
and optional GPU runtime survive updates and uninstall. Release publication
fails closed until the signing certificate is configured.

## Alternatives

- **MSI/MSIX installer** — adds system integration or package identity that the
  current app does not need and complicates the first release's data migration.
- **Framework-dependent ZIP** — smaller, but requires users to install the
  matching .NET Desktop Runtime and Windows App SDK runtime.
- **Ship model and GPU runtime files** — rejected because model/runtime
  downloads are large and already lazy, optional, integrity-checked, and stored
  in the local data directory.

## Why

A per-user ZIP needs no administrator rights and leaves the existing data root
outside the install folder. It is straightforward to smoke-test by extracting,
launching, overlaying an update, and removing the application directory on a
clean Windows runner. It has manual updates and no Start menu integration; this
is an intentional first-release scope. The tested baseline moves to .NET 10
LTS, which remains supported longer than the nearly ended .NET 9 release.

The signing key and password are GitHub Environment secrets, never repository
files or workflow arguments. SignTool uses SHA-256 Authenticode with an RFC 3161
timestamp. A valid signature displays a verified publisher but does not
guarantee that SmartScreen will omit its first-download warning; users should
verify the publisher and download source instead of bypassing a warning.

## References

- [Microsoft .NET release support policy](https://learn.microsoft.com/dotnet/core/releases-and-support)
- [Microsoft self-contained Windows App SDK deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [Microsoft SmartScreen reputation guidance](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)
- [Microsoft Authenticode timestamping guidance](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures)
