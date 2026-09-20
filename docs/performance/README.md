# Performance proposals

Where a measurement becomes a suggestion, and stops.

The benchmark effort (`../Benchmark-Plan.md`) measures the library and changes nothing in it. Everything it would otherwise want to change — an optimization, a new extension point, a behavior that looks wrong under a profiler, a limit that costs more than it seemed to — is written up here and left for the repository owner to decide.

A proposal is a document. It is not a patch, not a branch, and not a change already made and described afterwards.

## Files

One file per proposal:

```text
PERF-01-<slug>.md
PERF-02-<slug>.md
```

Numbers are never reused, and a rejected proposal stays in place with its outcome recorded. The index below is the list.

## Shape

```markdown
# PERF-nn — <one line: what is proposed>

**Status:** Open | Accepted | Rejected | Superseded by PERF-mm
**Raised:** <date>, while working <Benchmark-Plan.md item>
**Touches the wire / the public surface / a security boundary:** yes — <what> | no

## What was measured

The cells, with their margins of error, the profile and dataset each belongs to, and the raw
result file they come from. A proposal with no measurement behind it does not belong here.

## What the numbers suggest

The mechanism the measurement points at, and how confident the measurement makes that reading.

## What is proposed

The change, at the level of design rather than diff.

## What it would cost

Complexity, layering, the structural barriers, allocation, maintenance — and what would have to
stay true for the change to be safe.

## What is not known

What was not measured, and what would have to be measured before acting.

## Outcome

Filled in by the repository owner.
```

## Rules

- A proposal cites cells, not impressions.
- A proposal that would change the byte-level format, the public API or a security boundary says so in its first paragraph, because that decision is a different decision.
- A proposal never claims a speedup it has not measured on both sides.
- Nothing here is a result. The benchmark report links to open proposals as questions, never as findings.

## Index

*(none yet)*
