# Windows portability review — 2026-10-20

**Scope:** Revisit the open findings in issue [#11](https://github.com/d1dac0/biscotti-win/issues/11) against `origin/main` at `79ca874c85eec06c8b1fa74d32b701f2e981dd10`. This is an audit only; remediation is tracked separately.

**Verification:** Clean baseline `tools/build.ps1` passed; `tools/check.ps1` passed (89 tests). The baseline build emitted existing NAudio obsolescence and test analyzer warnings, with zero errors.

## Executive readout

The Windows app now covers several issues that were unresolved in the original
audit: CPU transcription fallback, crash-safe audio containers, selectable
process loopback on supported Windows 11, device-change warnings, per-meeting
speaker embeddings, local ICS calendar input, tray/hotkey controls, and
single-instance activation. The former “CPU is slower than real time” concern
is contradicted by the measured CPU row: the matrix defines RTF as audio seconds
per transcription second and reports 1.38×, which is faster than real time.

Four release risks remain prominent:

1. **A selected app-only capture can widen to all output audio.** If process
   loopback fails to start, the recorder starts device-wide loopback. It shows
   a status warning, but the user is not asked before unrelated applications
   become part of the recording. On Windows 10, device-wide capture is the
   supported path and is also shown in the recording status.
2. **Transcript text is still plaintext at rest.** `meeting.json` and
   `meeting.md` contain transcript, summary, notes, and attendee metadata.
   The accepted DPAPI document is a design only; `MeetingStore` still reads and
   writes these files as plaintext.
3. **Cross-channel clock drift is unmeasured.** The microphone and loopback
   channels have independent capture clocks. Sample-rate conversion preserves
   each channel's requested ratio, but the app has no cross-channel drift
   estimator or long-duration bound.
4. **Distribution and rights are not settled.** CI builds source but no
   installer/signing workflow exists. The upstream macOS repository declares
   PolyForm Perimeter 1.0.1; obtain a rights determination before distributing
   a product that is explicitly described as its Windows port.

The other meaningful evidence gap is diarization quality: the 0.88 embedding
threshold was sanity-checked on ten short public clips, not on crosstalk or
noisy real meetings. The current implementation does not separate overlapping
voices.

## Prior findings, rechecked

| Original finding | Current state | Remaining limitation |
| --- | --- | --- |
| CUDA-only Whisper runtime could fail on non-NVIDIA machines (#3, #20) | Fixed: CPU runtime works by default; CUDA is an opt-in tier with fallback. | Performance rows are measured for CPU and RTX 3080 only. iGPU and RTX 3060 rows remain pending. |
| Raw downsampling aliased speech (#2) | Fixed: `AntiAliasDecimator` filters input frames and keeps exact sample-rate conversion carry across packets. | This does not calibrate the two devices' independent clocks. |
| Recording crash damaged audio/metadata (#12, #22, #45) | Atomic meeting metadata saves and authenticated AES-GCM audio frames are implemented. Audio keys are protected with DPAPI CurrentUser; incomplete final frames are recoverable. | The repository tests cover atomic writes and frame recovery, but a hard-kill end-to-end WPF recovery run is not recorded here. |
| All system output was captured (#14) | Windows 11 build 22000+ can use process-tree loopback for a selected app; Windows 10 reports that it captures all speaker audio. | Process-loopback startup failure also falls back to all speaker audio without a confirmation step. The #14 implementation report says a live two-app isolation test was not run. |
| Microphones rejecting 16 kHz capture (#13) | Fixed: microphone capture has a 48 kHz path and decimation fallback. | Physical Bluetooth/USB coverage is not represented by hardware-free CI tests. |
| No speaker embeddings or names (#9) | Fixed for per-meeting clusters: WeSpeaker embeddings, cosine clustering, and conservative name resolution are present. | No cross-meeting voiceprints; short lines retain fallback labels; no overlapping-voice separation; evaluation set is too small for a broad accuracy claim. |
| Calendar requires a cloud account (#5) | Avoided: calendar context uses a user-selected local ICS file. | No Outlook/Graph OAuth flow exists; do not add one silently under the local-only product promise. |
| Default device changes silently stop/corrupt a channel (#19) | Improved: one-second device snapshot checks surface persistent warnings and keep any healthy channel; devices do not hot-swap during a meeting. | Physical unplug/default-device-change manual checks were not performed per the accepted spec's verification record. |
| Tray and hotkey launches could create duplicate processes (#18, #61) | Implemented: tray controls, configurable RegisterHotKey, user-scoped mutex, CurrentUserOnly activation pipe, and awaited shutdown. | Keep the duplicate-launch/exit regression check in release smoke tests. |
| Graph calendar tokens conflict with “no cloud” (#11) | Resolved by local ICS input; no Graph token or remote calendar access is used. | State this clearly in any calendar marketing and settings UI. |

## Current findings and follow-up work

### P1 — App-only capture is not fail-closed

`DualChannelRecorder.StartLoopback` tries the selected process tree and then
starts device loopback if the process recorder throws or fails. The visible
status says that all speaker audio is now captured and asks the user to mute
other sounds. That is better than silent fallback, but it changes capture
scope after the user selected one app. Windows 10 also necessarily uses the
system mix. Require explicit consent before starting or retrying with a wider
capture scope.

Follow-up: [#67](https://github.com/d1dac0/biscotti-win/issues/67).

### P1 — Meeting text remains plaintext; README audio claim is stale

`MeetingStore.Save` uses `File.WriteAllText` for JSON and Markdown, and
`ReadMeetingMetadata` uses `File.ReadAllText`. No text-protection wrapper is
present in `Biscotti.Core`. The accepted
[`dpapi-meeting-data-at-rest` decision](../specs/dpapi-meeting-data-at-rest.md)
defines protection and migration, but issue #17 closed after recording the
decision; it did not implement encryption. These files include transcripts,
summaries, notes, and attendee names.

Audio is now stored in authenticated encrypted containers and its per-meeting
key is DPAPI-protected. README still says WAV audio remains plaintext and
describes audio encryption as future work. The remaining text-data exposure
and stale audio statement should be resolved together: implement the accepted
text design and update the storage guide to describe both formats accurately.

Follow-up: [#68](https://github.com/d1dac0/biscotti-win/issues/68).

### P2 — Long-session channel drift has no measured bound

`DualChannelRecorder` serializes each channel's callback samples separately;
`TranscriptAssembler` merges Whisper timestamps by sorting them. There is no
shared hardware clock, cross-channel calibration, adaptive resampling, or
long-meeting drift test. `AntiAliasDecimator` preserves its own input/output
sample ratio; that does not prove that the independent microphone and render
clocks stay aligned. Replace the “drift is negligible over hour-long meetings”
comment with a measured bound or implement a correction after measurement.

Follow-up: [#69](https://github.com/d1dac0/biscotti-win/issues/69).

### P2 — Speaker quality is not validated on the difficult cases

The diarizer computes one CPU embedding per Whisper line only when the line is
at least one second long, then uses a greedy centroid clusterer at cosine
similarity 0.88. Missing or short segments preserve a fallback label. The
small public-clip smoke check is useful implementation evidence, but it does
not establish diarization accuracy with overlapping voices, far-field noise,
accents, or short turn-taking. The current product does not separate
crosstalking voices and should not imply that it does.

Follow-up: [#70](https://github.com/d1dac0/biscotti-win/issues/70).

### P2 — CPU performance expectation arithmetic disagrees with the matrix

[`transcription-performance-matrix.md`](../transcription-performance-matrix.md)
defines RTF as seconds of audio divided by transcription wall-clock seconds.
With the measured CPU RTF of 1.38×, a 60-minute meeting is approximately
43.5 minutes of transcription, not “a bit over an hour.” The measured RTX 3080
row is 75.7×. Keep the absent iGPU/RTX 3060 data explicitly pending; do not
publish estimated values as measurements.

Follow-up: [#71](https://github.com/d1dac0/biscotti-win/issues/71).

### P2 — No end-user installer or signing path

Windows CI restores, builds, and runs tests, but the repository has no installer
or release artifact workflow. Define install, update, uninstall, user-data
retention, signing, and runtime/model distribution before calling the app
ready for public distribution.

Follow-up: [#72](https://github.com/d1dac0/biscotti-win/issues/72).

### Open pre-release decisions

- **Audio quality:** No app-side AEC, dereverb, or denoising is present. Current
  capture records the OS/app output and microphone as supplied. Define the
  supported far-field/noisy-room quality target and measure it before adding a
  local enhancement chain.
- **Privilege boundaries:** The audit does not have authoritative test
  evidence that process loopback captures elevated target processes from a
  non-elevated Biscotti process. Test this explicitly and document any Windows
  restriction; do not claim it as a confirmed failure based on the old issue
  text alone.
- **Retention:** The user can delete meetings; there is no storage quota or
  automatic retention policy. At 16 kHz mono 16-bit PCM, the two channels are
  about 220 MiB per recorded hour before small container overhead. Keep
  retention user-controlled unless the product defines an opt-in policy.
- **Same-user threat model:** DPAPI CurrentUser protects the audio key from
  other users and offline copied files, but software already running as the
  same Windows account can ask DPAPI to decrypt it. Text files currently have
  no at-rest protection. The accepted spec documents roaming-profile and
  password-reset risks.

## Updated pre-mortem

1. **The wrong audio reaches the transcript.** App-only startup fails or the
   user is on Windows 10, and the app records system-wide playback. A visible
   status warning can still be missed. Fail closed or ask before broadening.
2. **Long meetings produce misordered turns.** Independent capture clocks drift
   without a measured limit, shifting “You” and remote turns across the merged
   transcript.
3. **Sensitive minutes are exposed to another process or local user.** Audio
   is encrypted, but structured text and Markdown are still plaintext; README
   also gives an obsolete audio statement that weakens trust in its privacy
   guidance.
4. **Speaker labels look more certain than they are.** The evaluated samples
   do not cover crosstalk/noise; a mixed segment can yield a mixed embedding,
   and overlaps are not separated.
5. **The app works in CI but cannot be installed cleanly.** There is no release
   package, signing path, or update/uninstall test yet.

The old “CPU-only transcription is slower than real time” pre-mortem should be
retired: measured CPU RTF is 1.38× by the matrix's definition. The matrix's
60-minute example needs correction, and other GPU tiers remain unmeasured.

## Hard questions answered

| Area | Current answer | Decision or evidence still needed |
| --- | --- | --- |
| Core loop: which loopback? | Selected process tree on supported Windows 11; whole-device mix on Windows 10 or when process startup fails. | Fail closed or request explicit confirmation before widening capture. Run a real two-app test. |
| Core loop: how much drift? | No bound is recorded. Per-channel sample-rate conversion is exact, but channel clocks are independent. | Measure start/end skew on long dual-channel recordings, set a transcript-level limit, then decide whether to resample. |
| Core loop: clean-enough audio? | No app-side echo cancellation or denoising. | Define room/noise target and evaluate local processing; do not promise noise isolation today. |
| Local AI: speed by hardware? | CPU 1.38×, RTX 3080 75.7× on one 289.8-second large-v3-turbo fixture. | Correct CPU duration arithmetic and measure iGPU/RTX 3060 on those systems. |
| Local AI: GPU choices? | CPU is baseline; CUDA is opt-in. There is no DirectML backend. | Compare runtime support, model parity, packaging size, and fallback across actual devices before adding a second GPU backend. |
| Local AI: speaker quality? | Per-meeting embedding clusters work on the small sample set; no cross-meeting voiceprints or overlap separation. | Build a labeled noisy/crosstalk evaluation set and set an accuracy bar. |
| Storage: abrupt termination? | Atomic JSON writes and authenticated audio frames preserve prior metadata and complete audio frames. | Add/record a process-kill recovery smoke test against a live WPF recording. |
| Storage: what is encrypted? | Audio containers use AES-GCM with a DPAPI CurrentUser-wrapped key. `meeting.json` and `meeting.md` remain plaintext; models/settings are not meeting content. | Implement the accepted text-file design and state same-user limitations. |
| Storage: retention? | Users can delete meetings manually; no quota or automatic deletion. | Define and test a user-controlled storage warning/retention policy before large-scale use. |
| Windows UX: single instance and notifications? | Tray, hotkey, Windows notifications, mutex, and current-user activation pipe are implemented. | Keep duplicate-launch/exit tests and manual Focus Assist verification in release smoke. |
| Windows UX: calendar? | Local ICS input avoids cloud auth and Graph tokens. | Preserve the local-only default; any future online provider needs a separate opt-in decision. |
| Windows UX: health? | Persistent device warnings and a one-second snapshot poll expose many failures. | Perform physical disconnect/default-device tests and verify process-loopback app exit is surfaced. |
| Scope/build: what is the Windows boundary? | The product is a .NET 9 WPF/WASAPI app; do not port the upstream macOS app by adding Windows conditionals around AppKit/CoreAudio/XPC. | Keep shared data/AI contracts separate from Windows audio, storage, shell, and process adapters. |
| Scope/build: package and sign? | No release packaging/signing path exists. | Choose format and code-signing workflow before public distribution. |
| Scope/build: can the upstream code be reused? | The upstream README declares PolyForm Perimeter 1.0.1 and this project describes itself as a Windows PC port. | Obtain rights review before distributing a product that may compete with the upstream software. |

## Source references

- Current implementation: [`DualChannelRecorder`](../../Biscotti.Core/Audio/DualChannelRecorder.cs), [`AntiAliasDecimator`](../../Biscotti.Core/Audio/AntiAliasDecimator.cs), [`MeetingStore`](../../Biscotti.Core/Storage/MeetingStore.cs), [`EncryptedAudioFile`](../../Biscotti.Core/Audio/EncryptedAudioFile.cs), [`SpeakerDiarizer`](../../Biscotti.Core/Transcription/SpeakerDiarizer.cs), [`SpeakerEmbeddingClusterer`](../../Biscotti.Core/Transcription/SpeakerEmbeddingClusterer.cs).
- Design records: [`DPAPI text decision`](../specs/dpapi-meeting-data-at-rest.md), [`encrypted audio`](../specs/encrypted-audio-streams.md), [`per-app loopback`](../specs/per-app-loopback-capture.md), [`audio device changes`](../specs/audio-device-selection.md), [`speaker diarization`](../specs/speaker-diarization.md), [`tray and lifecycle`](../specs/tray-hotkey-notifications.md).
- Performance evidence: [`matrix and methodology`](../transcription-performance-matrix.md).
- External sources: [Microsoft WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording), [Microsoft application-loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/), [DPAPI CurrentUser scope](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope), and [PolyForm Perimeter 1.0.1](https://polyformproject.org/licenses/perimeter/1.0.1).
