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
- [ ] #15 Live incremental transcription while the meeting is running (design: [live transcription](docs/specs/live-incremental-transcription.md))
- [ ] #14 Per-app loopback capture (only the meeting app lands in the transcript)
- [x] #19 Device picker UI and mid-meeting device-change handling (decision: [audio device selection](docs/specs/audio-device-selection.md))

## Phase 3: Capture the meeting context around the transcript

**Goal:** reduce friction around meetings and make output immediately useful.

- [ ] #18 Tray icon, global hotkey and toast notifications
- [ ] #4 Auto-stop recording when the call ends
- [ ] #5 Calendar-aware meeting prompts
- [ ] #6 Custom vocabulary (names, products, jargon) passed to Whisper
- [ ] #7 Timestamp-linked markdown notes in the meeting window
- [ ] #8 MCP server: chat with your meeting notes from Claude Desktop / LM Studio
- [ ] #9 Per-participant diarization and real speaker names for loopback

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
