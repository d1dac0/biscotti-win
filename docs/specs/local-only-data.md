# Local-only data: everything on-device, nothing uploaded

**Status:** Accepted

## Decision

All user content — audio, transcripts, summaries, models, replayed recording
health — stays on the local machine. Transcription (Whisper via Whisper.net)
and summarization (Ollama) run on-device. The only network calls the app makes
are for downloads the user initiates: Whisper GGML models from Hugging Face and
GPU runtime packages from NuGet/NVIDIA. Everything lives under
`%LOCALAPPDATA%\BiscottiWin` (settings.json, Models, Meetings, runtimes) and
nothing from there is ever committed.

## Alternatives

- **Cloud transcription/summarization** — immediate privacy loss and a new
  dependency surface; rejected outright.

## Why

- Privacy is the product's core promise; a meeting app that leaves the machine
  is a different (and delivering-against) product.
- Storage under `%LOCALAPPDATA%` gives crash-safe writes, DPAPI compatibility
  (issue #17), and a single cleanup story.
- Applies to agent work too: never stage generated files or meeting data into
  git.
