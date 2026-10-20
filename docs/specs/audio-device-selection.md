# Audio device selection and device-change warnings

**Status:** Accepted

Issue: #19

## Decision

Add an Audio devices dialog reachable from the main window. Users select a
microphone and output device, refresh the device lists, and save their choices.
The output list includes an explicit Windows default option. Resolve and
validate saved choices before every recording; unavailable selections open the
dialog rather than silently choosing another device.

Keep the existing microphone capture path and its 16 kHz / 48 kHz fallback.
Persist the microphone index together with its reported name, and require a
new choice if that pair no longer matches the enumerated device. This detects
ordinary index changes without claiming that legacy microphone indices are
stable hardware identifiers. Persist explicit output choices by endpoint ID.

Starting a meeting must successfully open and start both selected capture
channels. If either fails, stop and dispose the partial capture, preserve any
existing meetings, and prompt the user to select devices. The microphone must
produce capture callbacks within a bounded startup interval. A loopback device
may legitimately produce no samples while Windows is silent, so successful
endpoint activation is its startup check, not a requirement to hear speech.

During recording, monitor capture failures and relevant endpoint changes.
Keep a visible warning in the recorder UI when the selected device disappears
or when the Windows default output changes while default output is selected.
Continue any healthy channel and preserve the audio already captured. Tell the
user to stop, select devices, and start a new recording. Do not switch devices
silently within the current meeting.

## Alternatives

- Automatic hot swapping: deferred because a replacement device may have a
  different clock or format, and continuity across encrypted audio frames and
  live transcription windows needs separate design and testing.
- Converting microphone capture to WASAPI solely for stable endpoint IDs:
  deferred to avoid replacing the existing microphone compatibility fallback
  as part of a picker and warning change.
- Persisting microphone index alone or silently falling back to another device:
  rejected because a changed index can record the wrong microphone.

## Why

Explicit selection prevents recording the wrong audio source. Persistent
warnings make device loss visible without discarding the other channel or
claiming that a disconnected device is still recording. This is the warning
option allowed by #19 and keeps the initial scope bounded.

## Implementation plan

1. Add device choice models, safe enumeration/disposal, saved-selection
   validation, and microphone name persistence alongside the existing index.
2. Add the Audio devices dialog and main-window entry point. Refresh lists,
   indicate unavailable saved choices, and disable the Audio devices entry
   point and all selection/save controls during recording, transcription, or
   summarization. Re-enable them only when the meeting service returns to idle.
   Guard the save handler as well, so a dialog opened before recording cannot
   change active device settings. Offer the dialog when recording cannot start with the saved
   devices.
3. Make capture startup transactional, retain the microphone format fallback,
   and report startup failures without leaving a half-started recording.
4. Monitor endpoint changes and capture-stop errors. Display a warning that
   remains visible when the periodic recording-health status refreshes, and
   dispose the device-monitoring subscription when the recording ends. The
   implementation compares device snapshots once per second and also handles
   capture-stop errors; it does not require audio samples to detect an output
   change.
5. Add hardware-free regression coverage for selection validation, startup
   rollback, relevant device-change warnings, and subscription cleanup. Run
   tools/build.ps1 and tools/check.ps1, then inspect the rendered dialog and
   manually check selection and unplug/default-device-change behavior on the
   local Windows machine.

## Coordination

PR #42 also changes the recorder and main window for per-app loopback capture.
Reconcile its latest state before implementation and again before landing.
Device selection must preserve its capture routing and fallback warnings.

Playback remains separately tracked by #48 and is deferred at the user's request.

## Verification

- Clean baseline: Release build and 45 tests passed at 631d275.
- Release build and full check: 54 tests passed after implementation.
- Rendered dialog inspected; microphone and output enumeration and saving
  succeeded on the local Windows machine.
- Live recording check: device selection disabled during startup, recording,
  and transcription; re-enabled after processing finished. Microphone callbacks
  arrived and silent system output did not prevent recording.
- Physical unplug and Windows default-output change checks were not performed;
  hardware-free snapshot tests cover these transitions and warning behavior.
- Original device preferences were restored after the live check.

