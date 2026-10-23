# Deferred: gate step 7 is not machine-independent, and fails in both directions

**Status: notated for later work, not fixed here.** Raised 2026-10-23 while landing migration 006.

## What was found

CI run 13 (`c5bdbfc`) was red. The handoff's standing assumption was that CI failures are a ~33%
flake in the large simulation-image build/push step. **That is wrong for this run.** Read from the
job log in a logged-in browser session (the API token reaches `/api/v1` only, which has no step
detail), the failing step is:

> **Enforce Un-Excluded Coverage Ratchet** — 56s
> ```
> Measured: line 96.73%, branch 89.45%, 345 uncovered lines in 79 classes.
> Coverage ratchet FAILED: the un-excluded measurement regressed.
>   REGRESSED branch_rate: baseline 89.46 -> measured 89.45
> ```

Every step before it passed, including the full test run (608/608) and step 4 at 100.00%. The seven
container build and push steps never ran — they were skipped because step 7 had already failed. So
the image-flake theory did not apply to this run at all, and the storage-contention hypothesis on
`heimdall.lan` is unsupported by it. Runs 1–11 may still be that flake; run 13 is not.

## The actual defect

The regression is **0.01 of a percent in branch rate** — baseline 89.46, CI measured 89.45. The same
commit measured 89.46 locally. The number is not stable across machines or runs.

The probable cause is the already-known `ValkeyEventBridge.ExecuteAsync` race: it runs 169 or 170
times depending on timing, which moves branch coverage by a hair. P4 gave the skip path its own
deterministic test on the belief that the ratchet no longer depended on the race. **It still does.**

The design problem is worse than the one value, because `check-coverage-ratchet.py` fails closed in
**both** directions:

- measured below baseline → `Coverage ratchet FAILED: ... REGRESSED` → exit 1
- measured above baseline → `Baseline is stale. Re-run with --update ...` → exit 1

Only an exact match passes. A gate that demands an exact float match on a quantity that varies by
±0.01 between machines cannot stay green. The script's own comment in `verify-all.sh:163` states the
intent — "the ratchet must be a machine-independent number" — and the measurement is not one.

## Consequence for the commits landing now

Migration 006 raises the honest numbers to **96.75 / 89.57** as measured on the workstation, and the
committed baseline is updated to match (which is what the script instructs on an improvement). If
CI measures 89.56, **step 7 will fail again** — this time as a regression against the new baseline.
That would be the same defect, not a new one, and not a reason to revert 006.

## Options when this is picked up

1. Find and kill the stray host-level `ValkeyEventBridge` start, so the measurement is deterministic.
   This is the real fix and it closes the known open defect at the same time. Cost: unknown — the
   stray start has already resisted one search.
2. Give the rate comparison a tolerance (for example 0.05) while keeping the per-class
   uncovered-line counts exact. The per-class counts are integers and are the part that actually
   catches regressions; the two rates are derived summaries. Cheap, and it preserves the guarantee
   that matters.
3. Compare only the per-class uncovered-line map and drop the rate comparison entirely.

Option 2 is the smallest change that makes the gate honest about its own precision. Option 1 should
happen regardless, because the race is a real defect independent of the gate.

**Do not** resolve this by widening the baseline upward on each red run — that silently ratchets the
threshold in the wrong direction, which is exactly what the gate exists to prevent.
