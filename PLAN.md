# Plan

The plan of record. Every story and bug is a GitHub Issue; this file tracks
which phase each belongs to, not its status — status lives in the issue
labels. Agents pick open `type:story` issues from the current phase in order
unless a `type:bug` issue is open first.

## Phase 1: Local meeting capture works end to end — delivered

**Goal:** record dual-channel audio, transcribe on-device, store it safely,
summarize locally.

- [x] #2 Anti-alias the system-audio downmix (PR #22)
- [x] #3 CPU whisper runtime fallback when CUDA is missing (PR #22)
- [x] #12 Crash-safe WAV + meeting.json saves (PR #22)
- [x] #13 Mic 16 kHz capture fallback for rejecting mics (PR #22)
- [x] #21 Live recording health readout: elapsed, bytes, silence (PR #22)
- [x] #20 GPU transcription as an opt-in tier with lazy runtime download (PR #26)

## Phase 2: Trustworthy long meetings

- #105 Restore missing microphones before recording: preserve connected choices,
  resolve the Windows default, and cover reconnects with regression tests
  ([decision](docs/specs/default-microphone-recovery.md)).

**Goal:** long meetings stay complete, private, and robust: long audio,
long transcripts, at-rest privacy, hardware changes mid-meeting.

- [ ] #10 Scripted test-meeting fixture for transcript determinism checks
- [ ] #17 Encrypt meeting data at rest with DPAPI (decision: [DPAPI meeting text files](docs/specs/dpapi-meeting-data-at-rest.md))
- [x] #16 Map-reduce chunked summaries so long meetings are not truncated (PR #37)
- [x] #15 Live incremental transcription while the meeting is running (PR #41; design: [live transcription](docs/specs/live-incremental-transcription.md))
- [ ] #14 Per-app loopback capture (only the meeting app lands in the transcript; design: [per-app loopback](docs/specs/per-app-loopback-capture.md))
- [x] #48 Play encrypted microphone and system audio in the meeting window (PR #52)
  - Plan: expose the existing authenticated decrypting stream to playback; add independent microphone/system play-stop controls; surface missing, corrupt, or unavailable-key errors; cover stream and failure handling without audio hardware.
- [x] #19 Device picker UI and mid-meeting device-change handling (PR #49; decision: [audio device selection](docs/specs/audio-device-selection.md))
- [ ] #54 Show recording duration and local start time

## Phase 3: Capture the meeting context around the transcript

**Goal:** reduce friction around meetings and make output immediately useful.

- [ ] #18 Tray icon, global hotkey and toast notifications (design: [tray, hotkey, and notifications](docs/specs/tray-hotkey-notifications.md))
- [ ] #53 Use the Biscotti logo for window and tray icons (PR #55)
  - Finish: merge current main while preserving the current layout; validate
    embedded icon sizes, every window, executable, and stable tray identity
    with state tooltips; run build/check and inspect synthetic previews.
- [ ] #4 Auto-stop recording when the call ends ([spec](docs/specs/recording-auto-stop.md))
- [ ] #5 Calendar-aware meeting prompts (spec: [local iCalendar file source](docs/specs/calendar-awareness-local-ics.md))
- [ ] #6 Custom vocabulary (names, products, jargon) passed to Whisper
- [ ] #7 Timestamp-linked markdown notes in the meeting window
- [ ] #8 MCP server: chat with your meeting notes from Claude Desktop / LM Studio
- [x] #9 Per-participant diarization and real speaker names for loopback (PR #65; decision: [speaker diarization](docs/specs/speaker-diarization.md))

## User-directed work: Mac feature parity and Windows experience (#79)

**Status:** Approved for autonomous execution on 2026-10-20; implementation and local
verification complete (99 tests and synthetic walkthrough); PR delivery in progress. Branch:
`issue-79-mac-parity`. Initial review: 2026-10-20. See the
[review](docs/mac-feature-parity-review.md) and
[proposed UI decision](docs/specs/mac-parity-ui-alignment.md).

1. Finish the source-backed capability inventory against pinned Mac/Windows
   revisions, including tests, release materials and open issues. Record
   implemented/partial/missing/platform-specific separately; create linked,
   non-duplicate follow-ups for substantial gaps, with priorities, acceptance
   criteria and dependencies.
2. After approval, align existing WPF screens: shared warm surfaces and sage
   accents, readable heading/body hierarchy, grouped setup/recording/calendar/
   history sections, discoverable Settings, explicit empty/progress/error
   states, consistent meeting-review tabs and settings/device dialogs. Keep
   capture, local inference and encrypted storage behavior intact. Logo and
   tray artwork remain owned by #53 / PR #55.
3. Complete annotated screen comparisons with one synthetic meeting fixture,
   run build/check and the Windows walkthrough, then deliver one PR for #79.
   Actual Mac screenshots need a macOS capture source; source-derived diagrams
   must be labelled as such and cannot be claimed as runtime screenshots.

The user approved the plan and instructed the session to continue without
waiting. Baseline build/check passed with 98 tests. #79 adds a native WPF flow
test and synthetic preview; see the review/evidence index for final validation.

### Parity follow-ups (separate issues; do not implement in #79)

- #80 Guided first-run models and local AI settings (P2; recommended next).
- #81 UI history search, organization and richer queries (P2).
- #82 Local cross-meeting speaker identity and correction (P2).
- #83 Explicit local import/export/copy workflows (P2).
- #84 Retry processing and interrupted-job recovery (P2).
- #85 Calendar context, prompts, preferences and tray previews (P2; decision: [calendar context and tray offers](docs/specs/calendar-context-and-tray.md)).
- #86 Activity-aware auto-stop with countdown and user control (P2).
- #87 Rendered Markdown and timestamp navigation (P2; audio jumps need #88).
- #88 Persistent audio transport and seeking (P2; decision: [persistent audio transport](docs/specs/persistent-audio-transport.md)).
- #89 Mac runtime visual validation and Windows dark/appearance polish (P3).
- #90 Microphone echo control (P2; decision: [Windows microphone echo control](docs/specs/windows-microphone-echo-control.md); evidence: [echo-control verification](docs/echo-control-verification.md)).

## Active work: guided local model setup and AI settings (#80)

**Status:** Implemented and verified locally; PR delivery in progress.
Decision: [guided local model setup](docs/specs/local-model-setup.md).
Evidence: [model setup verification](docs/model-setup-verification.md).
On 2026-10-20 the user removed the
separate plan-approval requirement and directed work to proceed within the
existing authorization.
Claimed by d1dac0 on 2026-10-20 in branch `issue-80-model-setup`.
Baseline at `e45376b`: `tools/build.ps1` and `tools/check.ps1` passed;
98 core tests and 1 Windows flow test passed, none skipped. The initial
build reports existing NAudio obsolete-API and test read warnings.

1. Record the setup/settings decision in `docs/specs/` before implementing.
   Add testable readiness checks that distinguish missing Whisper files,
   incomplete downloads, an unavailable local Ollama server, and an absent
   selected summary model. Show model sizes and available local disk space;
   check space before downloads. Keep Ollama inference on the existing local
   endpoint and keep model downloads explicit.
2. Extend the existing setup card and Settings window using the current WPF
   styles. Expose the two supported Whisper models, CPU/GPU preference with
   existing fallback, vocabulary, and a list of locally installed Ollama
   models with sizes. Show readiness, download progress, cancel/retry, and
   clear offline-after-download guidance. Provide actionable local Ollama
   installation/model-download instructions when it is unavailable. Explain
   Windows microphone permission failures and let the user open permission
   settings without changing permissions automatically. Keep capture available
   when only summaries are unavailable.
3. Add validated summary preferences (brief/standard/detailed and optional
   custom guidance, bounded to 4,000 characters) to local settings. Apply
   them to single-pass and chunked summaries while retaining the section
   contract used to parse summaries, titles, actions, and participants. Save
   changes as a validated unit; Cancel discards edits. Apply model/runtime
   changes to the next meeting, avoiding changes during active processing.
4. Add hardware-free regressions for missing server/model, insufficient disk,
   corrupt/incomplete download, cancellation/retry, CPU fallback, settings
   validation, and summary guidance in single/chunk/merge passes. Run both
   required scripts and inspect the native setup/settings flow with synthetic
   data. Launch with `dotnet run --project Biscotti.Win` for the human
   walkthrough and report the actual generated executable path. Deliver one
   labelled PR with `Fixes #80`, then merge only after green CI.

Final local verification: build/check passed with 113 core tests and the
expanded Windows flow test (114 total, none skipped). The native synthetic
setup/settings walkthrough passed. Additional independent review, extended
performance checks and real multi-GB downloads were not run.

## On-request audits

Standing `type:audit` issues wait in the tracker for the human's explicit
go-ahead — agents never take them as "the next story" in phase order. Audit
issues complete the audit only; remediation findings join the normal loop.

- **#11 macOS-to-Windows portability review: blind spots, pre-mortem, hard
  questions.** Run whenever the product has grown enough to be worth a second
  pair of eyes, out of phase order.

---

Rules:

- One issue per PR. Don't bundle unrelated fixes into a story PR.
- New scope discovered mid-phase: file it as a new issue under the current
  phase, or a new phase if the changed direction is big.

## Decisions

- #87 Markdown notes: [local rendering, explicit-save drafts, and timestamp navigation](docs/specs/markdown-notes.md).

Load-bearing decisions (a stack, a data model, a library choice, an
architecture) are written down in `docs/specs/`, one file per decision: what
was decided, the alternatives, and why. Write the spec **before** building on
the decision; if one is genuinely overturned, write a new spec and mark the old
one `Superseded`; if you must build before deciding, mark it `Assumed` and
revisit before the feature is done. See `docs/specs/README.md`.

## Active work: meeting history search (#81)

1. Share literal substring matching with MCP; add ranked in-memory snippets.
2. Add accessible search, clear/open controls and local-month history groups.
3. Cover search, empty/protected failure and WPF flows; run build/check and
   a synthetic Windows walkthrough before PR delivery.

Decision: [meeting history search](docs/specs/meeting-history-search.md).
Baseline: build/check passed (98 Core/MCP tests + 1 WPF test); existing NAudio
obsolete and inexact test-read warnings remain outside this issue.

#81 verification complete: 108 tests passed and native synthetic keyboard
walkthrough passed. See [verification](docs/meeting-history-search-verification.md).
Integration verification after #80: build/check passed, 123 tests.

## Active work: local speaker memory (#82)

1. Record the [speaker memory decision](docs/specs/local-speaker-memory.md).
2. Add stable group keys, encrypted opt-in person storage, conservative suggestions,
   explicit corrections and forget controls in the meeting Speakers tab.
3. Verify synthetic false-match/ambiguity cases, storage and correction regressions,
   build/check, and the native Windows workflow before one labelled PR.

Baseline at b43780c: build/check passed, 123 tests; existing obsolete NAudio
and inexact test-read warnings. Additional independent review and real-speaker
accuracy evaluation are outside this change; synthetic evidence is not calibration.

#82 local verification complete: build/check passed, 133 tests; native synthetic
speaker-correction walkthrough passed. See [verification](docs/speaker-memory-verification.md).

## Active work: explicit local meeting transfer (#83)

**Status:** Implemented and locally verified; PR delivery in progress.
Decision: [local meeting transfer](docs/specs/local-meeting-transfer.md).
How to use the [CSV and Markdown workflows](docs/local-meeting-transfer.md).

CSV v1 import preview reports valid, duplicate and invalid rows before writing;
valid meetings stage as protected text and never overwrite existing IDs. History
exports all saved meeting text, and the meeting review copies or exports current
Markdown after plaintext disclosures. Synthetic regression coverage includes
timestamps, multiline quoting, duplicates, bad rows, storage protection and
cancellation.

Verification after merging `origin/main` at `4236417`: `tools/build.ps1` and
`tools/check.ps1` passed, with 144 Core tests and 1 Windows flow test, none
skipped. The synthetic import-preview and meeting-review screens were inspected.
Existing obsolete NAudio API and inexact test-read compiler warnings remain.

## Saved meeting recovery (#84)

**Status:** Implemented and locally verified; PR delivery in progress.
Decision: [recovery and replacement policy](docs/specs/saved-meeting-recovery.md).
See [verification and synthetic Windows preview](docs/saved-meeting-recovery-verification.md).

1. Record the [recovery and replacement decision](docs/specs/saved-meeting-recovery.md).
2. Persist capture/transcription/summary status, surface interrupted meetings, and
   add explicit recovery, transcription retry and summary regeneration actions.
3. Preserve previous transcript and summary content on errors; test encrypted
   truncated audio, missing resources, storage errors and WPF flow with synthetic data.
4. Deliver one labelled PR after build/check and native flow verification.

Baseline at d3582af: `tools/build.ps1` and `tools/check.ps1` passed, with 144
Core/MCP tests and 1 Windows flow test, none skipped. Existing obsolete NAudio
API and inexact test-read warnings remain.

Local verification: `tools/build.ps1` and `tools/check.ps1` passed with 158
Core/MCP tests and 1 Windows flow test, none skipped. The synthetic recovery
preview passed in the native Windows app.

## Controllable auto-stop (#86)

Decision and plan: [controllable recording auto-stop](docs/specs/controllable-recording-auto-stop.md).
Baseline at dfde794: build/check passed (158 Core tests, 1 WPF flow test).
Existing NAudio obsolete API and inexact test-read warnings remain.

Implemented saved enable/disable controls, a 30-second countdown after five
minutes of armed output silence, and a session-only Keep recording override.
The [decision](docs/specs/controllable-recording-auto-stop.md) documents the
Windows activity investigation and why process/session state is not call state.
After integrating `origin/main` at ac8fda6, build/check passed: 170 Core/MCP
tests and 1 WPF flow test, none skipped. Native synthetic countdown cancellation
and Settings were inspected; live hardware/cross-app measurements were skipped.

## Active work: persistent audio transport (#88)

**Status:** Implemented and locally verified; PR delivery in progress.
Decision: [persistent audio transport](docs/specs/persistent-audio-transport.md).

One transport stays below the meeting tabs for microphone/system selection,
play/pause, seeking, and duration/position. Timestamp links seek the selected
channel and start playback. The existing authenticated reader remains bounded
and no plaintext audio is written to disk.

Verification: build/check passed with 170 Core/MCP tests and 1 WPF flow test,
none skipped. Synthetic UI review confirmed the transport stays visible on the
transcript and notes tabs. Fake-output coverage exercises seek bounds, channel
switching, stop-before-delete, and disposal; encrypted frame tampering remains
covered by the encrypted playback checks.
