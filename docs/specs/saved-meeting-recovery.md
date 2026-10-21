# Saved meeting recovery and retry

**Status:** Accepted

## Decision

Persist a small `ProcessingStatus` on each protected meeting record. A meeting
left `Recording` after process exit is an interrupted capture; recovery closes
its metadata and retains its complete authenticated audio frames, including
when the final frame is truncated. The user can then review audio or explicitly
retry transcription. Transcription retry replaces transcript lines only after
both available channels finish successfully; a failure keeps the prior lines
and status says the retry failed. A successful retry marks the summary pending
but preserves any previous summary, notes, title and action items. Summary retry
stages new parsed output and replaces summary/action items only after a non-empty
valid response; a failure retains prior values. A user-edited title is never
replaced. Processing uses the existing local Whisper and Ollama services; no
models or data are sent to a remote service.

The current format keeps one active transcript and summary, with no revision
history. A successful retry replaces that result in the protected meeting
record; a failed retry changes only its processing status and leaves the last
saved content available.

## Alternatives

Automatically retrying on startup was rejected because it could consume local
resources or change saved text without a user action. Replacing a transcript or
summary incrementally was rejected because a missing model, unreadable protected
audio or unavailable Ollama could destroy a previously useful result. A separate
job database was rejected for this scoped recovery path; the protected meeting
record already has atomic saves and contains the status needed to offer recovery.

## Why

Status written before capture and each processing stage identifies the safe
recovery action after an abrupt exit. Explicit actions and staged replacement
keep recovery predictable and preserve the user's last good results.
