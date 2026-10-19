# Live transcription reads fixed windows from the growing WAV files

**Status:** In Progress

## Decision

Transcribe each channel's growing WAV in serial, non-overlapping 30-second PCM windows on one background worker. The worker reads only complete windows while recording and drains the final partial window after capture stops. Each segment's relative Whisper timestamps are offset by its position in the source WAV, and completed lines are appended to the meeting and shown in its live window.

## Alternatives

Overlapping rolling windows could improve recognition at boundaries, but require duplicate-text reconciliation and repeat inference. Queuing copied audio buffers from capture callbacks can grow memory when inference falls behind. Re-running Whisper over the entire recording after Stop doubles inference work.

## Why

The recorder already writes crash-readable WAV files and Whisper processes WAV streams. Reading bounded ranges from those files keeps capture callbacks simple, bounds the worker to one inference at a time, avoids overlap duplication, and lets the same stream pass produce both the live and saved transcript.
