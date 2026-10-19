# Platform: Windows x64 only, revisited by portability audit

**Status:** Accepted (revisit when #11 runs)

## Decision

Biscotti Win targets Windows x64 exclusively: WPF on `net9.0-windows`,
x64-only native runtimes (Whisper.net win-x64; the GPU tier is Windows x64
only), PowerShell-based repo tooling (`tools/*.ps1`). Cross-platform (macOS)
is explicitly out of scope for now.

## Alternatives

- **.NET cross-platform UI (Avalonia/etc.)** — deferred: WPF is the fastest
  path to the Windows-caliber capture features (WASAPI loopback, per-app
  routing) this product lives on.

## Why

- The differentiating captures (mic + system loopback of the meeting app) are
  Windows-specific audio plumbing; a cross-platform UI would not carry their
  weight yet.
- Issue #11 (macOS-to-Windows portability review, audit) is the standing
  mechanism to revisit this; it runs only on the human's explicit go-ahead.
