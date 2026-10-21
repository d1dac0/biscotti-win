# Local Markdown notes and transcript navigation

**Status:** Accepted

## Decision

For #87, render a deliberately small Markdown subset directly into WPF documents:
headings, unordered/ordered lists, quotes, fenced code, bold, italic, inline code,
and local timestamp links (`[label](biscotti://time/38.5)`, invariant seconds).
HTML, images, and other links stay inert text; no browser, resource loading, shell
execution, or remote content is involved. Unsupported syntax stays visible.
The source editor preserves the original Markdown exactly. Timestamp note text
is editable, while its numeric timestamp remains unchanged. No storage migration
is needed: the existing protected meeting format remains authoritative.

Edits are drafts until explicit Save or Ctrl+S, with visible saved/unsaved/error
feedback and a save/discard/cancel prompt on close. Timestamp activation selects
the containing transcript turn, otherwise the closest turn by start time, and
focuses it. An empty transcript produces feedback. Audio seeking remains #88.

## Alternatives

A browser-backed renderer introduces remote resource and navigation surfaces.
A full Markdown dependency is unnecessary for this bounded subset. Autosave
would change the existing explicit-save workflow and make recovery from edits
less predictable, so it is deferred.

## Why

Meeting content is untrusted and must stay on-device. Rendering only WPF text
and explicit local navigation keeps that boundary small. Keeping source and
timestamps intact avoids a destructive migration and preserves exports.
