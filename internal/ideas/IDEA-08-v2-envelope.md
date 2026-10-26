# IDEA-08 — Per-field compression and a V2 envelope

**Class: plan.** **Status:** idea. Recorded, not scheduled, in `rc2/RC2-Plan.md` §2.

## What and why

Compression applies to the whole payload. A frame with one large text field and many small numbers
compresses the numbers for nothing, and a reader that needs one field decompresses all. Per-field
compression, or a V2 envelope with chunked phases, would let large members be compressed (and later
streamed) on their own.

## Where it was written

- `Audit-Future.md` §7.

## What is already fixed

- Adding a format version is a pipeline registered in `BinarySerializer`; the router picks it up;
  formatters, engine, algorithms and limits are untouched (`CLAUDE.md`, "Versioned envelope").
- Decompression is bounded by the declared length (`MaxCompressedBytes`, contract §5.10, §12). Every
  compressed field would be a new decompression site, so a new bomb surface to bound.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | `[BinaryCompress]` on a keyed member: the field body is compressed inside its length | Payload-level, works under V0 and V1 | Each field's decompressed size declared and budgeted; a new member attribute frozen |
| b | V2 envelope: phases applied per chunk with a chunk table in the header | Streaming-friendly, partial reads | A new wire format; header bound and chunk budget to design |
| c | Do nothing; consumers split large blobs out of band | No surface | Whole-payload compression only |

Recommended when taken up: measure first — a corpus where per-field compression wins by a margin worth a
new rule.

## Open questions

1. Is there a workload where whole-payload compression is measurably the wrong unit?

## Version impact

| Form | Version |
|---|---|
| a, opt-in by attribute | **minor** |
| b, written only when asked (`WithVersion(2)`), V1 still the default write | **minor** |
| b as the default write version | **major** (§5.2: "the version written by default changed") |

## Preconditions

- A benchmark corpus that motivates it (`Benchmark-Plan.md`), written up as a `PERF-nn`.
