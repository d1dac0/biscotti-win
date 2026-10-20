# Mac / Windows feature parity review

Review date: **2026-10-20**. Story: [#79](https://github.com/d1dac0/biscotti-win/issues/79).
Scope: source-backed capability review plus the bounded Windows UI alignment.
The user approved autonomous execution on 2026-10-20. Major new capabilities are
tracked separately below.

## Revisions and method

| Input | Pinned revision / material |
| --- | --- |
| [Mac source tree](https://github.com/scosman/Biscotti/tree/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a) | `f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a`, current main when fetched |
| Windows clean origin/main baseline | `ade9975a881b51200fd48b39d17675192dac3252`; review changes are identified as **[#79](https://github.com/d1dac0/biscotti-win/issues/79)** |
| [Current Mac release v1.0.0](https://github.com/scosman/Biscotti/releases/tag/v1.0.0) | Published 2026-10-08: cross-meeting voice recognition, CSV import/export, notification cleanup, device recovery |
| Mac license checked | [LICENSE.md at pinned SHA](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/LICENSE.md): PolyForm Perimeter 1.0.1, including Noncompete and notice clauses |
| Tracker checked before taking work | No open bugs; open issues [#53](https://github.com/d1dac0/biscotti-win/issues/53) and [#79](https://github.com/d1dac0/biscotti-win/issues/79); open PRs [#55](https://github.com/d1dac0/biscotti-win/issues/55) (logo) and [#23](https://github.com/d1dac0/biscotti-win/issues/23) (recording health follow-up) |

The Mac repository is a reference. No Swift source, artwork or bundled font is
copied. This change uses independently authored WPF and Windows-installed Georgia,
Segoe UI and Consolas. #53 / PR #55 owns app and tray logo work.

Mac paths prefixed with a module (e.g. `HomeUI/`) are under
`Packages/BiscottiKit/Sources/`; named Mac test suites are under
`Packages/BiscottiKit/Tests/` unless identified as AudioCapture or Transcription
package tests. AudioCapture source is under `Packages/AudioCapture/Sources/`.
Windows source is under `Biscotti.Core/`, `Biscotti.Win/` or `Biscotti.Mcp/`;
existing test names resolve under `Biscotti.Tests/`. References to #79 describe
the submitted Windows source rather than the baseline SHA.

Review inspected screen implementations, settings, capture/orchestration,
transcription/AI, storage, tests, documented workflows and release notes.
Mac test **source** was inspected; Mac tests/app were not executed on Windows.
Statuses describe Windows capability relative to the source evidence, not
measured accuracy/performance equivalence. **implemented** means the specified
workflow exists; **partial** means a useful subset exists; **missing** means
the relevant Windows implementation and UI inventory have no equivalent;
**platform-specific** means a deliberate OS-specific approach with its impact
explained. A README omission alone was never used as missing-feature evidence.

For negative findings, inspect the complete `MainWindow.xaml`,
`MeetingWindow.xaml`, `SettingsWindow.xaml`, `Models.cs`, `AppSettings.cs`,
`MeetingService.cs` and storage/query implementations. Reproduce targeted
inventory with `rg -n -i 'voiceprint|export|import|search|seek|retry|regenerate' Biscotti.Win Biscotti.Core Biscotti.Mcp`.
Presence of a keyword is a lead, not proof of a working UI command.

## Capability matrix

| Capability | Mac source / test or workflow evidence | Windows source / test or workflow evidence | Status | User impact | Windows-equivalent approach | Follow-up |
| --- | --- | --- | --- | --- | --- | --- |
| First run / permission guidance | `OnboardingUI/OnboardingView{,Model}.swift`, `OnboardingStepViews.swift`; OnboardingUITests (disk/denial/granted) | `MainWindow.xaml.cs` GPU/model prompts, `AudioDevicesWindow`; `ModelStoreTests`, `AudioDeviceTests` | partial | Model download works, but permission/Ollama readiness is fragmented. | Keep Windows permission conventions; guided readiness and repair. | [#80](https://github.com/d1dac0/biscotti-win/issues/80) |
| Local model installation / configuration | `ModelManagementUI/ManageModelsSheet.swift`, `Intelligence/ModelPolicy.swift`; ModelManagementUITests, ModelManagerTests | `ModelStore`, `GpuRuntimeStore`, `AppSettings.OllamaModel`; `ModelStoreTests`, `GpuRuntimeTests`; Settings has no model chooser | partial | Users must arrange Ollama/model configuration separately. | Expose installed models, sizes, readiness and safe CPU fallback. | [#80](https://github.com/d1dac0/biscotti-win/issues/80) |
| Microphone + system capture; separate channels | `AudioCapture/AudioRecorder.swift` composes two engines; AudioRecorderTests, StartAlignmentTests | `Audio/DualChannelRecorder.cs`, `AntiAliasDecimator.cs`; `CrashSafetyAndAudioTests` | implemented | Both sides are recorded locally; no meeting bot. | WASAPI output + WaveIn microphone rather than Core Audio taps. | — |
| Meeting-app / app-only compatibility | `AudioCapture/LiveSystemCaptureEngine.swift`, `MeetingDetection/MeetingDetector.swift`; AudioProcessTests | `DualChannelRecorder`, `MainWindow.RefreshAudioApps`; `PerAppLoopbackTests` | platform-specific | Mac activity catalog and Windows process picker differ; a silent/unsupported target is not proof of compatibility. | Retain explicit app selection and fail-closed consent for whole-device fallback ([#67](https://github.com/d1dac0/biscotti-win/issues/67)); no new all-app compatibility claim. | — |
| Microphone voice isolation / acoustic echo | `AudioCapture/LiveMicCaptureEngine.swift` explicitly enables VoiceProcessingIO; LiveMicCaptureEngineTests | `DualChannelRecorder` uses WaveIn and resampling; no AEC stage in capture implementation | partial | Separate tracks do not remove speaker leakage into the mic. | Evaluate Windows voice processing using controlled audio. | [#90](https://github.com/d1dac0/biscotti-win/issues/90) |
| Start, stop, visible elapsed time | `RecordingUI/RecordingView.swift`, `RecordingController.swift`; RecordingViewModelTests | `MainWindow.StartRecordingAsync/StopRecordingAsync`, `MeetingTimeDisplay`; `MeetingTimeDisplayTests` | implemented | User explicitly starts recording; stop begins local processing. | Native buttons, visible duration, red active/stop state; [#79](https://github.com/d1dac0/biscotti-win/issues/79) adds grouped hierarchy. | — |
| Live / final transcript | `AppCore.stopRecording` enqueues `TranscriptionService.transcribe`; RecordingView is notes-focused, not a live transcript feed | `LiveTranscriptionWorker`, `MeetingService`, `MeetingWindow`; `LiveTranscriptionWorkerTests`, `TranscriptionFixtureTests` | implemented | Windows already offers incremental text as an additional workflow; final pass can regroup speakers. | Retain live window and completed review; do not infer Mac live streaming from README. | — |
| Timestamps / speaker labels / diarization | `MeetingDetailUI/TranscriptListView.swift`, Transcription package speaker tests | `WhisperTranscriber`, `SpeakerDiarizer`, `SpeakerIntroductionResolver`; `SpeakerDiarizationTests`, [#70](https://github.com/d1dac0/biscotti-win/issues/70) evaluation | implemented | Best-effort per-meeting labels; overlapping voices remain a known Windows limit. | Keep anonymous labels when evidence is weak; no claim of equivalent accuracy. | — |
| Cross-meeting identity / correction | `VoiceprintMatching/VoiceprintMatcher.swift`, `DataStore+Voiceprints.swift`; VoiceprintMatchingTests, PersonPickerLogicTests | `Models.cs` stores string speaker labels; `SpeakerDiarizer` clusters only this meeting; no person/voiceprint database | missing | A known colleague is not remembered by voice. | Local encrypted, user-correctable identity with uncertainty and forget controls. | [#82](https://github.com/d1dac0/biscotti-win/issues/82) |
| Summaries, titles, action items | `Intelligence/MeetingAnalyzer.swift`, `IntelligencePrompts.swift`; IntelligenceTests, SummaryTabTests | `Ai/OllamaService`, `MeetingService`, SummaryParser; `ChunkedSummaryTests`; [#79](https://github.com/d1dac0/biscotti-win/issues/79) displays `ActionItems` | implemented | Local summaries preserve later commitments through chunking. | Keep map-reduce and explicit unavailable-summary state; expose existing stored actions. | — |
| Participant names / meeting context | `IntelligencePrompts.meetingDetailsBlock`, `VoiceprintEvidence`; CalendarCardTests, SpeakerMappingTests | `CalendarAttendees`, `SummaryParser`, `SpeakerIntroductionResolver`; `CalendarSummaryContextTests` | partial | Attendee roster and introductions work; persistent identity and rich context do not. | Never equate an invitation with attendance; add richer event snapshots and correction separately. | [#82](https://github.com/d1dac0/biscotti-win/issues/82), [#85](https://github.com/d1dac0/biscotti-win/issues/85) |
| Summary prompts / regeneration / streaming | `SummaryPromptUI`, `MeetingDetailViewModel.regenerate`, `MeetingAnalyzer.onPartialSummary`; RegenerateWiringTests | `OllamaService` has fixed prompts and `stream=false`; `MeetingWindow` edits text only | partial | No UI prompt control, retry generation or streamed summary. | Expose local preferences and explicit regenerate behavior without losing edits. | [#80](https://github.com/d1dac0/biscotti-win/issues/80), [#84](https://github.com/d1dac0/biscotti-win/issues/84) |
| Calendar source, selection, refresh | `Calendar/CalendarService.swift` EventKit observation; CalendarServiceTests; SettingsCalendarSection | `Calendar/IcsCalendarReader`, `MainWindow.RefreshCalendarMeetingsAsync`; `IcsCalendarReaderTests` | partial | Local .ics events and recording context exist; exports can become stale and calendar selection is limited. | Define Windows provider/filtering while retaining offline .ics support. | [#85](https://github.com/d1dac0/biscotti-win/issues/85) |
| Calendar-aware / detected-meeting prompts | `Notifications/NotificationKind.swift` meetingStarting/adHocDetected; CalendarDeepLinkTests, NotificationsTests | Main window lists upcoming events with Record; `MainWindow.Shell` only emits a summary-ready notification | partial | Upcoming rows are not proactive start offers. | Deduplicated opt-in offers, expiry cleanup and event/tray navigation. | [#85](https://github.com/d1dac0/biscotti-win/issues/85) |
| Auto-stop / keep-recording control | `MeetingDetector` mic-activity debounce; `AppCore` countdown and preference; DebounceAndLifecycleTests | `LoopbackSilenceMonitor`, `MeetingService.CheckForLoopbackSilence`; `LoopbackSilenceMonitorTests` | partial | Five minutes of armed output silence differs from call-end detection; no countdown/cancel/preference. | Retain in-person safeguard; design activity signal and user control before changing semantics. | [#86](https://github.com/d1dac0/biscotti-win/issues/86) |
| Tray / menu bar / shortcuts | `MenuBarUI/MenuBar{Content,Label}View.swift`, `App/Sources/GlobalHotKey.swift`; SettingsViewModelTests | `MainWindow.Shell`, `HotkeyChord`; `AppSettingsTests`; app/tray logo in [#53](https://github.com/d1dac0/biscotti-win/issues/53) / PR [#55](https://github.com/d1dac0/biscotti-win/issues/55) | partial | Windows has start/stop, open-last, Settings and Exit, but fewer upcoming/recent previews. | Keep Windows tray/shortcut conventions; extend menus separately; reuse [#53](https://github.com/d1dac0/biscotti-win/issues/53) artwork ownership. | [#85](https://github.com/d1dac0/biscotti-win/issues/85); [#53](https://github.com/d1dac0/biscotti-win/issues/53) |
| Notifications / preferences / cleanup | `Notifications/NotificationService.swift`, NotificationCleanupTests, CountdownCancelTests | `App.RegisterNotifications`, `MainWindow.Shell.ShowSummaryNotification`; no notification preference UI | partial | Summary readiness is available; stale start/countdown offers and preference controls are not. | Native Windows notifications, opt-in offers and expiry tests. | [#85](https://github.com/d1dac0/biscotti-win/issues/85), [#86](https://github.com/d1dac0/biscotti-win/issues/86) |
| Custom vocabulary | `Vocabulary/VocabularyService.swift` combines settings/calendar inputs; VocabularyFormatterTests | `AppSettings.LocalVocabulary`, SettingsWindow, Whisper initial prompt; `AppSettingsTests`, `TranscriptionFixtureTests` | partial | Manual terms work; calendar-derived vocabulary and richer list controls differ. | Retain comma/newline terms; add consented calendar context and settings controls. | [#80](https://github.com/d1dac0/biscotti-win/issues/80), [#85](https://github.com/d1dac0/biscotti-win/issues/85) |
| Timestamped Markdown notes | `Recording/NotesMarkdown.swift`, `MarkdownEditorUI/MarkdownEditor.swift`; NotesMarkdownTests | `MeetingService.AddTimestampedNote`, `MeetingStore.RenderMarkdown`, Notes tab; `CrashSafetyAndAudioTests` notes persistence/Markdown cases | partial | Notes persist with times; Windows displays raw Markdown and timestamps are not navigable links. | Safe Markdown editing/rendering, save feedback and timestamp navigation. | [#87](https://github.com/d1dac0/biscotti-win/issues/87), [#88](https://github.com/d1dac0/biscotti-win/issues/88) |
| Audio review / transport / deep links | `DesignSystem/AudioTransport.swift`, `MeetingDetailViewModel.seekAndPlay`; AudioTransportTests, MeetingOpenIntentTests; App/deeplinks.md | `MeetingWindow.BeginPlayback`, `EncryptedAudioPlayback`; `EncryptedAudioFileTests`; only channel play/stop | partial | Users cannot scrub or jump from text/notes to audio. | Keep protected per-channel playback; add persistent seekable transport and local deep-link handling. | [#88](https://github.com/d1dac0/biscotti-win/issues/88) |
| History / search / tags | `MeetingListUI`, `DataStore/SearchIndex.swift`, Tag model; MeetingsSearchTests, SearchIndexTests, TagTests | `MeetingStore.LoadAll`, MainWindow list; `Mcp/MeetingQueryService.SearchMeetings`; no UI search/tag fields | partial | History and MCP search exist; visual search/filtering/organization are absent. | Reuse local query boundary and protect any derived index. | [#81](https://github.com/d1dac0/biscotti-win/issues/81) |
| CSV import/export / copy / sharing | `ImportExport/MeetingCSV{Importer,Exporter}.swift`, `MeetingDetailViewModel.copy*`; ImportExportTests, CopyTranscriptTests; App/ImportingExporting.md | `MeetingStore.RenderMarkdown` writes protected internal copy; Main/Meeting windows have no explicit export/import/copy commands | missing | Internal protected files are not a portable export workflow. | User-selected local export/import with plaintext disclosure; no remote sharing. | [#83](https://github.com/d1dac0/biscotti-win/issues/83) |
| MCP meeting queries and transcript paging | `MCPServer/MeetingToolCatalog.swift`, MCPToolRoundTripTests; App/ConnectingMCP.md | `Biscotti.Mcp/MeetingTools.cs`, `MeetingQueryService`; `docs/mcp-server.md` reproducible stdio client workflow | implemented | Both expose local read-only meeting queries; query richness differs with UI search. | Windows stdio process rather than loopback HTTP; extend ranking/date/tag queries with search work. | [#81](https://github.com/d1dac0/biscotti-win/issues/81) (richer queries) |
| Privacy / offline / retention | `DataStore` disables CloudKit by default, `RecordingController` local storage, local Transcription/LocalLLM pipelines; DataStoreTests; README offline-after-model-download workflow | `ProtectedMeetingText`, `EncryptedAudioFile`, loopback Ollama endpoint, model download stores; `EncryptedAudioFileTests`, `CrashSafetyAndAudioTests` | implemented | Meeting processing stays local after dependencies are installed. Both retain meetings until deletion; no automatic retention UI was found. | Windows additionally uses DPAPI/AES protection; MCP client privacy is the client's responsibility. No telemetry/cloud addition. | — |
| Delete / storage failures / crash recovery | `RecordingController.recoverOrphans`, `DataStore.delete`, MeetingDetailView delete confirmation; RecordingControllerTests | `MeetingStore` protected atomic writes, WAV recovery, MeetingWindow confirmation; `CrashSafetyAndAudioTests`, `EncryptedAudioFileTests` | partial | Windows protects writes and reports failures; no persisted recoverable job/reprocess UI. | Retain fail-closed storage behavior; make saved interrupted meetings actionable. | [#84](https://github.com/d1dac0/biscotti-win/issues/84) |
| Model / permission / device errors | `OnboardingStepViews` denial/retry; `AudioRecorder` route/start retries; RouteChangeTests, OnboardingSystemAudioTests | `ModelStoreTests`, `AudioDeviceTests`, GPU fallback tests; UI download repair, device warning and explicit reselection | partial | Windows repair and warnings exist; unified readiness and processing repair remain fragmented. | Model guidance in [#80](https://github.com/d1dac0/biscotti-win/issues/80). Keep [#19](https://github.com/d1dac0/biscotti-win/issues/19)'s deliberate no-silent-device-switch policy; Windows hardware equivalence remains unmeasured. | [#80](https://github.com/d1dac0/biscotti-win/issues/80), [#84](https://github.com/d1dac0/biscotti-win/issues/84) |
| Empty / loading / active / completed / failure states | `HomeView`, `RecordingView`, `MeetingDetailView` loading/failure/display-state switches; PipelineStatusTests, TranscriptContentTests | [#79](https://github.com/d1dac0/biscotti-win/issues/79) MainWindow history/setup/processing/error copy and MeetingWindow empty/live/summary states; `Biscotti.Win.Tests/MeetingFlowTests` | implemented | Primary Windows states are now explicit and actionable; advanced retry still deferred. | Shared native WPF hierarchy; synthetic preview reproduces each state without profile access. | [#84](https://github.com/d1dac0/biscotti-win/issues/84) (retry actions), [#89](https://github.com/d1dac0/biscotti-win/issues/89) (further visual validation) |
| Appearance / navigation / keyboard / accessibility | `DesignSystem/Color+Theme`, Tokens, HomeView, grouped SettingsView, native list selection; DynamicColorTests, SettingsLayoutTests | [#79](https://github.com/d1dac0/biscotti-win/issues/79) `Theme.xaml`, `ThemePalette`, main/review/settings/device windows, Enter/open action and UI tests | partial | Warm surfaces/sage, display/body hierarchy and labels align; full sidebar, Mac fonts, dark theme and rich editors remain different. | Native Windows chrome/focus/controls; system brushes in high contrast; defer shell/dark polish and cross-device visual validation. | [#89](https://github.com/d1dac0/biscotti-win/issues/89) |

## Priorities and follow-up boundaries

All follow-ups were checked against all existing open/closed issue titles before
creation. They link to #79 and each contains evidence, acceptance criteria,
priority and dependencies. P2 is functional workflow work; P3 is deferred
appearance/cross-device validation. No new P0/P1 defect was established by this
source review. Existing completed stories remain implementation history rather
than being reopened with wider criteria.

- [#80 — Guide first-run model setup and expose local AI settings](https://github.com/d1dac0/biscotti-win/issues/80)
- [#81 — Search and organize meeting history in the Windows UI](https://github.com/d1dac0/biscotti-win/issues/81)
- [#82 — Recognize and correct speakers across meetings locally](https://github.com/d1dac0/biscotti-win/issues/82)
- [#83 — Add explicit local meeting import, export and copy workflows](https://github.com/d1dac0/biscotti-win/issues/83)
- [#84 — Retry saved-meeting processing and recover interrupted jobs](https://github.com/d1dac0/biscotti-win/issues/84)
- [#85 — Bring calendar context, prompts and tray previews into Windows workflows](https://github.com/d1dac0/biscotti-win/issues/85)
- [#86 — Add meeting activity detection and controllable auto-stop](https://github.com/d1dac0/biscotti-win/issues/86)
- [#87 — Render Markdown notes and link timestamps to meeting navigation](https://github.com/d1dac0/biscotti-win/issues/87)
- [#88 — Add a persistent audio transport and timestamp seeking](https://github.com/d1dac0/biscotti-win/issues/88)
- [#89 — Validate Mac visual comparisons and complete Windows appearance parity](https://github.com/d1dac0/biscotti-win/issues/89)
- [#90 — Evaluate and provide microphone echo control on Windows](https://github.com/d1dac0/biscotti-win/issues/90)

Recommend #80 next: users should be able to get both transcription and summary
models ready before adding more review features. #84 makes interrupted or
failed processing actionable. #82 depends on explicit privacy/identity design,
#87 audio jumps depend on #88, and #85/#86 coordinate notification cleanup.
Do not start any of them as part of #79.

## Visual and interaction findings

See the [annotated comparison gallery](parity-evidence/comparison.html) and
[screen evidence index](parity-evidence/README.md). The gallery pairs Mac
**source-derived diagrams** with actual Windows synthetic-preview captures.
They compare the same Atlas fixture and flow, with source links and remaining
mismatches. These are annotated comparisons, not fabricated Mac screenshots.
The diagrams express hierarchy/interaction only; Mac pixel geometry, motion,
voiceover and hardware behavior remain unverified and are tracked in #89.

| Screen / state | Windows baseline mismatch | Scoped change in [#79](https://github.com/d1dac0/biscotti-win/issues/79) | Remaining difference |
| --- | --- | --- | --- |
| Setup / empty | Loose model controls; no meeting-history empty state | Inline setup title, model progress, privacy explanation and first-meeting guidance | Guided permissions, disk/Ollama checks and model settings: [#80](https://github.com/d1dac0/biscotti-win/issues/80) |
| Home / list | Toolbar-like control stack, no explicit history heading or keyboard open instruction | Centered scrollable column, recording card, upcoming card, meeting cards, Settings, Enter/open action | Search, tags, date groups/sidebar: [#81](https://github.com/d1dac0/biscotti-win/issues/81) / [#89](https://github.com/d1dac0/biscotti-win/issues/89) |
| Recording / processing | Start/Stop colors inverted relative to intent; progress mostly in one status line | Sage start, red stop/recording, active heading/duration and processing explanation; live notes preserved | Single recording workspace, countdown/user control: [#86](https://github.com/d1dac0/biscotti-win/issues/86) / [#89](https://github.com/d1dac0/biscotti-win/issues/89) |
| Transcript / summary | Flat rows, default typography, stored actions not exposed | Readable speaker/time cards, consistent metadata, explicit empty/live states, completed summary and action items | Markdown presentation, find/speaker editing, version/retry UI: [#81](https://github.com/d1dac0/biscotti-win/issues/81)/[#82](https://github.com/d1dac0/biscotti-win/issues/82)/[#84](https://github.com/d1dac0/biscotti-win/issues/84)/[#87](https://github.com/d1dac0/biscotti-win/issues/87) |
| Notes | Unlabelled editor and large empty timestamp list space | Explicit Notes label, bounded timestamp list and labelled Markdown notes field | Rendered Markdown, autosave and clickable timestamps: [#87](https://github.com/d1dac0/biscotti-win/issues/87)/[#88](https://github.com/d1dac0/biscotti-win/issues/88) |
| Settings / devices | Inconsistent styles; Settings only in tray | Main-window Settings entry; shared headings, surfaces and error colors; scrolling settings dialog | Rich model, permissions, notifications and calendar preferences: [#80](https://github.com/d1dac0/biscotti-win/issues/80)/[#85](https://github.com/d1dac0/biscotti-win/issues/85) |
| Tray / notifications | Generic icons, limited menu versus Mac upcoming/recent content | Preserve native Windows menu and state behavior; synthetic tray walkthrough | Artwork: [#53](https://github.com/d1dac0/biscotti-win/issues/53); upcoming/recent menus and preference/expiry behavior: [#85](https://github.com/d1dac0/biscotti-win/issues/85)/[#86](https://github.com/d1dac0/biscotti-win/issues/86) |

Native Windows title bars, default control templates, focus indicators,
access keys, local-time metadata and close-to-tray behavior remain. Main and
review windows have minimum dimensions; the home and Settings content scrolls.
High-contrast resources follow Windows system brushes dynamically. OS
high-contrast settings were not changed during verification; the theme logic
and contrast ratios were checked without modifying user settings.
Full dark mode and 200% scaling/screen-reader coverage remain in #89.

## Synthetic fixture and reproduction

`Biscotti.Win/DesignPreview.cs` defines Project Atlas planning, Alex/Jordan,
three transcript turns, two action items and a timestamped checklist note.
There are no real names, calendar imports, meeting recordings or profile paths
in the evidence. The opt-in preview bypasses normal startup, the single-instance
activation channel, profile settings, model checks/downloads and meeting storage.
Record/stop/note interactions operate on memory only; save/delete cannot persist
or remove meetings. The tray preview does not register the global shortcut.

From the worktree/repository root:

```powershell
dotnet run --project Biscotti.Win -- --design-preview=history
```

Other reproducible states: `setup`, `empty`, `recording`, `processing`,
`failure`, `review`, `tray`. Use `--design-preview=STATE`.
Open Settings from home; select a meeting and press Enter; switch
Transcript/Summary/Notes/Audio; in recording preview add a note, then Stop &
save; in tray preview inspect the context menu and exit. Close preview windows
when finished.

## Verification

- Clean baseline: build succeeded; check **98 passed, 0 failed, 0 skipped**.
- New native WPF test exercises setup gating, empty/history/processing states,
  disabled live Save/audio, processing text, and live-to-completed binding/
  action-list refresh. It runs on STA with synthetic in-memory data and needs
  neither a GPU, audio hardware nor models.
- `tools/check.ps1` and CI now test the solution, including the new WPF test
  project, rather than silently excluding UI checks.
- Final build/check and walkthrough results are recorded in the evidence index.
- Actual Mac execution, hardware call/device testing, screen-reader pass and
  200% scaling were not performed. They are explicit follow-ups, not passing
  checks. This review does not claim measured cross-platform audio accuracy.
