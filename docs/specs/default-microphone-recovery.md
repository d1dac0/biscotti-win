# Recover a missing microphone before recording

**Status:** Accepted

## Decision
Before a new recording and when loading the device picker, retain a connected
explicit microphone by its endpoint identity (or index and name for legacy
settings). Otherwise choose the current Windows Console default input. Use
the WaveIn default mapper when its endpoint cannot be matched uniquely to a
legacy WaveIn device. Resolve again at capture startup to handle reconnects.
Active recordings continue to warn without switching devices.

## Alternatives
Requiring users to reselect devices after every reconnect leaves recording
unnecessarily blocked. Choosing the first enumerated microphone can select a
different source from the Windows default.

## Why
Issue #105 requires recovery without overriding a connected explicit choice.
Endpoint identity survives WaveIn index changes; the Windows default mapper
also works when truncated or duplicate legacy device names prevent matching.
