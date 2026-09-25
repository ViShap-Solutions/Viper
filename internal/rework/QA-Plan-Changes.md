# QA-Plan.md — changes required

**Applies to:** `internal/QA-Plan.md` as of the NX-12 change.
**Two parts.** §1 is owed to the plan *now*, for the format as it exists: the NX fixes are pinned by
tests and recorded in §30.3, but the plan's own body does not carry checkpoints for them. §2 is owed
to the rework, stage by stage (`Rework-Plan.md` §12).

The method for working the plan stays in the `viper_tester` skill. IDs below continue each prefix
from its highest current number, so nothing is renumbered.

---

# 1. Owed now — the NX rules in the body of the plan

If the rework proceeds, §2 re-derives most of these sections anyway; §1 is still worth doing first,
because R1–R5 run against the current plan and must not lose a rule the plan never named.

| New ID | Section | Checkpoint | Contract | Already pinned by |
|---|---|---|---|---|
| HDR-20 | §11 | a declared expansion above `MaxDecompressionRatio` is `BinaryLimitException` while the header is read, and only when compression is not `None` | §5.10, §11 | `Algorithms/CompressionTests` |
| PM-17 | §15 | a non-public base member under `[BinaryInclude]` is in the derived type's plan and round-trips | §14.1 | `Contracts/InheritanceTests` |
| PM-18 | §15 | a member hidden by `new` is a second member; both travel, the base declaration first | §14.1, §22.3 | `Contracts/InheritanceTests` |
| PM-19 | §15 | an override is one member, and its own attributes apply | §14.1 | `Contracts/InheritanceTests` |
| PM-20 | §15 | the plan of a type is the same bytes however many times it is built | §14.1 | `Contracts/InheritanceTests` |
| KEY-19 | §16 | `[BinaryContract]` is inherited; a derived contract round-trips | §14.2 | `Contracts/InheritanceTests` |
| KEY-20 | §16 | a base reads a derived payload, skipping the derived key | §14.2 | `Contracts/InheritanceTests` |
| KEY-21 | §16 | a derived member with neither key nor ignore is `BinaryTypeException` naming it | §14.2 | `Contracts/InheritanceTests` |
| KEY-22 | §16 | a key the base already claims cannot be reused below it | §14.2 | `Contracts/InheritanceTests` |
| REF-18 | §18 | a second first-occurrence under a visible id is `BinaryFormatException` | §16 | `References/ReferenceFramingTests` |
| LIM-45 | §20.5 | `MaxDecompressionRatio` below / exact / above / invalid | §5.10 | `Algorithms/CompressionTests`, `Exceptions/ConfigurationValidationTests` |
| LIM-46 | §20.6 | the documented default table names exactly the limits the type declares | §5 | `Exceptions/ConfigurationValidationTests` |
| LIM-47 | §20.6 | a composite formatter is handed `CompositeReader`/`CompositeWriter`, which expose no raw integer; the engine exposes no payload primitives | §18, §24 | `Limits/StructuralBarrierTests` |
| HST-35 | §22 | a duplicate key in each of the six refusing dictionaries → `BinaryFormatException` preserving the `ArgumentException` | §8.2, §23 | `Hostile/DuplicateEntryTests` |
| HST-36 | §22 | a duplicate in a collapsing container (`ConcurrentDictionary`, sets, frozen, immutable sets) → `BinaryFormatException` | §23 | `Hostile/DuplicateEntryTests` |
| HST-37 | §22 | a null dictionary key → `BinaryFormatException` | §8.2 | `Hostile/DuplicateEntryTests` |
| HST-38 | §22 | a tiny frame declaring a huge expansion allocates nothing proportional | §12 | `Hostile/AllocationAmplificationTests` |
| HST-39 | §22 | a ratio-legal frame that produces nothing allocates nothing proportional | §12 | `Hostile/AllocationAmplificationTests` |
| CMP-14 | §23 | the ratio is the reader's policy: the same bytes pass one reader and fail a stricter one | §5.10 | `Algorithms/CompressionTests` |
| CMP-15 | §23 | both built-in algorithms decompress incrementally | §12 | `Algorithms/CompressionTests` |
| CMP-16 | §23 | a Brotli stream that yields the declared length but never terminates is malformed | §12 | `Algorithms/CompressionTests` |
| CHK-09 | §24 | a checksum reporting a size the header cannot record → `BinaryConfigurationException`, on write and on read | §8.1 | `Algorithms/ChecksumTests` |
| V0-26 | §13 | a byte-reversed magic is not recognised; `Peek` reports no header | §22 | `Format/RoutingTests` |

Also owed now:

- **LIM-40** text: "through `Validate`" → "through `Validate` or `ValidateShape`, called only from
  `ValueReader`, `ValueWriter` and the engine's composite surface".
- **CMP-16** records a behaviour change that is easy to miss: before NX-01, `TryDecompress` refused a
  Brotli stream that never reached its end marker even when it had produced the declared length. The
  incremental path first accepted it; the fix restored the refusal. It deserves its own line so a later
  rewrite of the decoder does not lose it again.
- §32 release gate: the test count line to the current number when the plan is next closed.

---

# 2. Owed to the rework, by stage

## R0 — Baseline lock

- New §0 "Rework oracle": the recorded bytes for every §23 family and graph shape, V0 and V1, with and
  without references. Rule: R1–R5 must reproduce them byte for byte; they are retired at R6.
- New checkpoint family `ORC-nn`, one per oracle file.

## R1 — Wire primitives on buffers

- §2 layout: `Streams/` is renamed for what it will pin after R2 (for example `Wire/`), not deleted.
- LIM-44 rewritten: payload bytes are reachable only through `WireReader`/`WireWriter`.
- LIM-40, LIM-47 rewritten for the new types. The rule text does not change.
- WF-* and HST-* stay untouched: they are the evidence that R1 changed nothing on the wire.

## R2 — Pipeline on pooled buffers

- §21 **rewritten.** STR-01…STR-28 pin rules that survive (budget versus truncation, origin-relative
  budget, high-water mark across patches, window over-read is malformed, bounded skipping, no
  materialisation). Each is re-expressed against the reader and writer; none is dropped.
- **V0-25 inverted:** a V0 keyed write to a non-seekable destination *succeeds*.
- **API-18** extended to keyed payloads.
- New: no `MemoryStream` under `Pipeline/` (source-shape test, like the existing barrier tests).

## R3 — Non-seekable reading, new entry points

- **API-17, OPT-18, V0-14 inverted:** a non-seekable source is read, not refused.
- New family `SRC-nn` — one checkpoint per source kind (span, sequence, multi-segment sequence,
  seekable stream, non-seekable stream, `PipeReader`) × (V1, V0 delimited), asserting identical values.
- New: a non-seekable double that fails on any read past the frame proves exactly one V1 frame is
  consumed.
- New: V0 from an undelimited non-seekable source in a mode that may read ahead →
  `NotSupportedException`.
- §31 cross-entry-point equivalence extended to every new entry point and to `PooledPayload`.
- New: async frame-edge — cancellation, a frame split across many pipe segments, a pipe completed
  mid-frame is `BinaryFormatException`.

## R4 — Typed engine

- New family `TYP-nn`: every §19 round-trip corpus case runs through the typed path and yields the
  same bytes as the R0 oracle.
- New: allocation assertions (`AssertEx.AllocatesLessThan`) on the §11 paths of `Rework-Plan.md` that R4
  makes reachable — primitive-record serialize into a writer, primitive-record deserialize from a span.
- LIM-47 and INV-5 tests extended: a typed shape has no loop over wire data; the engine owns it.
- New: polymorphic slots are the only boxed path (a counting test double, not a profiler).

## R5 — Algorithm contracts

- §23–§26 rewritten per `Rework-Plan.md` §8: one method per direction; every built-in, including the
  new ones, round-trips through the pipeline and resolves from the catalog.
- New: `Guarantee` drives `RequireEncryption`; `KeySize` is checked at `Build()`; ChaCha20Poly1305
  on a platform where it is unsupported → `BinaryFormatNotSupportedException`.
- New: `HkdfKeyProvider` derives distinct keys per key id and never exposes the root key.
- CMP-15 retired (incremental is the only mode); CMP-16 kept.

## R6 — Final wire format

- §14 wire format and §30.4 frozen fixtures **rewritten**. New fixtures committed from the corpus.
- New: every varint is minimal — a non-minimal encoding of the same value is `BinaryFormatException`
  (INV-9), for counts, lengths, ids, keys and header numbers.
- New: header extensions — unknown critical rejected, unknown non-critical skipped, every extension
  inside the associated data (flip a byte → `BinaryIntegrityException`).
- New: schema fingerprint mismatch → `BinaryTypeException`, if D-2 is accepted.
- HDR-* rewritten for the flag-driven header; absent fields are absent, not zero.
- ORC-* retired.

## R7 — V0 at parity

- §13 **rewritten** from `Rework-Plan.md` §7's table: one checkpoint per row.
- New: a plaintext V0 frame handed to a reader configured for AEAD fails its tag
  (`BinaryIntegrityException`) — the downgrade argument, pinned.
- New: an unconfigured V0 frame is byte-identical to the V1 payload of the same value (kept from today).
- V0 references: cycles, sharing and keyed-field scopes as in §18, now under V0.

## R8 — Generator ground

- New family `CONF-nn`, the conformance suite: for each object shape, the plan (order, keys, layout)
  and the bytes. Written so a generated plan can be substituted and run against the same cases.
- New: a consumer project built with AOT analysis reports the annotated reflection entry points and
  nothing unannotated.

## R9 — Re-gate

- §32 release gate rebuilt; test count, Debug and Release.
- New: `dotnet pack` succeeds for all three packages with non-empty READMEs.
