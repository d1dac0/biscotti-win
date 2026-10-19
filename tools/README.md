# Build and development (Windows)

The workflow in `AGENTS.md` assumes these stand alone, so agents never
reconstruct build steps from memory. All paths are relative to the repo root.

- `tools/build.ps1` - the one build command, runnable from a fresh agent
  session with no arguments. It restores if needed, then builds.
- `tools/check.ps1` - the one test/regression check command (restore + build +
  full test suite). Console output is the artifact; nothing extra is produced.
- `tools/bootstrap.ps1` - one-time repo setup: creates the workflow labels
  listed in `.github/labels.json` (idempotent).

Run them from any directory; they locate the repo root from their own path.
