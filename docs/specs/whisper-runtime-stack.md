# Whisper runtime stack: Whisper.net 1.9.0, CPU bundled, GPU lazy opt-in

**Status:** Accepted

## Decision

Transcription runs on Whisper.net 1.9.0 (whisper.cpp bindings). The **CPU
runtime (`Whisper.net.Runtime`) is the only bundled package reference** — it
works on every Windows x64 machine. The **GPU (CUDA) tier is not a package
reference**: on first run an NVIDIA GPU is detected via `nvidia-smi -L`, the
user opts in once (`AppSettings.WhisperRuntime`), and `GpuRuntimeStore`
downloads `whisper.net.runtime.cuda.windows` 1.9.0 plus NVIDIA's official
Windows redistributable wheels (`nvidia-cuda-runtime` 13.1.80,
`nvidia-cublas` 13.1.1.3) into `%LOCALAPPDATA%\BiscottiWin\runtimes\cuda\win-x64`.
`WhisperRuntimeConfigurator` activates it at startup only when the files are
present, points `RuntimeOptions.LibraryPath` at the data root via the
`runtime-root.marker` file path, and preloads `cudart64_13.dll` by absolute
path so Whisper.net's bare-name CUDA probe resolves. Any missing piece falls
back silently to the bundled CPU runtime.

## Alternatives

- **Vendoring `Whisper.net.Runtime.Cuda.Windows` as a NuGet reference** — bloats
  every install and crashes non-CUDA machines (the issue #3 lesson); rejected.
- **Hand-rolled DLL probing / AddDllDirectory tricks** — fragile against
  Whisper.net's own `NativeLibraryLoader` evolution; using the library's
  documented `RuntimeOptions` knobs keeps the app on its supported path.
- **Third-party NVIDIA repacks (NtvLibs)** — work, but NVIDIA's own Windows
  wheels are the official redistributable channel; preferred for provenance.

## Why

- CPU default never blocks or crashes a machine without NVIDIA hardware —
  the hard lesson from issue #3, proven again by the silent CPU fallback paths.
- The GPU tier is ~50x faster measured (75.7x vs 1.38x realtime on a
  4.8-min meeting WAV; see `docs/transcription-performance-matrix.md`), so it
  is worth a lucid opt-in with a visible one-time download.
- Lifecycle: everything under `%LOCALAPPDATA%` is deletable, re-downloadable,
  and never enters git, keeping the repo small and installs slim.
- Delivered in PR #22 (CPU) and PR #26 (GPU tier, issue #20).
