# PERF-05 — the ancestor-stack cycle search is quadratic in depth, and eats most of R2's gain at depth 500

**Class: plan.** A performance finding and the proposal it leads to, for the owner to decide on; its status line says what became of it, and it defines no behavior.

**Status:** Open
**Raised:** 2026-09-27, while working rework stage R2 (ALLOC-09, SCALE-03)
**Touches the wire / the public surface / a security boundary:** no — the diagnostic and the rule of
Contract §16 stay as they are

## What was measured

R2 working tree, Publication job, mean ± 99.9% margin. SCALE-03 from
`Measurements/1a73c62-20260927T130725Z/` against `Baselines/pre-rework/results.csv`; ALLOC-09 from
`Measurements/1a73c62-20260927T161408Z/` (a suite added in R2, no baseline cell).

| Cell | pre-rework | R2 | ratio |
|---|---|---|---|
| SCALE-03 serialize, depth 1 | 674.7 ns | 380.6 ns | ×0.56 |
| SCALE-03 serialize, depth 25 | 5 471.5 ns | 3 461.4 ns | ×0.63 |
| SCALE-03 serialize, depth 100 | 21 644.2 ns | 15 049.5 ns | ×0.70 |
| SCALE-03 serialize, depth 500 | 128 267.1 ns | 120 483.3 ns | ×0.94 |

| ALLOC-09, same run | engine write (ancestor stack) | per-operation `HashSet` replica alone |
|---|---|---|
| depth 4 | 515.3 ± 2.0 ns, 376 B | 99.3 ± 2.2 ns, 368 B |
| depth 32 | 4 160.2 ± 24.7 ns, 1 816 B | 543.5 ± 3.9 ns, 1 488 B |
| depth 500 | 114 388.2 ± 228.5 ns, 24 280 B | 9 151.7 ± 97.2 ns, 34 416 B |

## What the numbers suggest

The gain of R2 shrinks steadily with depth and is nearly gone at 500. Each structural value searches
the whole path above it, so a chain of depth *d* costs about *d²/2* reference comparisons: ~125 000 at
depth 500, a few tens of microseconds, against the replica's 9 µs for the whole set. Below about
depth 32 the stack is cheaper than the set and allocates nothing of its own; above it the set wins on
time. The ALLOC-09 engine cell is the whole write, not the search alone, so the crossover depth is
an estimate.

## What is proposed

1. Keep the linear search up to a fixed path depth (for example 32) and, past it, index the path in a
   pooled `HashSet` by reference that is filled from the stack once and kept in step with pushes and
   pops. Ordinary graphs keep today's allocation; deep chains stop being quadratic.
2. Alternatively, accept the curve: the path is bounded by `MaxDepth` (512 by default), so the worst
   case is bounded, and deep chains are rare.

## What it would cost

(1) adds a second structure to `GraphState` and one branch per push; it must be returned to its pool
cleared, like the stack, so no object of one operation outlives it. (2) costs nothing now and leaves a
known curve in SCALE-03.

## What is not known

The search alone was not isolated from the write; a cell that measures only the push/search/pop over
a path of *d* would place the crossover exactly. R4 rewrites the engine and should read SCALE-03 again
before choosing.

## Outcome

Filled in by the repository owner.
