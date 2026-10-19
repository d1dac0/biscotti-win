# Biscotti Win

Free, private meeting transcription for Windows — a PC port of [Biscotti (macOS)](https://github.com/scosman/Biscotti).

Records your meetings and turns them into speaker-labeled transcripts and AI summaries.
**No cloud. No bot joins your call. No account.** Recording, transcription, and summaries all run locally on your PC.

## How it works

1. **Record** — captures your microphone ("You") and your PC's system audio ("Speaker N", everyone on the call) via WASAPI loopback from any app: Zoom, Teams, Meet, FaceTime (web), Slack huddles — or an in-person conversation through your mic.
2. **Transcribe** — on-device speech recognition with [Whisper.cpp](https://github.com/ggml-org/whisper.cpp) via [Whisper.net](https://github.com/sandrohanea/whisper.net). The GGML model downloads once (Large v3 Turbo ~1.6 GB, or Small ~466 MB).
3. **Summarize** — a local LLM through [Ollama](https://ollama.com) (default: Gemma 3) writes a summary, action items, a meeting title, and guesses participants' real names if they said them aloud.

Nothing leaves your machine. Ollama is optional — without it you still get the transcript.

## Build

```powershell
dotnet build -c Release
Biscotti.Win\bin\Release\net9.0-windows\Biscotti.Win.exe
```

## Requirements

- Windows 10/11 with a microphone
- .NET 9 SDK (to build)
- Optional: [Ollama](https://ollama.com) installed with `ollama pull gemma3:4b` for AI summaries

## Data location

Everything is stored under `%LOCALAPPDATA%\BiscottiWin`:

- `Meetings\<guid>\meeting.json` — structured transcript, summary, notes
- `Meetings\<guid>\meeting.md` — human-readable copy
- `Meetings\<guid>\mic.wav` / `sys.wav` — full audio, yours to delete
- `Models\` — Whisper GGML model

## Roadmap (from Biscotti feature parity)

- Windows calendar awareness (offer to record before a meeting starts)
- Voiceprint cross-meeting speaker matching (real per-person names)
- Auto-stop when the call ends
- Custom vocabulary
- Markdown-notes linked to the moment they happened
- MCP server to chat with your meeting notes
