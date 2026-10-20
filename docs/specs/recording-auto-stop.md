# Auto-stop recording after loopback silence

**Status:** In Progress

Issue: #4

## Decision

Infer that a call has ended from five continuous minutes without meaningful
audio on the captured loopback channel. Meaningful output is a loopback packet
whose mono RMS level reaches -40 dBFS (0.01); any later active packet resets
the silence interval. Auto-stop is armed only after at least one active output
packet has been observed in the current recording. If output is never heard,
auto-stop remains inactive and the recording UI says so; this keeps in-person
meetings with no system audio under manual control. When armed silence expires,
stop capture and continue the normal transcript and summary workflow.

## Alternatives

- Stop after five minutes of silence from recording start: rejected because
  in-person meetings and calls that have not started playing audio would stop
  unexpectedly.
- Ask Windows or the meeting application whether the call ended: deferred
  because there is no common call-state API across supported applications.
- Add an auto-stop setting and timeout picker: deferred; the initial story has
  no settings requirement, and one documented interval keeps this behavior
  predictable.

## Why

Loopback silence is only a heuristic for call end, but requiring prior output
activity avoids treating a never-audible, in-person meeting as an ended call.
The UI must show whether the heuristic is inactive or armed. With whole-device
loopback, unrelated desktop audio can refresh the timer; per-app capture in
issue #14 will make the signal more specific when enabled. A five-minute
interval tolerates ordinary pauses while bounding captured dead air.

## Implementation plan

1. Add a hardware-free loopback activity detector and silence monitor with
   injectable timestamps; add regression coverage for quiet packets, active
   packets, timer reset, and never-active recordings.
2. Publish loopback activity from the recorder and connect the monitor to
   MeetingService. Make the five-minute trigger stop and process a recording
   only once, even if a manual stop races with the timer.
3. Show a clear recording status while auto-stop is inactive and when it is
   armed; retain the existing per-channel health and device warnings.
4. Reconcile recorder and service changes with PR #42, then run the build and
   full check and manually inspect the recording status UI.

## Verification

- Baseline on `origin/main` commit `dfbc164`: Release build succeeded with
  three existing CA2022 warnings; `tools/check.ps1` passed all 55 tests.
- Final Release build succeeded with 0 warnings and 0 errors;
  `tools/check.ps1` passed all 60 tests; `git diff --check` passed.
- Rendered the app's main window and confirmed the idle state. Did not start
  live capture, so the five-minute trigger was not manually exercised; the
  monitor's inactivity, activity, reset, threshold, and stereo-downmix behavior
  is covered by hardware-free tests.
