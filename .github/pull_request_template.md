## Change

Describe the bug or user story and the resulting behavior.

## Related Issue

Closes #<!-- issue number, or state that no issue exists -->

## Acceptance Criteria

- [ ] <!-- Observable outcome from the issue -->

## Validation

List commands run and their results (`dotnet build -c Release`, test meetings recorded, transcripts inspected).
Describe any checks that could not be run; do not claim a flow passed without running it.

## Screenshots

Include before/after screenshots for UI changes, or mark not applicable. Never attach audio of real conversations.

## Configuration & Data Impacts

Describe model downloads, settings migration, or stored-data impacts under `%LOCALAPPDATA%\BiscottiWin`, or mark none.
Never include secrets.

## Agent Merge

- [ ] PR has exactly one `type:*`, one `priority:*`, and relevant `area:*` labels.

The owner authorizes the agent to merge completed PRs without another confirmation:
- [ ] Work is complete and this PR is ready for review.
- [ ] Acceptance criteria and relevant local validation pass.
- [ ] All configured checks and required reviews pass, or no checks/reviews are configured.
- [ ] Agent merges the verified PR head using squash merge and confirms the result.

Do not bypass failed checks, required reviews, or merge conflicts.
