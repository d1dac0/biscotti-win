# Biscotti Win

Free, private meeting transcription for Windows — a PC port of [Biscotti (macOS)](https://github.com/scosman/Biscotti).

Records your meetings and turns them into speaker-labeled transcripts and AI summaries.
**No cloud. No bot joins your call. No account.** Recording, transcription, and summaries all run locally on your PC.

## How it works

1. **Record** — captures your microphone ("You") and your PC's system audio ("Speaker N", everyone on the call) via WASAPI loopback from any app: Zoom, Teams, Meet, FaceTime (web), Slack huddles — or an in-person conversation through your mic.
2. **Transcribe** — on-device speech recognition with [Whisper.cpp](https://github.com/ggml-org/whisper.cpp) via [Whisper.net](https://github.com/sandrohanea/whisper.net). The GGML model downloads once (Large v3 Turbo ~1.6 GB, or Small ~466 MB).
3. **Summarize** — a local LLM through [Ollama](https://ollama.com) (default: Gemma 3) writes a summary, action items, a meeting title, and guesses participants' real names if they said them aloud.

Nothing leaves your machine. Ollama is optional — without it you still get the transcript.

## Tray controls

Biscotti Win stays available from the notification area. Closing the main window hides it in the tray; use the tray menu's **Exit** command to quit. The icon reflects whether the app is idle, recording, or processing. **Ctrl+Alt+R** starts or stops recording by default; change the shortcut from **Settings** in the tray menu. Windows controls whether the summary-ready notification appears during Focus Assist / Do not disturb.

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

Meeting files are currently stored as plaintext. The accepted design for issue
#17 protects `meeting.json` and `meeting.md` with Windows DPAPI for the current
user in the release that implements it; existing meetings are migrated without
deleting their source files until each protected replacement is verified. WAV
audio remains plaintext in that first phase. DPAPI CurrentUser does not protect
data from software already running as your Windows user. See the
[DPAPI storage decision](docs/specs/dpapi-meeting-data-at-rest.md) for migration,
profile roaming, password-reset behavior, and the follow-up WAV design.

## Chat with local meetings

Biscotti includes a read-only MCP server that lets compatible local MCP clients
search saved meetings, read meeting details, and page through timestamped,
speaker-labeled transcripts. The server reads the same local `meeting.json`
files as the app and does not open audio files or contact remote services.
See [the MCP setup guide](docs/mcp-server.md) for build and client configuration.

## Remote speaker labels

After recording, Biscotti locally groups remote transcript segments using
speaker embeddings. It can replace a cluster label with a real name when the
person introduces themselves or the transcript clearly matches an attendee.
The first processed meeting downloads the 26.5 MB English speaker model into
the local model cache; no meeting audio is uploaded. See
[third-party notices](docs/THIRD-PARTY-NOTICES.md) for model attribution.

## Roadmap (from Biscotti feature parity)

- Windows calendar awareness (offer to record before a meeting starts)
- Voiceprint cross-meeting speaker matching (real per-person names)
- Auto-stop when the call ends
- Custom vocabulary
- Markdown-notes linked to the moment they happened
