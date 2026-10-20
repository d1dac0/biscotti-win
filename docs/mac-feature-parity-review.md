# Mac / Windows feature parity review

**Status: initial reconnaissance; not the completed acceptance review.**

Issue: [#79](https://github.com/d1dac0/biscotti-win/issues/79).
Review date: **2026-10-20**. Implementation awaits approval of the plan in
[PLAN.md](../PLAN.md). Do not interpret the observations below as a complete
feature matrix or completed visual verification.

## Pinned inputs

| Input | Revision / evidence |
| --- | --- |
| Mac main, fetched for this review | `f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a` |
| Windows origin/main, clean worktree baseline | `ade9975a881b51200fd48b39d17675192dac3252` |
| Mac current release | [v1.0.0](https://github.com/scosman/Biscotti/releases/tag/v1.0.0), published 2026-10-08; release notes cover cross-meeting voiceprints, CSV import/export, stale notification cleanup and audio-device recovery |
| Mac license | [LICENSE.md at review SHA](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/LICENSE.md): PolyForm Perimeter 1.0.1; includes Noncompete and notice requirements. No reference source/artwork/font reuse is proposed. |
| Windows open issues at review start | #79 and #53; no open `type:bug` issues |
| Windows open PRs at review start | #55 (logo; #53), #23 (capture statistics/silence follow-up); recheck before modifying overlapping code |

Mac paths below are relative to `scosman/Biscotti` at the pinned revision;
Windows paths are relative to this repository at its pinned baseline.

## Initial evidence and proposed alignment

| Flow | Mac evidence inspected | Windows evidence inspected | Initial observation / planned UI scope |
| --- | --- | --- | --- |
| Home and meeting list | `Packages/BiscottiKit/Sources/HomeUI/HomeView.swift`: centered scrolling column, greeting, stat chips, upcoming and past sections, calendar empty state | `Biscotti.Win/MainWindow.xaml`: stacked recording/model/device controls, app picker, upcoming calendar and meeting ListBox | Group existing Windows workflows into distinct sections with consistent card/heading hierarchy and a visible history empty state. |
| Visual system | `Packages/BiscottiKit/Sources/DesignSystem/Color+Theme.swift`, `Tokens.swift`: shared warm surfaces, sage, semantic recording/error colors, typography and spacing | `Biscotti.Win/App.xaml` has no shared resources; XAML windows use individual literal colors and spacing | Author shared WPF resources; verify text contrast, keyboard focus and resizing. Do not copy reference implementation or fonts. |
| Meeting review | `Packages/BiscottiKit/Sources/MeetingDetailUI/MeetingDetailView.swift`: loading, summary/notes/transcript states, editable title, pinned transport, delete confirmation | `Biscotti.Win/MeetingWindow.xaml`: editable title, Transcript/Summary/My notes/Audio tabs, live note entry, per-channel playback, Save/Delete | Align tab wording and content hierarchy, add explicit empty/working states, and retain existing notes and playback. Advanced transport is a candidate follow-up, not silently included in the layout pass. |
| Setup and settings | `Packages/BiscottiKit/Sources/OnboardingUI/OnboardingView.swift`; `Packages/BiscottiKit/Sources/SettingsUI/SettingsView.swift` | `Biscotti.Win/MainWindow.xaml.cs`: integrity check, model download progress and GPU choice; `SettingsWindow.xaml`: shortcut/vocabulary; `MainWindow.Shell.cs`: Settings entry | Model setup exists. Improve its presentation in the existing main window, expose Settings there, and harmonize settings/device dialogs. Full onboarding and model management need deeper inventory before assigning status. |
| History search | Mac has MeetingListUI and search-related tests; implementation inventory pending | `Biscotti.Mcp/MeetingQueryService.cs` and `MeetingTools.cs` implement saved-meeting text search; main-window XAML has no search control | Avoid calling search wholly missing. Distinguish MCP search from UI search in the full matrix. |
| Speaker identity | v1.0.0 release describes cross-meeting voiceprints; source verification pending | `SpeakerDiarizer.cs`, `SpeakerIntroductionResolver.cs`, `SpeakerDiarizationTests.cs`; README lists cross-meeting voiceprints as future work | Distinguish existing within-meeting diarization from persistent speaker identity. Finish source review before creating a gap story. |

## Completion work

Expand this into the complete capability matrix required by #79, with both
products' source/test evidence, status, impact, Windows approach and a linked
issue for every deferred gap. Inspect the remaining Mac implementation and
tests rather than deriving statuses from release claims or documentation alone.
Cover setup/permissions/models; capture/app compatibility; transcripts/speakers;
summaries/actions/participants; calendar/auto-stop/tray/shortcuts/notifications;
vocabulary/notes/history/export; MCP/privacy/retention/recovery; all major UI
states. Link relevant previously closed issues as implementation history, not
as proof that every acceptance criterion is satisfied.

Prepare a synthetic fixture (e.g. "Project Atlas planning", Alex and Jordan,
short transcript, one action item and one timestamped note) for every screen.
Include setup, list, recording, transcript/summary/notes, settings, tray and
notifications. Capture Windows before/after and annotate Mac evidence with
provenance. This Windows environment cannot execute the Mac app; actual Mac
runtime captures remain outstanding. Source-derived diagrams must be explicitly
labelled and are not runtime evidence. Do not use personal meeting data or
misrepresent a mockup as a screenshot.

## Verification so far

- Clean worktree `tools/build.ps1`: passed (0 errors; existing NAudio alias and
  test-read analyzer warnings).
- Clean worktree `tools/check.ps1`: passed, **98 passed, 0 failed, 0 skipped**.
- App code changes: none.
- Manual app walkthrough, screenshot comparison and accessibility checks:
  pending; no completion claim.
- Extended checks/reviews beyond the required build/check/manual walkthrough:
  not run; optional at implementation review.
