# PERF-03 — the write buffer's lifecycle costs a small blob write more than the stream it replaced

**Status:** Open
**Raised:** 2026-09-27, while working rework stage R1 (MICRO-01, rewritten in R1)
**Touches the wire / the public surface / a security boundary:** the security boundary, if acted on —
every proposal below keeps pooled payload bytes cleared before they go back to the pool
(`Rework-Plan.md` §5.1, ENC-16)

## What was measured

MICRO-01 on the R1 working tree against `Baselines/pre-rework/results.csv`, Publication job, mean ±
99.9% margin. R1 measurement: `Measurements/530262a-20260927T112242Z/` (after the two fixes below);
the first R1 measurement, before them, is `Measurements/530262a-20260927T092250Z/`.

| Cell | pre-rework | R1 | ratio |
|---|---|---|---|
| write blob, 8 B | 14.9 ± 0.1 ns | 24.2 ± 0.3 ns | ×1.62 |
| write blob, 256 B | 22.4 ± 0.1 ns | 31.7 ± 1.9 ns | ×1.41 |
| write blob, 4 KiB | 104.7 ± 0.3 ns | 121.9 ± 1.7 ns | ×1.16 |
| write blob, 64 KiB | 1 209 ± 3 ns | 2 160 ± 27 ns | ×1.79 |
| read blob, 64 KiB | 4 081 ± 11 ns | 4 486 ± 49 ns | ×1.10 |
| read `Decimal` | 35.6 ± 0.1 ns | 37.6 ± 0.3 ns | ×1.06 |

The multi-byte column of each blob cell moves the same way. Every other MICRO-01 cell is faster:
fixed-width primitives ×0.16–0.30, varints ×0.16–0.30 with no allocation left, strings ×0.45–1.05.

Two regressions of the first R1 measurement were fixed in R1 and are not open here: a blob grew the
buffer segment by segment from 512 bytes (64 KiB write ×2.69, now ×1.79), and every varint read built
its diagnostic label eagerly (72 bytes per call, now none; one-byte read ×1.09 → ×0.30).

## What the numbers suggest

The pre-rework cell wrote into a `MemoryStream` sized once in the benchmark's setup and rewound
between invocations, so it measured a copy and nothing else. The R1 cell measures what one
serialization does: rent a segment from the pool, copy, and on disposal clear the segment and return
it. For 64 KiB that is the same copy plus a clear of the same size, which accounts for the gap; for
8 and 256 bytes it is the fixed cost of one rent and one cleared return of the 512-byte first
segment. None of it is a copy the old path did not also pay somewhere: the old serializer grew its
own `MemoryStream` and called `ToArray`, which the old cell excluded. End to end, WL-01 on every
profile without compression or encryption is ×0.65–0.76 (first R1 measurement), so the cell is a
cost moved into view rather than a slower serializer.

The 64 KiB read and the `Decimal` read are small and not explained yet. The read copies into a new
array through `ReadExact`'s segment loop; `Decimal` now reads four integers after one availability
check instead of one 16-byte block.

## What is proposed

1. Clear only the committed part of each segment (`AsSpan(0, length).Clear()`) and return it with
   `clearArray: false`. The uncommitted tail never held payload bytes from this operation. ENC-16's
   source check, which looks for `clearArray: true` on every return, would be rewritten to accept an
   explicit clear of the used length.
2. Give the first segment a size a small value never outgrows, or keep one segment per serializer
   thread for small payloads, so a small write costs no rent.
3. Read `Decimal` as one 16-byte block, as before, when it is contiguous.

## What it would cost

(1) moves a security property from a pool flag to code that computes the length, so the length has
to be right on every path, exceptions included. (2) adds state that outlives an operation, which is
what the per-operation design avoids; a thread-static segment must still be cleared between
operations. (3) is local.

## What is not known

Whether R2, which moves the phases onto pooled buffers, changes the lifecycle enough to make this
moot; the cell should be read again at R2's measurement before acting.

## Outcome

Filled in by the repository owner.
