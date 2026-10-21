# #79 screen evidence and verification

Captured 2026-10-20 with the isolated Project Atlas synthetic fixture in
`Biscotti.Win/DesignPreview.cs`. Open [comparison.html](comparison.html) for
annotated side-by-side source diagrams and runtime captures. A standalone [Mac source hierarchy diagram](mac-source-hierarchies.svg) is also attached to the PR.

## Provenance

- Mac: source-derived hierarchy diagrams, linked to
  `f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a`. These are **not Mac screenshots**.
  The Mac app/test suite was not run here. No Mac code/artwork/fonts are copied.
- Windows: actual WPF/WinForms window captures taken through the computer-use
  plugin. All meetings, names, notes, actions, event and recording states are
  synthetic. Preview startup bypasses profile settings/storage/downloads and
  capture. Audio-play actions explicitly confirm that no audio is opened.
- Some screenshots were taken during the UI iteration; differences such as the
  preview-only tray-menu button do not affect production behavior. The final
  notes capture shows the bounded timestamp list and labelled editor.
- The processing capture has the same visual progress placement as the final
  dedicated local-processing progress bar; the final bar has its own accessible
  name and is shown by the shared production/preview presentation method.
- Native tray popup is opened by the preview using the real app context menu,
  anchored in the preview window rather than the taskbar for a bounded capture.

## Captures

| Evidence | What to inspect |
| --- | --- |
| [Setup](windows-setup.png) | Missing-model explanation, disabled recording, no-meetings state |
| [Home](windows-home.png), [history](windows-history.png) | Display/body hierarchy, upcoming/recent sections, cards and local metadata |
| [Recording](windows-recording.png), [live transcript](windows-recording-transcript.png) | Red stop/duration, live text and protected editing controls |
| [Live notes](windows-live-notes.png) | Added synthetic note at 02:18, cleared composer and saved feedback |
| [Processing](windows-processing.png) | Progress and disabled start/device/source controls |
| [Summary](windows-summary.png), [transcript](windows-transcript.png) | Stored actions, speaker/time cards and readable content |
| [Notes](windows-notes.png), [audio](windows-audio.png) | Labelled Markdown editor, bounded note list, per-channel play/stop and preview isolation |
| [Settings](windows-settings.png), [devices](windows-devices.png) | Shared surfaces, headings, native controls and synthetic device choices |
| [Failure](windows-failure.png) | Explicit reason and recovery guidance; combined synthetic error example |
| [Tray](windows-tray.png), [menu detail](windows-tray-menu.png) | Native start/stop, open-last, Settings and Exit |

## Verification record

- Baseline `tools/build.ps1` and `tools/check.ps1`: passed, 98 tests.
- Final `tools/build.ps1`: passed, 0 errors. Existing NAudio alias warnings
  remain; no new warning category was introduced.
- Final `tools/check.ps1`: passed, **99 passed, 0 failed, 0 skipped**
  (98 core/MCP/regression tests + 1 native STA WPF flow test).
- WPF regression checks setup model gating, empty/history/processing states,
  live Save/audio restrictions, completion binding refresh, action-list
  replacement, synthetic device choices/edit lock, and high-contrast resource
  switching/restoration without changing OS settings.
- Manual synthetic walkthrough: setup/empty; recording; adding a timestamped
  note; stop; completed Summary/Transcript/Notes/Audio; Settings; audio-device
  dialog; Enter opening of the selected history item; native tray popup and Exit.
  Tray Exit disposed the preview process; unrelated Biscotti instances were
  left running.
- Actual `dotnet run --project Biscotti.Win -- --design-preview=...` launches
  were used during verification. The executable location was read from the
  build output/file system and supplied in the handoff; no machine-specific
  paths are committed.
- `git diff --check`: passed.
- Gallery image references checked against the files in this directory.

## Explicit limits

No real meeting/audio/profile data was used. Preview verifies the real screens
and state transitions, **not** audio hardware or actual model inference.
Existing synthetic regression tests cover storage/transcription behavior.
No macOS execution, Windows screen-reader session, OS high-contrast toggle,
200% display scaling or full hardware meeting-app matrix was performed.
Cross-platform runtime visuals and appearance work are tracked in
[#89](https://github.com/d1dac0/biscotti-win/issues/89); capture/echo work in
[#90](https://github.com/d1dac0/biscotti-win/issues/90). The Windows dark
palette and the validation completed for #89 are recorded in
[issue-89-appearance.md](issue-89-appearance.md).
Extended separate review and local release-package smoke were not run; CI
already includes the release-package smoke. Final CI outcome is linked on the PR.

Reproduce with:
```powershell
dotnet run --project Biscotti.Win -- --design-preview=history
```
Other states: setup, empty, recording, processing, failure, review, tray.
