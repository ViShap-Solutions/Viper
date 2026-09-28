# PERF-08 — The algorithm primitives after their port to the R5 interfaces

**Status:** Open
**Raised:** 2026-09-28, while working rework R5 (MICRO-10, ALLOC-12…ALLOC-14)
**Touches the wire / the public surface / a security boundary:** no

## What was measured

`Measurements/c215131-20260928T131001Z` against `Baselines/pre-rework/`, BASE-02
(`comparison-pre-rework.md` in the run). Same machine, same runtime, same power plan — the two
manifests agree on every environment line. Suites: `AlgorithmBenchmarks`,
`CompressionPrimitiveBenchmarks`, `ProtectionPrimitiveBenchmarks`, `AllocationTargetBenchmarks`,
`EnvelopeDifferentialBenchmarks`, `ProfileBufferBenchmarks`.

**Through the pipeline — what a consumer pays — nothing is slower.** All 70 `AlgorithmBenchmarks`
cells matched with `pre-rework` (`phase write` and `phase read` under B-P3d, B-P3b, B-P5, B-P6b,
B-P6d, and the rest) are faster, by ×0.05 to ×0.88. That is the whole rework to date, not R5 alone:
no stage before R5 measured this suite.

**Outside the pipeline — MICRO-10 — 28 cells are slower**, none faster beyond error:

| Primitive | Cells slower | Ratio | Allocation, B/op (pre-rework → R5) |
|---|---|---|---|
| `Crc32Checksum` compute | 3 of 3 | ×1.05–×1.10 | 0 → 0 |
| `Aes256GcmEncryption` encrypt / decrypt | 5 of 6 | ×1.01–×1.15 | 40–72 → 40–72 |
| `BrotliCompression` compress | 6 of 6 | ×1.04–×1.17 | 0 → 32 |
| `BrotliCompression` decompress | 6 of 6 | ×1.09–×1.22 | 0 → 32 |
| `DeflateCompression` compress | 5 of 6 | ×1.01–×1.06 | 544–2 089 598 → 232 |
| `DeflateCompression` decompress | 3 of 6 | ×1.03–×1.57 (1 MB compressible) | 424–6 704 → 288 |

**Allocation targets:** ALLOC-12 (Brotli → `IBufferWriter`) 296 → 352 B, worse; ALLOC-13 (Deflate)
840 → 552 B, better; ALLOC-14 (AES-256-GCM) 432 → 432 B, unchanged.

## What the numbers suggest

- **A control says part of it is the run.** `Crc32Checksum.Compute` is the same call as before the
  rename — `System.IO.Hashing.Crc32.Hash(source, destination)`, nothing around it — and it is 5–10 %
  slower; AES-GCM encrypt and decrypt run the same BCL calls and are up to 15 % slower. Code that did
  not change moved by that much, so a cell within ×1.15 of `pre-rework` is not, by itself, evidence
  against R5. The run lasted two hours and its soak took 40 minutes of wall time for 10 minutes of
  work, which points at a machine that was not idle throughout.
- **Brotli, beyond the control.** The primitive no longer calls the one-shot `BrotliEncoder.TryCompress`
  / `BrotliDecoder.TryDecompress`; it drives a `BrotliEncoder`/`BrotliDecoder` instance through the
  buffer writer in 16 KiB steps, because the interface writes into an `IBufferWriter<byte>` of unknown
  size. The 32 B per call and the extra native calls per step are the likely cost. Measured through
  the pipeline, Brotli is still faster than `pre-rework` in every cell.
- **Deflate decompress, 1 MB compressible, ×1.57** — the one cell far beyond the control. The primitive
  copies the compressed input into a pooled array (a `Stream` cannot wrap a span) and reads in steps
  bounded by the writer's span. Which of the two costs the 22 µs is not established.
- **ALLOC-12, +56 B.** Compression now writes into a `CompressionBuffer` — an object, because it is
  handed to the algorithm as an `IBufferWriter<byte>` — where it used to write into a rented array
  directly.

## What is proposed

1. **Re-measure MICRO-10 alone on an idle machine** (`--track A --filter '*PrimitiveBenchmarks'`), with
   `pre-rework` and this run beside it. What survives the re-run is the port's cost; the rest is the
   run. Nothing below is worth doing before that.
2. **Brotli: one call when the destination can take it.** Ask the writer for
   `BrotliEncoder.GetMaxCompressedLength(source.Length)` when that fits under the ceiling the writer
   reports, and compress in one `TryCompress`; fall back to the stepped loop otherwise. Decompression
   likewise: one `TryDecompress` into a span of `expectedLength` once the writer has grown to it. The
   bytes must stay identical — the fixtures and the oracle decide.
3. **Deflate decompress: profile the 1 MB cell** before changing anything — the input copy against the
   step size.
4. **ALLOC-12: reuse the `CompressionBuffer` object** per thread, the way PERF-07 proposes for
   `PayloadBuffer`; the two proposals are one mechanism.

## What it would cost

- (2) keeps two code paths per codec; the exact-bytes requirement makes it testable, not free.
- (4) is a thread-static cache: bounded, cleared on return, invisible to an algorithm.
- None of it touches the wire, the public surface or a limit.

## Decision

Pending — for the owner.
