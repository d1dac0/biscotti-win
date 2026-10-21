# Local speaker memory

**Status:** Accepted

## Decision

#82 adds explicit corrections for each stable within-meeting speaker group and
an opt-in DPAPI CurrentUser-protected person/voiceprint file. Existing meetings
without group keys can still be corrected by their existing label, but cannot
enroll mixed or unknown groups. Speaker keys remain separate from display names.
The Speakers tab can analyze saved, encrypted system audio with the already
installed, pinned WeSpeaker model on CPU; it never initiates a model download.
Embeddings are transient unless the user explicitly remembers a named group.
At least two usable speech segments of two seconds each are required. Keep up
to five normalized segment vectors per enrollment; re-enrollment of that same
meeting/group replaces its earlier assignment, allowing mistakes to be corrected.

Recognition produces suggestions only: each segment must agree on one person,
with cosine similarity at least 0.94 and a runner-up margin of at least 0.08.
Exclude the source meeting from matching so self-matches are not evidence.
Conflicting or missing evidence leaves labels unchanged. These are conservative
engineering thresholds, not a calibrated probability or verified biometric ID.
The user must explicitly apply any suggested name. Transcription, introductions
and summaries never enroll people. Corrections update transcript labels, not
already generated summary prose. Historical corrections are preserved when a
person is forgotten. Forget removes all that person's stored vectors; forget
all removes the protected store and disables memory. No embeddings are added
to meeting files, logs, or settings. Disabling memory clears the store.

## Alternatives

Automatic naming was rejected until consented real-speaker evaluation establishes
false-accept behavior. Persisting per-meeting embeddings was rejected because it
would leave hidden copies after forgetting. Reusing text labels as identity keys
would combine different clusters with the same corrected name. Plaintext storage
would break the application's at-rest privacy promise.

## Why

Explicit confirmation contains the impact of false matches while still making
returning people easier to label. Stable group keys allow later corrections
without changing another person's turns. Synthetic vectors exercise ambiguity,
abstention and correction deterministically without GPU or private recordings;
they do not establish real-world recognition accuracy.
