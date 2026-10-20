# Meeting history search and organization

**Status:** Accepted

## Decision

Search the already decrypted meeting metadata in memory, using the MCP query's
trimmed, ordinal case-insensitive literal substring semantics across title,
summary, notes, actions, timestamped notes, transcript text and speaker names.
Share the matcher in Core so UI and MCP cannot silently disagree. UI results
rank title before summary, notes, actions and transcript, then newest first;
each result includes a bounded excerpt of its first matching field. Queries,
excerpts and search documents are never persisted or sent to a service.

The unfiltered library groups by local calendar month, newest first. Search
results stay ranked without date groups. Defer editable tags: this issue adds
retrieval of existing content, without inventing a tagging workflow or changing
the encrypted meeting schema. A tag workflow can be proposed separately.

Load and filter away from the UI thread; debounce typing and discard superseded
results. A protected-data load failure clears the library and search documents,
shows an explicit failure, and never becomes an apparently complete partial
result. Refresh after review edits and processing updates without dropping the
query. No plaintext on-disk search index is introduced.

## Alternatives

A persistent full-text index could scale further, but duplicates sensitive data
and introduces encryption, migration and synchronization work. Tokenized or
fuzzy queries would diverge from the existing MCP contract. Date groups within
search would obscure relevance ordering; tags require their own authoring UX.

## Why

Users need to recover a meeting from a remembered phrase while preserving the
local-only and DPAPI at-rest promises. In-memory snapshots reuse the existing
protected store and keep this change independently reviewable from model setup.
