# Working on this repo

Biscotti Win — fully local meeting transcription and summarization (Whisper.net
+ Ollama, dual-channel WASAPI capture), a Windows x64 WPF app. User audio,
transcripts, and models never leave the machine; data lives under
`%LOCALAPPDATA%\BiscottiWin` and is never committed. The plan of record is
`PLAN.md`, load-bearing decisions live in `docs/specs/`, and work is verified
with `tools/build.ps1` and `tools/check.ps1`.

Several agent sessions work on the repo at once. Each one takes its own issue,
works on its own branch in its own git worktree, and lands it on `main` through
a pull request (see Git). Never work directly on `main`.

## The loop: one issue per turn, then stop

1. **Check findings first.** On every start and every "continue", list the open
   bugs and the open pull requests:
   ```bash
   gh pr list
   gh issue list --label type:bug
   ```
   Bugs come before stories. Read the whole issue (`gh issue view N --comments`).
   With no bugs, take the next open `type:story` of the current phase in
   `PLAN.md` (or a `type:task` the user pointed at).
   **Skip issues another session holds:** assigned, labelled `in-progress`, or
   with an open pull request. **Never auto-take `type:audit` issues** (the
   standing ones are filed in `PLAN.md` under "On-request audits") — the human
   gives the go-ahead explicitly.
2. **Claim it and branch.** Assign yourself, post a short claim comment, and
   add `in-progress` so other sessions skip it:
   ```bash
   gh api repos/d1dac0/biscotti-win/issues/<n>/assignees -f assignees[]=d1dac0 --method POST
   gh issue edit <n> --add-label in-progress
   ```
   Then work in a worktree on a fresh branch (see Git). Claiming happens before
   the branch. If another agent already claimed it, coordinate in the issue
   instead of starting parallel work.
3. **Plan the change** before writing code. Small tasks: a short list of steps
   is enough. Larger tasks: write the plan down, then proceed within the
   user's authorized scope without a separate plan-approval step.
   A load-bearing decision is written in `docs/specs/`
   before building on it.
4. **Write the code** in small, focused commits.
5. **Verify it before closing:** run `tools/build.ps1` and `tools/check.ps1`,
   plus any regression check below. Inspect actual output, not just exit codes.
6. **Commit, push and open a pull request** with `Fixes #N`, one `type:*`, one
   `priority:*`, relevant `area:*` labels (see `.github/labels.json`), then
   merge on green CI.
7. **Stop and hand over.** End the turn with:
   - first line: what changed, the issue, the PR link, the merged commit hash;
   - how to try it: the exact command and what to look for;
   - **Next:** the issue you'd take after the user's OK.

   Do not start the next issue in the same turn. Wait for "continue", "go"
   or "looks good". If the user reports a problem, fix that before anything
   else. A fix to a merged issue is a new issue, branch and pull request from
   the latest `origin/main`.

## How to work with the user

- **The user decides; the agent recommends.** Every question carries exactly
  one recommended option with a one-line why, so the user can make the call or
  override. Never a neutral listing, never a silent decision.
- **Suggestions, not gates.** Extended checks, reviews, and doc steps are
  offered for each change; the user picks. A skipped step is recorded honestly
  as skipped, not silently dropped.
- **Keep state in files, not chat.** Plans live in `PLAN.md`, decisions in
  `docs/specs/`, findings in issues. Anything a next session needs must be
  written down — a session can end at any point.
- **Lead with the action.** Reports start with what changed or what to do;
  detail lives in the linked file or PR. Ask one question at a time.

## Git

One issue, one branch, one worktree:

```bash
git worktree add .worktrees/issue-N -b issue-N-slug
```

- Branches come from `origin/main`; name: `issue-<number>-<slug>`.
- Detect existing isolation first: if `git rev-parse --git-dir` and
  `git rev-parse --git-common-dir` differ, this checkout IS a linked worktree —
  use it, create nothing. (OpenCode: create the worktree with
  `tools.opencode.worktree_create`, then `tools.opencode.session_move` into it.)
- `.worktrees/` is git-ignored; its contents can never be staged by accident.
- Run `tools/build.ps1` + `tools/check.ps1` after creating a worktree to get a
  clean baseline before changing code. A failing baseline is recorded, not
  chased; judge later failures against it.
- Update by merging `origin/main` into the branch. **No rebase, no force
  pushes.**
- Land with `gh pr merge <number> --squash --auto --delete-branch`; merge only
  on green CI.
- Commit messages: short, plain-English summaries of the behavior changed.
- Never stage everything blindly. Name explicit file paths; never `git add -A`
  or `git add .`. Never commit generated files, build outputs, or meeting data.
- Multiple agents in parallel: at the start of every session run `gh pr list`
  and re-check open PR state before acting. On overlap with another PR, leave a
  coordination comment; never close or force-merge another agent's PR.

## Labels

- Every PR: exactly one `type:*`, one `priority:*`, relevant `area:*`(s).
- `in-progress` marks a claimed issue — other sessions skip it.
- `blocked-answer` means the issue waits on someone outside the repo.
- `spike` is a timeboxed investigation to pick between approaches; its finding
  is written to `docs/specs/` (or an issue comment) and links there.

## Verification

- Build: `tools/build.ps1` (i.e. `dotnet build -c Release BiscottiWin.slnx`).
- Check: `tools/check.ps1` (restore + build + the full test suite). Keep the
  suite green; GPU-path tests must stay hardware-free (the CI runner has no
  NVIDIA GPU), and new behavior rides with test additions in the same PR.
- A change is not done until the build, the check, and a manual look at the
  result pass (launch the app for UI-affecting changes).

## Data and configuration

- The app must never upload user audio, transcripts, summaries, or models —
  transcription and summarization stay on-device. This is the product's core
  promise; keep it that way.
- Data lives under `%LOCALAPPDATA%\BiscottiWin`; never commit anything from
  there, and keep local paths out of committed files.
- Whisper model files and GPU/CPU runtime packages download lazily at runtime —
  never vendored into git.
- Hardware context for performance work: the primary test machine has an
  RTX 3080 (16 GB); measured numbers go to
  `docs/transcription-performance-matrix.md`.

## Decisions

Load-bearing decisions are written in `docs/specs/`, one file per decision:
what was decided, the alternatives, why. Write it before building on it — see
the shape in `docs/specs/README.md`. Overturning one writes a new spec and
marks the old one `Superseded`. Building before deciding writes it as `Assumed`
and expects it revisited before done. The reasoning lives in the file, not the
chat.

## GitHub identity (required)

Use GitHub username **d1dac0** for every GitHub operation on this repository
(issues, labels, PRs, releases, CI minutes). `gh auth status` must show d1dac0
as the active account; if not, switch with `gh auth switch -u d1dac0` before
any gh/git push. Do not commit under an organization identity. In scripts and
one-off commands carry the repo explicitly (`-R d1dac0/biscotti-win`) — don't
rely on the working directory deciding which repo answers.

## Shared memory across sessions

This project keeps a local, user-owned Engram graph in `.engram/`
(git-ignored): durable reasoning — decisions, principles, cautions,
problems/resolutions, open intents. Not a code map. It is how sessions learn
what the others found; git/issues say what changed, Engram says why and what to
watch out for. `.engram/` does not exist yet in this repo, so this section
doesn't apply until it does — set it up with `engram-alpha serve` in the repo
root.

- **Recall.** Read the project brief at session start (an injected
  "# Engram brief", else call `brief` once). If the brief names a graph other
  than this workspace, rebind: call `brief` with this workspace's ABSOLUTE
  path. In a worktree, always bind by the project's name before reading or
  writing memory — writes made while bound by fallback land in the wrong
  graph. Before any non-trivial work, `search` the graph, and read each hit's
  context before concluding it is silent.
- **Capture.** Sparing and durable only: key decisions with their reasons,
  principles, hard-won cautions, problems with their resolutions. Title = the
  claim, body = the why in 1–3 sentences. A dozen good nodes beat fifty
  mirrored doc lines; when in doubt, don't write. Every write reply is a
  verdict: `matched` → update that node; `suspects` → judge each (conflict,
  replaces, dismiss) and surface genuine contradictions to the user. Write
  silently at natural stopping points, never store secrets.
- **Maintain.** Judge the brief's suspects first; merge duplicates; verify
  stale hits before re-adding. The pane is at `http://127.0.0.1:8787` (real
  port in `.engram/daemon.json`); `engram-alpha serve` in the repo root starts
  the core and exits. If the MCP tools disconnect, don't drop captures:
  `POST /nodes` on that port with `"source": "claude"`, or ask the user to
  reconnect.
