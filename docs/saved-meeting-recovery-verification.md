# Saved meeting recovery verification

The recovery path was verified with synthetic in-memory meeting data. No audio
was captured, and the preview does not load Whisper or Ollama models.

Run the preview from the repository root:

```powershell
dotnet run --project Biscotti.Win -- --design-preview=recovery
```

The review window starts with an interrupted recording and offers **Recover
saved audio**. After recovery, **Retry transcription** is available; retrying
keeps the existing summary visible and explains that it still matches the old
transcript. **Regenerate summary** then returns the meeting to the complete
state. The native Windows review confirmed each status and button transition.

`tools/build.ps1` passed. `tools/check.ps1` passed with 158 Core/MCP tests and
1 Windows flow test, with none skipped. Tests cover protected processing status
round-tripping, interruption recovery, successful retries, empty results,
storage denial, unavailable Ollama, and preservation of prior user content.
The encrypted audio suite separately verifies authentication and recovery of
complete frames when the final frame is truncated. Existing obsolete NAudio
API and inexact test-read warnings remain.
