# Import, export and copy meeting text

In **Meeting history**, choose **Export all to CSV…** to export all saved
meetings, regardless of the current search. Confirm the unencrypted-file
disclosure and choose a local `.csv` destination. Audio is not included.
Cancelling file selection or cancelling before export commits leaves an
existing destination unchanged. A successful export explicitly says that it
is unencrypted. Import/export are unavailable until history loads successfully
and while recording or processing.

Choose **Import CSV…** and select a Biscotti Windows CSV v1 file. Review the
ready, duplicate and invalid-row counts. **Cancel** writes nothing; **Import
valid rows** adds only valid, new meeting IDs into protected local storage.
Invalid rows are listed by record number (the header is record 1; a quoted
multiline cell is still one record). Existing IDs and repeated valid IDs in
the input are skipped, never overwritten. While importing, **Cancel transfer**
stops before the next meeting; completed imports remain. A storage failure
stops the batch and reports the count already imported. Retry safely: existing
IDs will be skipped. No files referenced by the CSV are opened.

In a completed meeting, **Copy Markdown…** copies the current title, summary,
actions, notes and timestamped transcript after a clipboard disclosure.
Windows clipboard history/sync can retain this unencrypted text if enabled.
**Export Markdown…** writes the same current edits to a chosen `.md` file.
Neither action implicitly saves edits to the internal meeting record; use
**Save** for that. Transfer actions are disabled for live/processing meetings.

## CSV v1 format

The exact header is:

```csv
Version,Id,StartedUtc,EndedUtc,Title,Summary,Notes,CalendarAttendees,ActionItems,Transcript,TimestampedNotes
```

The exporter quotes every field with `"`, doubling embedded quotes. UTF-8,
commas, CRLF records and quoted multiline values are supported. A UTF-8 BOM
is optional. Version is `1`, Id is a nonempty GUID, Title is nonblank, and
StartedUtc is required. EndedUtc may be blank; otherwise it must follow the
start. Exported times use ISO 8601 UTC with seven fractional digits (`O` in
.NET). Import also accepts second/millisecond precision with `Z` or an explicit
offset such as `-05:00`. Times without offsets are rejected.

Summary and Notes are text. The last four cells are JSON arrays:

```json
["Alex", "Jordan"]
["Alex: send the checklist."]
[{"Speaker":"Alex","Text":"Ready.","StartSeconds":12.25,"EndSeconds":14}]
[{"Text":"Follow up","StartSeconds":12.5}]
```

Use `[]` for empty arrays. Transcript Text, Speaker and StartSeconds are
required; EndSeconds defaults to 0 (unknown), otherwise it must be at or after
StartSeconds. Notes require Text and StartSeconds. Seconds must be finite,
nonnegative and at most 1,000 hours. Null arrays/items and invalid JSON are
invalid rows. The importer accepts at most 32 MiB and 10,000 records; an invalid
header, broken CSV quoting, or either limit aborts preview without writes.
This Windows v1 format does not claim Mac CSV compatibility.

Treat CSV values as text when opening them in spreadsheet applications: meeting
content can begin with formula characters. Prefer importing the file as text.

## Protection boundary

Internal meeting files stay DPAPI-protected and audio stays encrypted. Imports
stage only protected files, then atomically publish a new directory. Incomplete
`.import-*` directories are hidden from history; they may remain after a crash.
Exports are deliberate plaintext copies outside protected meeting storage.
Folder links are rejected to prevent indirect export into that storage.
Keep your exported files in a folder you control and delete them when no longer
needed. There is no remote sharing action.
