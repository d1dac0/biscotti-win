# Landing-page screenshots

Captured from the Windows app at revision `760cdf0` on 2026-10-23, using its
built-in in-memory design preview and Windows dark app mode. These are actual
window captures, not mockups. Only synthetic Project Atlas meetings and calendar
entries appear. No user profile, meeting storage, provider credentials, recording
or model downloads are used by the preview.

From the repository root on Windows with the .NET 10 SDK:

```powershell
dotnet run --project Biscotti.Win -c Release -- --design-preview=history
dotnet run --project Biscotti.Win -c Release -- --design-preview=review
dotnet run --project Biscotti.Win -c Release -- --design-preview=calendar-connect
```

- `meeting-home.png`: history preview, maximized to show the calendar, recording
  controls and meeting history. The page continues below the visible viewport.
- `meeting-transcript.png`: review preview with the Transcript tab selected.
  The preview has no audio, so the player shows zero duration. Playback itself
  is not demonstrated by this image.
- `calendar-connections.png`: calendar-connect preview. The selected Work
  calendar is synthetic; no provider account was opened.

Dates and their formatting depend on capture time and Windows locale. Disabled
controls and the preview banner are intentional. Close preview windows when done.
