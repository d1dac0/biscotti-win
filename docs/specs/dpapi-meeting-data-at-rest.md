# DPAPI protection for meeting text files

**Status:** Accepted

## Decision

The application protects each meeting's `meeting.json` and `meeting.md` with Windows DPAPI using `ProtectedData.Protect` / `Unprotect` and `DataProtectionScope.CurrentUser`. The files keep their existing names. Each protected file starts with the ASCII bytes `BISCOTTI-DPAPI:1` followed by one LF byte (`0x0A`), then the DPAPI blob. Bytes after the marker are never interpreted as plaintext unless `Unprotect` succeeds. A file without the marker is a legacy plaintext file and is eligible for migration. Audio is protected separately under the accepted [encrypted audio streams decision](encrypted-audio-streams.md), implemented in issue #45.

## File handling and migration

- New JSON and Markdown contents are serialized/rendered as UTF-8, protected for the current user, prefixed with the versioned envelope marker, written to a sibling temporary file, and atomically renamed over the destination. JSON and Markdown are protected independently so a crash during a two-file save leaves each file's format detectable.
- At startup, the migration scans existing meeting directories. A file with the marker is unprotected and never retried as plaintext. A file without it is treated as legacy plaintext; JSON must deserialize successfully before it can be migrated. Existing Markdown bytes are protected as-is to preserve any user edits.
- Migration writes a protected temporary file, unprotects it and verifies the bytes, then atomically replaces that one source file. It is safe to rerun: after a crash, each file's marker tells the next run whether that individual file still needs migration.
- If DPAPI protection/unprotection, validation, or replacement fails, preserve the original file, report the affected meeting and operation, and do not silently skip it, overwrite it, or treat a marked-but-unreadable blob as plaintext. The UI must surface the error and allow retry or manual recovery. No migration step deletes the only readable copy.
- Settings and model files are outside this decision. Audio is explicitly outside Phase 1.

## Alternatives

- Encrypt only `meeting.json`: rejected because `meeting.md` duplicates the transcript and summaries in readable plaintext.
- Encrypt WAV files with one `ProtectedData` call: rejected because recording writes and reads growing WAV files; buffering whole recordings for DPAPI would add large memory use and interfere with live transcription.
- Rename files to an `.enc` extension: rejected because a versioned marker preserves the established meeting file paths and identifies the format in place.

## Why

The structured JSON and Markdown export are the small, discrete text files that contain searchable meeting content. Current-user DPAPI protects those files from direct reading by other local accounts and from offline inspection of copied files, while retaining atomic replacement and the existing storage layout. It does not protect against software already running as the same Windows user: such software can request DPAPI decryption too. Audio protection is specified separately and does not change the format decision for meeting text.

DPAPI is tied to the user's Windows credentials and normally to the machine. Microsoft documents an exception for roaming profiles, which may allow decryption on another machine carrying that profile. An administrator password reset can make previously protected data unavailable when the relevant DPAPI recovery material is unavailable; this must be treated as a visible data-access error, not an empty or corrupt meeting. The app must not promise cross-machine recovery.

## Related decision: WAV streaming protection (#45)

Issue #45 implemented the separate audio decision in [encrypted-audio-streams.md](encrypted-audio-streams.md). That decision specifies authenticated AES-GCM framing, DPAPI-wrapped meeting keys, crash recovery, migration, and the decrypting WAV-compatible reader used by recording and transcription.

## References

- [Microsoft: CryptProtectData and roaming profiles](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
- [Microsoft: DPAPI backup keys and password resets](https://learn.microsoft.com/en-us/windows/win32/seccng/cng-dpapi-backup-keys-on-ad-domain-controllers)
- [Microsoft: DataProtectionScope](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope)
- [Microsoft: AesGcm.Encrypt](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt)
