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
- #85 Calendar context, prompts, preferences and tray previews (P2).
- #86 Activity-aware auto-stop with countdown and user control (P2).
- #87 Rendered Markdown and timestamp navigation (P2; audio jumps need #88).
- #88 Persistent audio transport and seeking (P2).
- #89 Mac runtime visual validation and Windows dark/appearance polish (P3).
- #90 Microphone echo-control evaluation (P2).

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

Load-bearing decisions (a stack, a data model, a library choice, an
architecture) are written down in `docs/specs/`, one file per decision: what
was decided, the alternatives, and why. Write the spec **before** building on
the decision; if one is genuinely overturned, write a new spec and mark the old
one `Superseded`; if you must build before deciding, mark it `Assumed` and
revisit before the feature is done. See `docs/specs/README.md`.
