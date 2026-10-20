# Model setup verification (#80)

Verified on Windows x64, 2026-10-20, using synthetic data only.

- `tools/build.ps1`: passed. Existing NAudio obsolete-API and test-read
  warnings remain on a clean compilation.
- `tools/check.ps1`: passed, 113 core tests plus 1 expanded WPF flow test;
  no failures or skips.
- New core coverage: local Ollama discovery, absent server/model, invalid
  responses, cloud/remote exclusion, no generation POST without a local
  model, cancellation, guidance validation/roundtrip and all summary passes;
  disk exhaustion, incomplete response, ignored/successful/rejected resume
  ranges, cancellation and retry.
- Existing `GpuRuntimeTests.SelectOrderGpuRequiresInstall` verifies CPU
  fallback without NVIDIA hardware. Runtime changes are explicitly deferred
  to app restart rather than changing an already-loaded native library.
- The WPF flow test opens Settings, checks model choices/readiness, rejects
  excessive guidance, verifies cancelled edits leave the original untouched,
  and saves a valid draft through the modal dialog.
- Native visual walkthrough via `dotnet run --project Biscotti.Win --
  --design-preview=setup`: opened Settings; inspected model size, readiness,
  free space, download/cancel controls, runtime selection, summary guidance,
  microphone instructions, shortcut and vocabulary through the scrolling
  surface. The preview does not download, record or access meeting storage.
  Validation messages are in the fixed footer beside the persistent actions.

Ollama installation/pulls remain user-run: the Settings instructions explain
`ollama pull gemma3:4b`, progress in its terminal, Ctrl+C cancellation and
retry. Installed sizes come from the local API. Whisper/GPU download progress
and cancellation are in Biscotti itself. No installer or model is downloaded
automatically. Downloads persist even if settings edits are cancelled.

Not run: real multi-GB download, microphone permission changes, physical
capture, independent review, or extended performance measurements. The
failure paths use deterministic fake HTTP responses; no user meeting content
was sent to Ollama or an external service during this verification.

Try the isolated flow with the command above and choose Settings. For real
setup, omit `-- --design-preview=setup`; summary readiness should identify the
selected installed model or explain what is missing without disabling
transcription.
