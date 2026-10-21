# Transcription performance matrix (issue #20)

Purpose: measured Whisper `large-v3-turbo` realtime factors so the app can set
accurate user-facing speed expectations. Realtime factor (RTF) = seconds of
audio / seconds of transcription wall-clock; an RTF above 1 means more than one
second of audio is transcribed per wall-clock second. All measured rows below
use one fixed WAV file (see "Standard input").

## Standard input

- `mic.wav` of meeting `ee409035-…`: 9,273,262 bytes,
  16 kHz mono s16le → 289.8 s of audio. Clocked via a one-file console harness
  (`WhisperFactory.FromPath` + `ProcessAsync`, drain all segments).
- Model: `ggml-large-v3-turbo.bin` (1.51 GB local download).

## Measured (primary test machine, Windows 11)

| Runtime                  | Hardware                                       |     RTF | Wall time for 289.8 s audio |
|--------------------------|------------------------------------------------|--------:|----------------------------:|
| CPU (Whisper.net.Runtime 1.9.0) | Ryzen 9 5900HX, 8 cores / 16 threads    | **1.39×** |                     208.6 s |
| CUDA (Whisper.net.Runtime.Cuda.Windows 1.9.0 + NVIDIA CUDA 13.1 redist) | RTX 3080 16 GB, driver 591.59 | **75.7×** | 3.83 s |

Applying each measured factor to 60 minutes of audio gives about 43 minutes
11 seconds on the tested Ryzen 9 5900HX CPU and 48 seconds on the tested RTX
3080. These are projections from the single fixture above, not guarantees for
other hardware, models, or meetings. Recalculate with the same formula using
`tools/calculate-rtf.ps1` when adding measurements.

## Pending measurements (not re-run on this repo's hardware yet)

The issue's original matrix rows for an Intel/AMD iGPU and an RTX 3060 remain
pending because this repo's primary machine has no such hardware. Do not infer
or publish performance expectations for those tiers from community estimates.
Re-run the same fixture harness on each target system and record its hardware,
runtime, model, and wall time before adding a measured row.

## How the GPU tier is assembled (all lazy, nothing vendored)

| File in %LOCALAPPDATA%\BiscottiWin\runtimes\cuda\win-x64 | Source                                   |
|-----------------------------------------------------------|------------------------------------------|
| whisper.dll, ggml-whisper.dll, ggml-base-whisper.dll, ggml-cpu-whisper.dll, ggml-cuda-whisper.dll | NuGet `whisper.net.runtime.cuda.windows` 1.9.0 |
| cudart64_13.dll                                            | NVIDIA `nvidia-cuda-runtime` 13.1.80 wheel |
| cublas64_13.dll, cublasLt64_13.dll, nvblas64_13.dll         | NVIDIA `nvidia-cublas` 13.1.1.3 wheel      |

- `RuntimeOptions.LibraryPath` is pointed at the data root via the
  `runtime-root.marker` file (Whisper.net probes `runtimes/<variant>/<rid>`
  under the LibraryPath's parent directory).
- `cudart64_13.dll` is preloaded by absolute path so Whisper.net's bare-name
  `cudart64_13` availability probe resolves; Whisper.net then runs its own
  CUDA check and silently falls back to CPU when the GPU is unsuitable —
  a GPU must never block or crash transcription (issue #3 lesson).
