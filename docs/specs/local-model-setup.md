# Guided local model setup and summary preferences

**Status:** Accepted

## Decision

Issue #80 extends the existing setup card and Settings window. Whisper setup
shows the supported models, sizes, free space, progress and cancellation.
Ollama discovery uses only `http://localhost:11434/api/tags`, lists installed
GGUF models with nonzero local size, and excludes cloud/remote entries. The
same local-model gate runs before generation; missing summary prerequisites
leave the transcript usable. Ollama installation and model pulls remain
explicit user actions with instructions, rather than launching an installer
or pulling an arbitrary model. Download progress/cancel in Biscotti belongs
to Whisper and its optional CUDA runtime.

Settings persist summary length and at most 4,000 characters of extra guidance.
Guidance supplements every summary pass without replacing the parser's output
sections. Model/settings changes are saved only while idle. Runtime selection
requires restarting Biscotti, since Whisper's native runtime is process-wide;
an unavailable CUDA runtime continues to fall back to CPU. Model downloads
are explicit durable actions even if the user cancels the settings dialog.

## Alternatives

A separate onboarding wizard would duplicate the existing setup surface.
An editable Ollama server URL would weaken the on-device boundary. Arbitrary
prompt templates would break title/action/participant parsing. Live runtime
switching would misrepresent Whisper's native loader lifetime.

## Why

Users need to see why transcription or summaries are unavailable before a
meeting, recover from setup errors, and change the supported options without
editing JSON. Downloads need internet; recording, transcription and summaries
can run offline after the required local models and runtimes are installed.

API references: [installed models](https://docs.ollama.com/api/tags),
[local-only configuration](https://docs.ollama.com/faq#how-do-i-disable-ollamas-cloud-features).
