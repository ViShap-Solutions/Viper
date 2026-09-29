# PERF-04 — the pooled write path of R2 is slower on a large unphased blob and on some compressed cells

**Class: plan.** A performance finding and the proposal it leads to, for the owner to decide on; its status line says what became of it, and it defines no behavior.

**Status:** Open
**Raised:** 2026-09-27, while working rework stage R2 (ALLOC-02, ALLOC-03, ALLOC-06, MICRO-02,
MICRO-03, the profile matrix)
**Touches the wire / the public surface / a security boundary:** the security boundary, if acted on —
every proposal below keeps pooled payload, plaintext and ciphertext cleared before it goes back to a
pool (`Rework-Plan.md` §5.1, INV-11)

## What was measured

The R2 working tree against `Baselines/pre-rework/results.csv`, Publication job, mean ± 99.9% margin.
R2 measurement: `Measurements/1a73c62-20260927T130725Z/` (13 suites, filter in its `scope.md`).
A cell is listed when it is slower beyond both margins and by more than 5%; 10 of the ~480 compared
cells are. Everywhere else the stage is faster: WL-01 median ×0.65, WL-04 ×0.72, WL-02 ×0.74,
WL-05 ×0.77, DIFF-01 ×0.65–0.94, MICRO-07 ×0.34–0.97, MICRO-08 ×0.44–0.94, SCALE-02 ×0.55–0.86,
SCALE-03 ×0.46–0.94, and allocation falls in nearly every phased cell (WL-01 Brotli RecordBatchLarge
17.6 MB → 8.7 MB).

| Cell | pre-rework | R2 | ratio | allocated |
|---|---|---|---|---|
| A3 phase write, Default, DATA-08 Incompressible | 24.14 ± 0.09 ms | 27.83 ± 0.52 ms | ×1.15 | 26.4 → 22.8 MB |
| WL-01, Brotli, DATA-01 TinyFlat | 13.38 ± 0.05 µs | 14.30 ± 0.06 µs | ×1.07 | 2 336 → 936 B |
| WL-01, Brotli, DATA-04 RecordBatchLarge | 39.72 ± 0.15 ms | 42.55 ± 0.33 ms | ×1.07 | 17.6 → 8.7 MB |
| WL-04, ProtectedDeflate, DATA-05 DictionaryHeavy | 10.13 ± 0.03 ms | 10.65 ± 0.07 ms | ×1.05 | 8.9 → 4.6 MB |
| WL-05, Deflate, DATA-05 DictionaryHeavy | 10.08 ± 0.03 ms | 10.67 ± 0.09 ms | ×1.06 | 8.5 → 4.6 MB |
| MICRO-02 charge graph node | 0.9 ns | 1.0 ns | ×1.10 | 0 |
| MICRO-03 enter and exit, depth 1 / 8 | 2.0 ns | 2.1 ns | ×1.06 | 0 |
| MICRO-03 descend and return, depth 1 | 6.7 ± 0.1 ns | 7.1 ± 0.0 ns | ×1.07 | 0 |
| MICRO-03 descend and unwind, depth 8 | 3 663 ± 7 ns | 3 864 ± 11 ns | ×1.05 | 784 B |

The same A3 dataset without the regression, same run: Crc32 phase write 18.7 ms (pre-rework 24.1),
Aes256Gcm 19.8 ms (25.2), Brotli 21.0 ms (26.3).

## What the numbers suggest

**The unphased 22 MB write.** With no phase, V1 keeps the payload as the engine wrote it — a chain of
`PayloadBuffer` segments growing to 1 MiB each — and copies it into the returned array while every
segment is still held; the segments are cleared and returned only when the frame is disposed. With
any phase, the payload is copied once into a single rented array and the segments are released
before the phase runs. The phased path therefore does more work and is 9 ms faster on the same
payload, which points at the segments rather than the copy: about twenty 1 MiB arrays per operation
that the shared pool does not keep, so they are allocated on the large object heap each time and
cleared on return, alive together with the 22 MB result. The allocation column (22.8 MB, about the
result alone) says the segments are not charged to the operation as allocation, so this reading is a
hypothesis about pool retention and LOH clearing, not a measured fact.

**The Brotli and Deflate cells.** Each moved from a copy (`ToArray`) to a pooled buffer that is
cleared on return. `clearArray: true` clears the whole rented array — for a compression destination
that is `GetMaxCompressedLength` rounded up to the pool's bucket, not what was written — and the
payload is now linearised into one more rented array before the phase. At 14 µs, 0.9 µs is the size
of a few such clears; at 10 ms it is within what a clear of the decompression buffer would cost.

**MICRO-02 and MICRO-03.** R2 did not touch `SerializationBudget`, `ElementCount` or the depth scope.
The differences are a tenth of a nanosecond on 1–7 ns cells, and the same code measured ×0.02–0.07
of the baseline on the other cells of the same suites; they are recorded because they crossed the
threshold, not because a mechanism explains them. Their suites are rewritten in R4, where the budget
moves into `OperationState`.

## What is proposed

1. Clear only the committed part of a pooled buffer and return it with `clearArray: false` — the same
   change PERF-03 proposes for `PayloadBuffer`, extended to `RentedBytes`, whose length is known. A
   compression or encryption destination would then be cleared over what the algorithm wrote, not
   over its worst-case bound.
2. Release the payload segments as they are copied into a `byte[]` or a stream, or cap segment growth
   below the size the shared pool retains, so a large unphased write does not hold twenty
   LOH-sized segments beside its result.
3. Measure (2) first with a variant that keeps one retained segment per size class, before choosing.

## What it would cost

(1) moves a security property from a pool flag to code that computes a length; the length has to be
right on every path, exceptions included, and ENC-16's source check would be rewritten to accept an
explicit clear. (2) touches only `EncodedFrame` and `PayloadBuffer`, but a frame that releases its
payload while copying can no longer be copied twice, which nothing does today. (3) is measurement
only.

## What is not known

Whether the 22 MB cell is pool retention, LOH allocation or clearing: an allocation profile of that
cell alone would say. Whether PERF-03's proposal, taken first, removes the Brotli and Deflate
differences as well. The cells are read again at R4, whose engine changes the allocation of every
graph.

## Outcome

Filled in by the repository owner.
