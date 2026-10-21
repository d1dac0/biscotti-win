# Controllable recording auto-stop

**Status:** In Progress

Issue: #86. Supersedes [recording-auto-stop.md](recording-auto-stop.md).

## Decision

Retain the five-minute loopback-silence heuristic and -40 dBFS arming threshold.
Never-audible microphone-only recordings remain manual. Persist an enabled-by-default
preference for compatibility, expose it in Settings and on the recording screen,
and allow changing it during capture. After five quiet minutes, show a 30-second
countdown before stopping and processing. New meaningful output cancels the
countdown. Keep recording disables auto-stop for the remainder of that recording,
without changing the saved preference. Re-enabling starts fresh and requires new output.

Countdown notices live in the main window and tray menu, not Windows Action
Center. Reveal the main window on countdown entry so a tray-only recording can
be kept. Clear notices on audio resumption, cancellation, disable, stop, shutdown
and a fresh recording. Hiding/reopening the window does not reset the countdown.
Restarting the application never resumes an old countdown.

## Windows activity investigation

Core Audio exposes running/inactive/expired sessions through
IAudioSessionControl.GetState and session events. Active means a stream is
running, including silent streams; inactivity does not establish call end.
Capture and rendering sessions are separate. GetProcessId can return
AUDCLNT_S_NO_SINGLE_PROCESS for sessions spanning multiple processes. Browser
tabs and meeting applications do not share a documented call-state contract.
Process exit/reopen alone therefore does not replace the silence heuristic.
No app-activity watcher ships here; a future watcher needs measured false-positive
and false-negative evidence across supported applications first.

Primary references (reviewed 2026-10-20):

- [AudioSessionState](https://learn.microsoft.com/en-us/windows/win32/api/audiosessiontypes/ne-audiosessiontypes-audiosessionstate)
- [Audio sessions](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions)
- [GetProcessId](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-getprocessid)

## Alternatives

Stop when a meeting process exits: unsafe for browser tabs, process restarts and
microphone-only meetings. Infer call state from another app's microphone session:
deferred because stream state is not call state. Immediate stopping: rejected
because long quiet calls need an escape. Repeat countdowns after Keep recording:
rejected to avoid repeatedly interrupting a deliberately quiet recording.

## Why

Silence is an explainable heuristic. Unrelated desktop output can delay stopping
in whole-device capture; a quiet call can trigger it. Visible explanation, a
persistent opt-out and a session-only override give users control while retaining
in-person safety. No audio or application usage data leaves the machine.

## Implementation plan

1. Add a timestamp-driven controller and lifecycle regression tests.
2. Connect recording lifecycle, saved preference, countdown notice and tray action.
3. Run build/check, inspect synthetic countdown and Settings, and record results.
