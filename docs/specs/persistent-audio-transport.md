# Persistent encrypted audio transport and timestamp seeking

**Status:** Accepted

## Decision

Keep one audio transport visible below the meeting review tabs while users read
the transcript, notes, summary, or speakers. It selects microphone or system
audio, plays and pauses/resumes the selected channel, shows position and
duration, and supports a scrubber plus 5-second and 30-second keyboard seeks.
Timestamp links seek the selected channel and start playback at that time while
continuing to select the matching transcript turn. Seeking clamps to the
recording bounds. The transport owns one authenticated decrypting WAV stream;
channel switches stop and dispose the old output and stream before opening the
next channel. Closing stops and disposes playback; deleting a meeting must
successfully stop it before removing either channel file. Plaintext audio is
never written to disk, and decoded ranges remain bounded to the encrypted
reader's authenticated frame reads.

## Alternatives

Keeping controls only in the Audio tab makes seeking unavailable during note
review and timestamp navigation. Decrypting a whole recording for a simpler
seekable memory stream scales memory with meeting length and violates the
bounded-playback requirement. Maintaining separate simultaneous players makes
channel changes and delete ordering harder to reason about.

## Why

Timestamp navigation is useful only when playback can follow it, and users
need that action while reading the transcript and notes. The existing encrypted
reader already provides authenticated random access by frame, so a persistent
single-channel transport can support fast seeks without creating plaintext
scratch audio or buffering the complete meeting.
