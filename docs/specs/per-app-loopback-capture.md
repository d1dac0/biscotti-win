# Per-app loopback uses NAudio's process capture

**Status:** Accepted

## Decision

On Windows 11, capture a user-selected running app's process tree with NAudio's process-loopback recorder and write that stream to `sys.wav`. On Windows 10, continue using device loopback and show a clear warning that all playback is included. If process capture cannot start, fall back to device loopback and surface the fallback in the recording status. Keep the selected process ID for that recording only.

## Alternatives

Implementing `ActivateAudioInterfaceAsync` and its COM completion handler directly would keep NAudio 2, but would add a second low-level WASAPI implementation. Automatically selecting an app from audio sessions would miss apps that have not started rendering yet and would make the capture target less visible to the user.

## Why

The app already targets .NET 9, which NAudio 3 supports. NAudio 3 includes process-loopback capture with child-process-tree support, avoiding custom activation interop. A per-recording process choice avoids persisting stale PIDs. The Windows 10 warning preserves the story's stated compatibility behavior.

## References

- [Microsoft application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
- [NAudio process-loopback recorder documentation](https://github.com/naudio/NAudio/blob/main/Docs/WasapiRecorder.md)
