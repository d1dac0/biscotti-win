# Authenticated encryption for meeting audio streams

**Status:** Accepted

## Decision

Use one fresh 256-bit AES key per meeting. Protect that key with Windows DPAPI `CurrentUser`; persist only the wrapped key in each audio container header. Write microphone and system audio as separate authenticated encrypted containers from the first captured sample. Keep the existing 16 kHz mono PCM format inside the container so a reader can expose a WAV-compatible stream to Whisper without ever writing a plaintext WAV to disk.

Use bounded AES-256-GCM frames rather than applying `ProtectedData` to an active file or assuming that `CryptoStream` can directly provide GCM framing. A stream adapter may use `CryptoStream` only if its selected transform provides the required authentication; for GCM, frame records around `AesGcm` operations are the proposed path. CTR without independent authentication is prohibited.

## Proposed container format

- Keep the established `mic.wav` and `sys.wav` paths, but store a versioned encrypted-container format at those paths. The container starts with an 8-byte magic value, format version, PCM format, channel identifier, frame size, and length-prefixed DPAPI-wrapped meeting key. The files are no longer directly playable as WAV; app code must go through the decrypting WAV-compatible reader.
- The meeting key is generated once with a cryptographic random-number generator. Both channel files may carry its DPAPI-wrapped form. The raw key exists only in process memory while recording or reading.
- PCM is split into 4 KiB frames (128 ms at 16 kHz, mono, 16-bit). Each record stores the plaintext frame length, ciphertext, and 16-byte GCM tag. The nonce is the 32-bit channel identifier plus a 64-bit monotonically increasing frame index, making nonces unique across both channels under the shared meeting key.
- Authenticate the immutable container fields, channel identifier, frame index, and plaintext length as associated data. A reader rejects a tag or header mismatch; it does not return unauthenticated PCM.
- Write and flush the complete header before capture starts. Buffer at most one frame per channel; closing flushes the final partial frame. On abrupt termination, recover all complete authenticated frames and report that an incomplete tail was discarded. A truncated header or a failed tag is an explicit error.

## Capture and transcription integration

- Replace `WavStreamWriter` in `DualChannelRecorder` with an encrypted streaming writer. Preserve the current plaintext-byte counters used by recording health and the live worker.
- Give the live worker a decrypting random-access reader over complete frames. It can address its existing 30-second windows without decrypting earlier windows again.
- Build each bounded live window in memory with a synthesized WAV header; remove the current temporary `.wav` segment files. Add a `WhisperTranscriber` stream overload so live windows and final fallback transcription both read a decrypting WAV-compatible stream.
- Preserve the capture callback's bounded work: encryption buffers are fixed-size, writes remain synchronous and ordered per channel, and no full meeting is buffered in memory.

## Migration and failure behavior

- On meeting load, detect legacy plaintext WAVs by the container magic. For each legacy file, encrypt to a sibling temporary container, decrypt and compare the full PCM stream, then atomically replace the original path with the verified encrypted container. Keep the original until verification succeeds; migration is repeatable per file after a crash.
- If a meeting already has one encrypted channel and one plaintext channel after an interrupted migration, recover the wrapped meeting key from the encrypted header and use it for the remaining channel. If no encrypted channel exists, generate one key for the meeting migration.
- If DPAPI cannot unprotect the session key, the user sees which meeting/channel failed and how to retry or recover their Windows profile. Do not skip the meeting, overwrite its file, or fall back to plaintext interpretation.
- If a frame is truncated at end-of-file, preserve the preceding authenticated frames and surface a truncation warning. A failed authentication tag in a complete frame is corruption and must be surfaced rather than silently dropped.
- Existing legacy audio remains readable until its encrypted replacement has been verified. Successful migration removes the plaintext source as part of the atomic replacement; no temporary plaintext audio files are written.

## Tests and verification

- Unit tests cover multi-frame round trips, frame random access, nonce separation between channels, malformed headers, tag tampering, truncated tails, and no plaintext bytes in the persisted container.
- Recorder and live-worker tests verify audio is written only in the encrypted format, non-overlapping windows reproduce the expected PCM, and no temporary WAV segment is left on disk.
- Migration tests cover plaintext conversion, idempotent restart with one channel already converted, verification failure preserving the source, and DPAPI-unprotect failure being visible.
- Manually record and stop a meeting, confirm live transcription and final fallback work, inspect both saved audio files for the container magic and absence of PCM, then open a prior plaintext meeting to verify migration and playback/transcription.

## Alternatives

- Encrypt the entire WAV with one DPAPI call: rejected because it buffers large recordings and cannot support the growing-file live reader.
- Use one large GCM operation for a complete recording: rejected because it delays authentication and recovery until the recording ends and requires buffering the full payload.
- Use unauthenticated AES-CTR through `CryptoStream`: rejected because modified ciphertext could produce modified audio without detection.

## Why

The recorder and live worker currently share growing WAV files, and the worker needs bounded, offset-based reads while capture continues. Small authenticated frames permit bounded encryption at the capture boundary, independent verification and crash recovery, and random access to live windows. A per-meeting DPAPI-wrapped key avoids persisting a raw key while keeping large audio data away from DPAPI. Four-kibibyte frames limit abrupt-stop loss to one incomplete tail frame per channel and keep memory bounded.

## References

- [DPAPI protection scope and roaming profiles](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
- [AES-GCM encryption, separate authentication tags, and nonce uniqueness](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt)
