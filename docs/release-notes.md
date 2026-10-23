Free, private meeting transcription and review for Windows. Recording, transcription, and summaries run on your PC; meeting audio, transcripts, summaries, and models are never uploaded.

## Before you start

- **Microphone:** Windows asks for microphone access on first launch. Open Audio devices to pick your microphone and output device.
- **Optional AI summaries:** install [Ollama for Windows](https://ollama.com) and run `ollama pull gemma3:4b` (about 3.3 GB). Without Ollama you still get full transcripts.
- **Whisper model:** downloads on first use to `%LOCALAPPDATA%\BiscottiWin` — about 1.6 GB for Large v3 Turbo (default) or 466 MB for Small. An optional CUDA runtime (about 330 MB) speeds up transcription on compatible NVIDIA GPUs.

## Install and update

Download `BiscottiWin-<version>-win-x64.zip` from this release and follow the [Windows install guide](https://github.com/d1dac0/biscotti-win/blob/main/docs/windows-release.md). Extract to `%LOCALAPPDATA%\Programs\BiscottiWin` — no administrator rights and no separate runtime installs. Updates overlay the same folder after closing the app; meeting data lives separately under `%LOCALAPPDATA%\BiscottiWin` and survives updates and uninstall.

Verify the executable's digital signature before running it, and only download from this repository's release page. See the guide's SmartScreen notes.

## Changes in this release

