# Windows echo control verification

Issue: #90

## Synthetic measurements

`EchoLeakageAnalyzer` searches the microphone signal for a delayed linear copy
of the speaker reference and reports the fitted gain, delay, and correlation.
It runs only on in-memory samples and is used for hardware-free regressions.

- A synthetic speaker case with gain `0.20` measures about `-13.98 dB`.
- A synthetic headphone case with gain `0.002` measures about `-53.98 dB`, a
  40 dB reduction from the synthetic speaker case.
- A delayed synthetic echo at 73 samples (4.56 ms at 16 kHz) and gain `0.25`
  is recovered at about `-12.04 dB` with correlation near `1.0`.

These figures validate the measurement algorithm; they are not acoustic
measurements from physical speakers or headphones.

## Hardware observations

Not run in this session. The microphone and output endpoints require a local
headphones-versus-speakers walkthrough. The app reports whether Windows AEC
started for the selected devices; if it cannot start, it continues with the
existing microphone capture path and reports that microphone audio is not
echo-cancelled.

The WPF app launched successfully on Windows build 26200. The meeting was not
started, so this confirms startup only, not AEC hardware support or acoustic
performance.

User-reported hardware observation (2026-10-21): a screenshot during recording
showed about 20 seconds elapsed, with 0.6 MB in each channel, and the status
“Windows echo cancellation is unavailable for this microphone or output.
Microphone audio is recorded without echo cancellation.” The user reported both
channels were heard correctly and no echo or loopback was noticed. This verifies
that the fallback status is visible and both channels captured data; it does not
measure acoustic leakage or verify AEC on this device setup. The screenshot
showed all-speaker loopback selected, so app-only loopback behavior also remains
unverified.

For the walkthrough, select the same microphone and output used by the call,
record the same synthetic speech sample once through speakers and once through
headphones, then compare the microphone channel playback/transcript and confirm
that the speaker loopback remains isolated in its own channel. Keep the test
recordings local and delete them after inspection.
