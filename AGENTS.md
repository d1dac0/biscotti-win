# Biscotti Win — agent instructions

## GitHub identity (required)

- Use GitHub username **d1dac0** for every GitHub operation on this repository (repo creation
  expects it, CI minutes, Releases, issues, PRs). `gh auth status` must show d1dac0 as the
  active account; if it does not, switch with `gh auth switch -u d1dac0` before any gh/git push.
- Do not commit under an organization identity.

## Workflow rules

- One branch per issue, from origin/main. Branch name: `issue-<number>-<slug>`.
- Open a PR with `Fixes #<number>`, correct labels (exactly one `type:*`, one `priority:*`,
  relevant `area:*` — see `.github/labels.json`), then arm `gh pr merge <n> --squash --auto`.
  Merge only on green CI. Never force-push; update by merging main, never rebasing.
- Never commit to main directly. Never stage generated files or meeting data.
- Multiple agents work in parallel in this repo: at the start of every session run
  `gh pr list` and re-check open PR state before acting. On overlap with another PR,
  leave a coordination comment; do not close or force-merge another agent's PR.
- Changes close with verification: `dotnet build -c Release` and
  `dotnet test -c Release --no-build Biscotti.Tests` pass locally before PR; CI gate is the backup.

## Repository specifics

- The app must never upload user audio, transcripts, or models. All local: transcription
  (Whisper via Whisper.net) and summaries (Ollama) stay on-device. Keep it that way.
- Data lives under `%LOCALAPPDATA%\BiscottiWin` — never commit anything from there.
- Whisper model files and CUDA/CPU runtime packages are downloaded lazily, never vendored into git.
- Hardware context for performance work: the primary test machine has an RTX 3080 (16 GB).
