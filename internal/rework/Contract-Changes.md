# System-Contract.md — changes required by the rework

**Applies to:** `internal/System-Contract.md` as of the NX-12 change.
**Rule:** each change is made in the stage named beside it, together with the code, and never ahead
of it (`Rework-Plan.md` §0). Until then the contract keeps describing the code that exists.

Stage names refer to `Rework-Plan.md` §12. Decision IDs (D-n) refer to `Rework-Plan.md` §13.

---

# 1. Needed now, independent of the rework

None open. NX-01…NX-12 were applied to the contract in the same changes as the code, and §18 names the
composite surface. If the owner releases the current format without the rework, the contract is
already accurate for it.

---

# 2. Section by section

### §1 Purpose — R9

- No change of intent. Re-read once the rewrite is complete: "exhaustive" must still be true.

### §2 Architecture and responsibility boundaries — R1, R2, R4

- §2 diagram: `ValueReader / ValueWriter` → `WireReader / WireWriter`; the pipeline row loses
  `MeteredReadStream / MeteredWriteStream · WindowReadStream`; the engine row reads "typed traversal".
  *(R1, R2, R4)*
- §2.2 `SerializationOperation` → `OperationState`, a struct passed by `ref`; the "created exactly once
  per public call" rule is unchanged. *(R4)*
- §2.3 byte boundary: restate over buffers — the reader knows its exact remaining length; there is no
  stream underneath the engine. *(R1)*
- §2.4 traversal boundary: add "typed shapes" and the explicit object path for polymorphic slots
  only. *(R4)*

### §3 Public API surface — R3, R5, R8

- Type list: add `PooledPayload`, `EncryptionGuarantee`, `HkdfKeyProvider`, `ChaCha20Poly1305`, the new
  checksum and compression classes (`Rework-Plan.md` §8.2). Remove none of the existing types unless D-6
  removes `StreamExtensions`. *(R3, R5)*
- §3.1 serializer surface: replaced by `Rework-Plan.md` §9. The empty-input rule stays (an empty span
  is not a payload). The "reading needs a seekable stream" sentence is deleted. *(R3)*
- §3.2 stream extensions: rewritten or removed per D-6. *(R3)*
- New paragraph: the reflection entry points carry trimming/AOT annotations and why. *(R8)*

### §4 Options and configuration — R5, R7

- §4.1 builder: `WithEncryption` validates the key length against `IEncryptionAlgorithm.KeySize` at
  `Build()`. The paragraph saying "a key size belongs to the algorithm, so no entry point validates it
  on the way in" (§13.2) is reversed. *(R5)*
- §4.1 rejections: the two V0 rules (`RequireEncryption`/`RequireChecksum` with `WithVersion(0)` or
  `AllowV0Fallback`) are **replaced** by: under V0 the policy requires the configured phase to be
  present. *(R7)*
- §4.1 "Algorithms configured on the options are not applied to a V0 write" — deleted. *(R7)*
- §4.3 `FromHeader`/`FromStream` — per D-6. *(R3)*

### §5 Serialization limits — R2, R6

- `MaxWireBytes`: restate as the most the adapter buffers from a source and the most the writer emits,
  relative to the operation's start. Same default. *(R2)*
- §5.10: unchanged in meaning. The note that the ratio "is evaluated while the header is read" stays
  true for V1; add the V0 case — the ratio is evaluated against the delimited source length. *(R7)*
- Consider whether `MaxCompressedBytes` and `MaxEncryptedBytes` still pull their weight once every
  phase writes into a bounded pooled buffer. Recommendation: keep both; they are cheap and they bound
  the *declared* sizes before any buffer exists. *(R6)*

### §6 Resource accounting — R4

- `EnterDepth()` returning a `ref struct` scope stays. Add: the budget lives in `OperationState`.

### §7 Security stream mechanisms — R2

- **Rewritten entirely.** The three decorator types are deleted. The section becomes "Metering and
  windowing over buffers" and keeps every rule it states today: budget versus truncation
  classification, origin-relative write budget, high-water-mark accounting across patches, window
  over-read is malformed, unknown keyed fields skipped without materialisation.

### §8 Exception taxonomy — R3, R7

- §8.10: `NotSupportedException` for seekability is removed from the list of reasons. Its remaining
  uses: an encrypted V0 payload, or a V0 payload on a non-seekable stream, read from an undelimited
  source. *(R3, R7)*
- §8.8 `BinaryStreamException`: add `PipeReader`/`PipeWriter` failures. *(R3)*
- Add: `OperationCanceledException` from the async frame-edge methods is standard .NET and stays
  outside the taxonomy. *(R3)*

### §9 Inner-exception preservation — none

### §10 Version and wire-format contract — R6, R7

- §10.1 V1: add the extension area and its critical-bit rule. *(R6)*
- §10.2 V0: **rewritten.** V0 differs from V1 only in self-description. Phases and references apply
  from configuration; the delimiting rules; "unauthenticated by construction" is deleted, because a
  V0 payload configured for AEAD carries a tag. The downgrade reasoning of `Rework-Plan.md` §7 replaces
  the current "enforced at configuration time" paragraph. *(R7)*
- §10.3 routing: no peek-and-rewind; the magic is decoded from the buffered source. *(R3)*

### §11 V1 header fields — R6

- **Rewritten** from `Rework-Plan.md` W6: flags, varint algorithm ids, extension area, lengths present
  only for phases present, the expansion rule unchanged in meaning.

### §12 Compression contract — R5, R7

- The two-halves rule of NX-01 stays. The paragraph on `SupportsIncrementalDecompression` is replaced:
  decompression into a writer is the only mode, so the second half is structural for every algorithm,
  built-in or custom. *(R5)*
- Add V0 compression. *(R7)*

### §13 Encryption contract — R5, R7

- §13.1: `AuthenticatesAssociatedData` → `Guarantee`; the "one undertaking by the implementer" wording
  is kept for `AuthenticatedWithAssociatedData`. The associated-data image gains the extension area. *(R5, R6)*
- §13.2: key size validated at `Build()` for static keys, at resolution for provided keys. *(R5)*
- Add ChaCha20Poly1305 and its `IsSupported` behaviour — `BinaryFormatNotSupportedException` when the
  platform lacks it. *(R5)*
- Add V0 encryption with empty associated data. *(R7)*

### §14 Contracts and members — R6, R8

- §14.1: unchanged in rules. Add the schema fingerprint (W7) if D-2 is accepted. *(R6)*
- §14.2: keyed field framing becomes varint key + int32 length (W4), with the reason for the fixed
  length stated. *(R6)*
- Add: the object plan is the unit a generated plan replaces; nothing else is generated. *(R8)*

### §15 Polymorphism — R4

- Add: polymorphic slots are the only place the engine boxes. No change to tags or rules.

### §16 References and cycles — R6, R7

- §16: reference frame encoding per W3; explicit ids kept, with the skip-desync reason for rejecting
  implicit ids. *(R6)*
- Add V0 references from configuration. *(R7)*

### §17 Arrays and safe materialisation — R4

- "variable-size arrays are built incrementally through a bounded-capacity growth path": restate — an
  array is built in a pooled buffer and materialised once at its final length, so the only allocation
  is the final array.

### §18 Naming and ownership model — R1, R2, R4

- Replace the names: `WireReader`, `WireWriter`, `PayloadBuffer`, `OperationState`, `IFormatter<T>`,
  `FormatterCache<T>`. Remove the three stream decorators.

### §19 Format inspection and diagnostics — R3

- `BinaryFormatInspector.Peek` over a span and a sequence; the "requires a seekable stream and must
  restore its position" rule applies only to the stream overload.

### §20 Stream ownership — R3

- **Rewritten.** Any stream; exactly one V1 frame is read; nothing is read past it; V0 on a
  non-seekable stream needs a delimited source. "A failed operation may leave the stream position at
  the point of failure" stays for streams.

### §21 Semantic clarifications — R7, R9

- §21.1: the V0 paragraph is replaced by the configuration-driven rule. *(R7)*
- §21.3 deferred list: remove "streaming (non-buffered) payloads" if the frame-edge design is
  accepted (it answers the non-seekable need without streaming the engine); keep "a public formatter
  contract" and "source generators"; replace "an async API" with "an async engine". *(R9)*

### §22 Wire format — R6

- **Rewritten** from `Rework-Plan.md` §6. §22.1 gains the varint rule and "minimal encoding only".
  §22.2 value framing: reference frame per W3. §22.3 shapes: counts as varints, keyed framing per W4.
  §22.6 envelope and §22.7 associated data rebuilt with the extension area. §22.8 V0: the phase and
  delimiting rules, and the byte-identity property restated.

### §23 Supported types — R4

- No type is added or removed by the rework. Re-verify every note against the typed engine —
  especially the memory-like values, `ImmutableArray<T>` and `Lazy<T>`.

### §24 Release checklist — R9

- Rebuilt at R9. New boxes: every entry point reads non-seekable sources; no `MemoryStream` in the
  payload path; the §11 allocation targets met or recorded as open; the conformance suite passes;
  trimming/AOT annotations present; `dotnet pack` succeeds for all three packages.
