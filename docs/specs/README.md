# Specs: decisions, not chat

One file per load-bearing decision. A file that survives sessions is the whole
point of this folder.

Each spec is short and follows one shape:

```markdown
# <Decision title>

**Status:** Proposed | In Progress | Accepted | Superseded | Assumed

## Decision
One paragraph: what was decided.

## Alternatives
Why not the runner-up.

## Why
The reasons that outlast this session.
```

- Linked from `PLAN.md` where the decision shaped the plan.
- `Superseded` specs stay, pointing at their replacement.
- `Assumed` specs are debts: revisit before the feature they touch is done.

Existing specs in this repo were backfilled to `Accepted` status from
already-merged work (issues #22, #20; see each spec's references).
