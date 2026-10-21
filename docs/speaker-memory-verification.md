# Speaker memory verification (#82)

Verified on Windows, 2026-10-20. Baseline b43780c passed 123 tests.

## Automated checks

`tools/build.ps1` and `tools/check.ps1` pass: 132 Core/MCP tests and one
expanded native WPF flow test (133 total, none skipped). Existing NAudio
obsolete-API and inexact test-read warnings remain. `git diff --check` passes.

Regressions cover DPAPI enrollment round-trip and absence of plaintext labels,
opt-in requirements, corrupt-store preservation, deletion and disabled restart,
reassignment of a mistaken enrollment, a forgotten person's stale ID, stable
correction groups, old transcript compatibility, and corrected meeting storage.
The store tests run in an isolated xUnit collection because existing storage
tests temporarily replace the process-global text protector.

The matcher rejects missing, invalid, dimension-mismatched, weak, ambiguous,
conflicting and same-meeting-only evidence. A deterministic sweep rejects 100
unseen synthetic vectors and accepts 20 small perturbations of an enrolled
synthetic voice. This evaluates policy branches, **not** real-world biometric
accuracy. No NVIDIA hardware, private recordings, or external services are used.

The native WPF test covers disabled live-recording controls, label correction,
opt-in, transient synthetic analysis, no-suggestion state, explicit enrollment
action, and forget-all disabling memory while preserving transcript labels.

## Windows walkthrough

From the issue worktree root:

```powershell
dotnet run --project Biscotti.Win -- --design-preview=speakers
```

This uses the existing isolated in-memory preview startup: no profile settings,
meeting data, audio capture, model downloads, or voiceprint files are opened.
The generated Debug executable was confirmed in the build's
`Biscotti.Win/bin/Debug/net10.0-windows10.0.17763.0/win-x64/` directory.

Inspected the native Speakers tab at 940 by 760: text wraps, native tab/focus
controls remain visible, the lower memory controls are accessible by scrolling.
Entered `Alex corrected`, selected Save label, then inspected Transcript:
both Alex turns changed, Jordan stayed unchanged. The WPF flow test exercises
synthetic opt-in/analyze/forget; the manual walkthrough did not change real
privacy preferences or enroll anyone. Groups include their first-turn timestamp
to distinguish groups that share a display name.

## Limits and skipped checks

Suggestions always need explicit confirmation and never overwrite transcript
labels automatically. Existing summary prose is not rewritten by corrections.
Old transcripts without stable voice-group keys support labels but cannot enroll
a potentially mixed voice. Existing installed speaker models are required for
saved-audio analysis; analysis does not initiate downloads.

Independent review, extended performance testing and consented real-speaker
accuracy calibration were not run. In particular, the matcher thresholds are
engineering guards rather than measured false-accept guarantees. No claims of
production biometric reliability are made.
