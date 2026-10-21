# Windows microphone echo control

**Status:** Accepted

Issue: #90

## Decision

Request Windows communications-mode processing for the selected microphone through WASAPI and set the selected output endpoint as the AEC reference when Windows and the microphone endpoint expose the reference control. If Windows cannot apply that path, keep the existing WaveIn capture and tell the user the microphone is being recorded without guaranteed echo cancellation. Keep the processed microphone and speaker loopback in their separate encrypted channel files.

## Alternatives

- Bundle a software AEC engine such as WebRTC Audio Processing. This adds native runtime packaging and requires synchronizing the separately clocked microphone and output callbacks, delay estimation, and device changes.
- Rely on Windows choosing its own reference in communications mode. This does not guarantee that the selected speaker endpoint is the reference.
- Remove WaveIn and require WASAPI capture. This would drop the current device fallback on machines without a supported communications capture path.

## Why

Windows exposes AEC through a capture-endpoint audio processing object and provides a WASAPI control for choosing the render reference. This keeps echo cancellation on-device and lets the OS route the selected endpoint into its processing pipeline. Endpoint and driver support varies, so the fallback must remain explicit and must not claim that separate channels themselves cancel echo.

The explicit reference-control API requires Windows 11 build 22621 or later. The availability of the control is treated as the support signal; without it, capture continues without a claim that echo was removed.

## Verification plan

1. Add hardware-free tests for endpoint matching, capture fallback/status, and a synthetic leakage measurement algorithm.
2. Record Windows/driver limitations and a reproducible headphones-versus-speakers walkthrough in `docs/echo-control-verification.md`.
3. Run `tools/build.ps1` and `tools/check.ps1`; record hardware observations separately from synthetic test results.
