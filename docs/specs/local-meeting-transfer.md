# Explicit local meeting transfer

**Status:** Accepted

## Decision

Issue #83 adds a Windows CSV v1 format: one meeting per row, with columns
`Version,Id,StartedUtc,EndedUtc,Title,Summary,Notes,CalendarAttendees,ActionItems,Transcript,TimestampedNotes`.
UTC times use ISO 8601 round-trip formatting; collection cells contain JSON
using the public meeting model's property names. CSV quotes every cell and
escapes embedded quotes, commas and newlines. This is an independently authored
Windows format; Mac CSV interchange is not claimed. Audio is not transferred.

Import is a two-stage operation: parse/validate and preview counts without
writes, then explicitly import valid rows. Existing IDs (including duplicate
rows in the file) are skipped. Commit each new meeting through protected
staging and a directory rename that refuses an existing target; report partial
success on cancellation or storage failure. Staging directories start `.import-`
and are excluded from history, so an interrupted import cannot expose a partial
meeting. Only DPAPI-protected text is written inside meeting storage. Limit CSV
input to 32 MiB and 10,000 rows; reject malformed CSV before offering import.

CSV export takes a fresh snapshot of all saved meetings, independent of search.
Markdown export/copy uses the current review edits, with transfer controls
disabled during live capture/processing. File selection and an unencrypted-file
disclosure precede atomic export, and clipboard copy has its own disclosure.
Exports may not target internal meeting storage. Preview mode never reads or
writes real meeting storage or the clipboard.

## Alternatives

Copying protected internal files is tied to the originating Windows profile.
Silently producing plaintext beside those files breaks the at-rest promise.
A binary archive with audio would require a separate format and considerably
larger transfers; it is outside this issue. Source reuse from Mac is not
authorized, so this format and implementation are authored independently.

## Why

Users need a deliberate, reviewable way to move meeting text while internal
records remain protected. Stable IDs make repeat imports safe. Import preview
and cancellation should never overwrite a saved recording or hide a failed row.
