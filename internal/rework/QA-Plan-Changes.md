# QA-Plan.md — changes required

**Applies to:** `internal/QA-Plan.md` as of 2026-09-26 (after `HST-40` and `KEY-23`).
**Two parts.** §1 is owed to the plan now, for the format as it exists. §2 is owed to the rework,
stage by stage (`Rework-Plan.md` §12), and is applied in the stage together with its code.

The method for working the plan stays in the `viper_tester` skill. Every new ID continues its prefix
from the highest number in use **or reserved here**, so nothing is renumbered:

```text
highest in QA-Plan.md    HDR-19 CTR-26 PM-16 KEY-23 REF-17 CYC-10 LIM-44 STR-28 HST-40 CMP-13 CHK-08
                         ENC-23 CAT-09 V0-25 API-20 OPT-21 WF-30 XEP-07 EXT-05 UTIL-16 CMPT-12
reserved by §1           HDR-20 CTR-27…30 KEY-19…22 REF-18 LIM-45…47 HST-35…39 CMP-14…16 CHK-09 V0-26
new families             ORC (R0), SRC (R3), TYP (R4), ALC (R4), CONF (R8)
```

Plan sections are cited as "plan §n" (`Rework-Plan.md`); contract sections as "§n".

---

# 1. Owed now — the NX rules in the body of the plan

The NX fixes are pinned by tests and recorded in §30.3, but the body carries no checkpoints for them.
R1–R5 run against the current plan and must not lose a rule the plan never named, so this part is
done before R1.

| New ID | Section | Checkpoint | Contract | Already pinned by |
|---|---|---|---|---|
| HDR-20 | §11 | a declared expansion above `MaxDecompressionRatio` is `BinaryLimitException` while the header is read, and only when compression is not `None` | §5.10, §11 | `Algorithms/CompressionTests` |
| CTR-27 | §15 | a non-public base member under `[BinaryInclude]` is in the derived type's plan and round-trips | §14.1 | `Contracts/InheritanceTests` |
| CTR-28 | §15 | a member hidden by `new` is a second member; both travel, the base declaration first | §14.1, §22.3 | `Contracts/InheritanceTests` |
| CTR-29 | §15 | an override is one member, and its own attributes apply | §14.1 | `Contracts/InheritanceTests` |
| CTR-30 | §15 | the plan of a type is the same however many times it is built | §14.1 | `Contracts/InheritanceTests` |
| KEY-19 | §16 | `[BinaryContract]` is inherited; a derived contract round-trips | §14.2 | `Contracts/InheritanceTests` |
| KEY-20 | §16 | a base reads a derived payload, skipping the derived key | §14.2 | `Contracts/InheritanceTests` |
| KEY-21 | §16 | a derived member with neither key nor ignore is `BinaryTypeException` naming it | §14.2 | `Contracts/InheritanceTests` |
| KEY-22 | §16 | a key the base already claims cannot be reused below it | §14.2 | `Contracts/InheritanceTests` |
| REF-18 | §18 | a second first occurrence under a visible id is `BinaryFormatException` | §16 | `References/ReferenceFramingTests` |
| LIM-45 | §20.5 | `MaxDecompressionRatio` below / exact / above / invalid | §5.10 | `Algorithms/CompressionTests`, `Exceptions/ConfigurationValidationTests` |
| LIM-46 | §20.6 | the documented default table names exactly the limits the type declares | §5 | `Exceptions/ConfigurationValidationTests` |
| LIM-47 | §20.6 | a composite formatter is handed `CompositeReader`/`CompositeWriter`, which expose no raw integer; the engine exposes no payload primitives | §18, §24 | `Limits/StructuralBarrierTests` |
| HST-35 | §22.1 | a duplicate key in each of the six refusing dictionaries → `BinaryFormatException` preserving the `ArgumentException` | §8.2, §23 | `Hostile/DuplicateEntryTests` |
| HST-36 | §22.1 | a duplicate in a collapsing container (`ConcurrentDictionary`, sets, frozen and immutable sets) → `BinaryFormatException` | §23 | `Hostile/DuplicateEntryTests` |
| HST-37 | §22.1 | a null dictionary key → `BinaryFormatException` | §8.2 | `Hostile/DuplicateEntryTests` |
| HST-38 | §22.3 | a tiny frame declaring a huge expansion allocates nothing proportional | §12 | `Hostile/AllocationAmplificationTests` |
| HST-39 | §22.3 | a ratio-legal frame that produces nothing allocates nothing proportional | §12 | `Hostile/AllocationAmplificationTests` |
| CMP-14 | §23 | the ratio is the reader's policy: the same bytes pass one reader and fail a stricter one | §5.10 | `Algorithms/CompressionTests` |
| CMP-15 | §23 | both built-in algorithms decompress incrementally | §12 | `Algorithms/CompressionTests` |
| CMP-16 | §23 | a Brotli stream that yields the declared length but never terminates is malformed | §12 | `Algorithms/CompressionTests` |
| CHK-09 | §24 | a checksum reporting a size the header cannot record → `BinaryConfigurationException`, on write and on read | §8.1 | `Algorithms/ChecksumTests` |
| V0-26 | §13 | a byte-reversed magic is not recognised; `Peek` reports no header | §22 | `Format/RoutingTests` |

Also owed now:

- **LIM-40** text: "through `Validate`" → "through `Validate` or `ValidateShape`, called only from
  `ValueReader`, `ValueWriter` and the engine's composite surface".
- **CMP-16** records a behaviour that is easy to lose: `TryDecompress` refused a Brotli stream that
  never reached its end marker even after producing the declared length; the incremental path first
  accepted it and the NX-01 fix restored the refusal. A later rewrite of the decoder must keep it.
- **§32** — the test-count line updated to the current number (1 668) when the plan is next closed.
- Already applied, nothing owed: **HST-40** (non-minimal 7-bit integers), **KEY-23** (ascending keys).

---

# 2. Owed to the rework, by stage

## R0 — Baseline and oracle

- New **§0 "Rework oracle"**: one text file of SHA-256 values, one per case of the existing corpora —
  `RoundTrip/Corpus*.cs` under every `CorpusProfiles` profile, `Format/V0CorpusTests`, the reference
  graphs of `References/`, the keyed shapes of `Contracts/` — V0 and V1, references on and off where
  the format admits it. The oracle invents no case. Rule: R1–R5 reproduce every value; the oracle is
  retired at R6.
- **ORC-01** — every corpus case hashes to its recorded value.
- **ORC-02** — on a mismatch the test prints the hex of the expected and the actual output for that
  case.
- **UTIL-17** — the oracle helper is itself tested: a deliberately changed byte is reported with both
  hex dumps (§29 — shared helpers are tested).

## R1 — Wire primitives on buffers

- **LIM-44** rewritten: payload bytes are reachable only through `WireReader`/`WireWriter`.
- **LIM-40**, **LIM-47** rewritten for the new types; the rule text does not change.
- **LIM-48** — `WireReader` and `WireWriter` are `ref struct`s and the only types exposing payload
  primitives (source-shape test, like the existing barrier tests).
- WF-* and HST-* are untouched: they are the evidence that R1 changed nothing on the wire.

## R2 — Pipeline on pooled buffers

- **§21 rewritten** as "Metering and windowing over buffers" — STR-01…STR-28 re-expressed against the
  reader and writer (budget versus truncation, origin-relative budget, high-water mark across patches,
  window over-read is malformed, bounded skipping, no materialisation). None is dropped. §2 layout:
  `Streams/` renamed `Wire/`.
- **STR-29** — no `MemoryStream` under `Pipeline/` or on the payload path (source-shape test).
- **STR-30** — INV-15: an exception in the middle of a graph leaves an `IBufferWriter<byte>`, a
  `PipeWriter` and a `Stream` destination with zero bytes written.
- **V0-25 inverted** — a V0 keyed write to a non-seekable destination succeeds and is byte-identical
  to the seekable one.
- **API-18** extended to keyed payloads.
- **CYC-11** — cycle detection by ancestor stack: the same `BinaryTypeException` as today, for a cycle
  at depth 1 and at depth 500; a deep acyclic graph with repeated (non-cyclic) instances is not
  refused.

## R3 — Public surface and non-seekable reading

- **Retired:** SX-01…SX-11 (§8, `StreamExtensions` deleted); every OPT checkpoint citing §4.3
  (`FromHeader`/`FromStream` deleted); the `Deserialize` populate checkpoints of §6 are re-expressed
  for `Populate` below.
- **Inverted:** API-17, OPT-18, V0-14 — a non-seekable source is read, not refused.
- **SRC-01…SRC-nn** — one checkpoint per source kind — span, single-segment sequence, multi-segment
  sequence, seekable `Stream`, non-seekable `Stream`, `PipeReader` (async), `Stream` (async) — ×
  (V1, V0 where allowed), asserting identical values.
- **SRC** — a non-seekable double that fails on any read past the frame proves exactly one V1 frame is
  consumed, synchronously and asynchronously.
- **API-21** — an empty span, sequence or stream is `BinaryFormatException` at every read and populate
  entry point; a `null` array is `BinaryFormatException` (plan §9.1).
- **API-22** — without a bytes-consumed form, trailing bytes after a V1 frame or a V0 root are
  `BinaryFormatException`; with it, two frames (V1) and two payloads (V0) back to back are read and
  the reported position is exact, for span (`int`) and sequence (`SequencePosition`).
- **API-23** — `PooledPayload`: the bytes equal `Serialize<T>(T)`; `Dispose` twice is safe; access
  after `Dispose` is `ObjectDisposedException`.
- **API-24** — `Populate`: a class is populated in place and the same instance is observed; a keyed
  contract keeps a field absent from the payload; nested objects are new instances; a type with a
  dedicated formatter is `BinaryTypeException`; a null root or a back-reference root is
  `BinaryFormatException`; an empty input leaves the target untouched; the bytes-consumed forms.
- **API-25** — asynchrony: cancellation before any byte leaves a `PipeReader` unconsumed; a frame
  split across many pipe segments is read; a pipe completed mid-frame is `BinaryFormatException`;
  `SerializeAsync` to a pipe and a stream equals `Serialize`.
- **API-26** — an asynchronous read or populate meeting V0 is `NotSupportedException` naming the rule;
  an asynchronous V0 write succeeds.
- **API-27** — `DeserializeAsyncEnumerable` over a `Stream` and a `PipeReader`: N frames yield N values
  and complete; the source ending inside a frame is `BinaryFormatException` after the complete frames
  were yielded; each frame has its own budget (N frames each just under a cumulative limit all pass);
  V0 is `NotSupportedException`; cancellation leaves a started frame unconsumed in the pipe.
- **OPT-22** — `WithKeys` ×3 reads an encrypted frame; keys supplied through both `WithEncryption` and
  `WithKeys` are `BinaryConfigurationException` at `Build()`.
- **V0-27** — the V0 read boundary: default strict for span and sequence; a seekable stream is left at
  the end of the root; a non-seekable stream without a length is `NotSupportedException`.
- **§31** extended: **XEP-08** — every new entry point, including `PooledPayload`, `Populate` and the
  asynchronous methods, agrees with the others under P0, P6 and P7.
- **EXT-05** re-evaluated against the new contract §3.

## R4 — Typed engine

- **TYP-01** — every §19 corpus case through the typed engine reproduces the R0 oracle.
- **TYP-02** — INV-17: a counting test double observes no boxing when writing and reading a graph
  without a polymorphic slot; enums are not boxed.
- **TYP-03** — an array whose count is backed by bytes is read into an array of its final length; one
  that is not is read through the pooled path; both yield the same value; a count not backed by bytes
  still fails on truncation without a proportional allocation.
- **LIM-49** — INV-5 extended: shapes (`ISequenceShape`, `IMapShape`) and type contracts receive no
  count and no primitive; the only loops over wire data are in engine codecs (source-shape test).
- **CTR-31** — the engine's contract checks: a contract that calls `Member`/`Field` with a wrong type,
  a wrong key, out of order, too few or too many times is `BinaryTypeException` naming the type and
  member; `ReadField` returning `true` without reading, or `false` after reading, likewise.
- **CTR-32** — a struct owner is populated in place through the `ref` setter.
- **ALC-01…ALC-nn** — `AssertEx.AllocatesLessThan` on each allocation target of plan §11 that R4 makes
  reachable (write into `IBufferWriter` without phases; read of a primitive record from a span; the
  reference-table and asynchronous paths).
- **§28 caches rewritten.** Retired with `src/.../Cache/`: CN-06…CN-13 (one per deleted cache) and
  CN-19 (`ImmutableCollectionsMarshal` resolution — the typed shape calls it directly). Re-pointed at
  the caches that remain: CN-03 (contract cache), CN-04 (union maps), CN-05 (`FormatterCache<T>` and
  the shape-factory cache), CN-14, CN-15, CN-16.
- **CN-20** — no type remains under `src/ViShap.Viper.Serialization/Cache/`, and no `ConcurrentDictionary`
  keyed by `Type` exists outside the contract, union and shape-factory caches (source-shape test).

## R5 — Algorithm contracts

- §23–§26 rewritten for plan §8: one method per direction.
- **Retired:** CMP-15 (incremental is the only mode). **Kept:** CMP-16.
- **CMP-17** — `Decompress` producing fewer or more than `expectedLength` bytes is
  `BinaryFormatException`.
- **CHK-10** — `XxHash3Checksum` and `XxHash128Checksum` round-trip through the pipeline and resolve
  from the catalog.
- **ENC-24** — `KeySizeInBytes` checked at `Build()` for a static key and at resolution for a provided
  key.
- **ENC-25** — the service checks of plan §8.1: a ciphertext length below the plaintext length, a
  destination not filled exactly, a decrypted length out of range → `BinaryConfigurationException`.
- **ENC-26** — `ChaCha20Poly1305Encryption` round-trips; where unsupported it is `BinaryFormatNotSupportedException`
  at `Build()` and on read.
- **ENC-27** — `HkdfKeyProvider` derives distinct keys per key id, equal keys for equal ids, and never
  exposes the root key.
- **ENC-28** — `RequireEncryption` refuses an algorithm reporting `AuthenticatesAssociatedData = false`.
- **CAT-10** — the catalog resolves every built-in, new ones included.
- **EXT-06** — no public algorithm interface declares a default member (reflection over Core).
- **EXT-08** — no public type of the three packages shares its simple name with a public type of the
  BCL assemblies the packages reference (`System.IO.Hashing`, `System.Security.Cryptography`,
  `System.IO.Compression`, `System.Buffers`, `System.IO.Pipelines`) — reflection over both.

## R6 — The final format

- **§11 and §14 rewritten**; §14.5 "Associated data" rewritten: the associated data is the header.
- **HST-09 extended** — flipping every byte of an encrypted frame's header, `onDiskLength` included,
  fails the tag (INV-14).
- **HST-41** — a non-minimal varint is `BinaryFormatException` in every structural position: count,
  length, id, key, version, payload mode, service kind, service length, `onDiskLength`.
- **HDR-21…HDR-nn**, one per rule of plan §6.1: records out of order; a repeated number; number 0; a
  critical-bit mismatch on a known number; an unknown critical number → `BinaryFormatNotSupportedException`;
  an unknown non-critical number skipped; a body not read exactly; a header above 4 096 bytes; a set
  reserved payload-mode bit → `BinaryFormatNotSupportedException`; a record with `id = None`; an empty
  custom name; a checksum hash of the wrong length.
- **CMP-18** — the order of checks of plan §6.1.4: with encryption the exact ratio and
  `MaxCompressedBytes` are enforced after decryption and before decompression allocates.
- **WF-31…WF-nn**, one per row of plan §6.3.2 and the examples there: the null fold for strings,
  sequences, maps and keyed objects with references off; the count without `+ 1` after a reference
  frame; flags for positional objects, unions and `Nullable<T>`; nothing for non-nullable types; the
  `ImmutableArray<T>` fold of plan §6.3.3.
- **REF-19** — the reference frame of plan §6.3.4, byte for byte (`01`, `02`, `0B`).
- **§30.4** rewritten — the fixtures re-frozen once from the corpus; **CMPT** checkpoints re-pointed
  at them.
- **Retired:** ORC-01, ORC-02 (the oracle is deleted).

## R8 — Generator ground

- **CONF-01…CONF-nn** — the conformance suite: for each object shape — positional, keyed, inherited,
  shadowed, overridden, union, struct — the member order, keys, layout and bytes, written so that a
  generated `TypeContract<T>` can be substituted and run against the same cases.
- **EXT-07** — a consumer project built with AOT analysis reports the annotated reflection entry
  points and nothing unannotated.

## R9 — Re-gate

- **§32** rebuilt; test count in Debug and Release.
- New §32 box: `dotnet pack` succeeds for all three packages with non-empty READMEs.
