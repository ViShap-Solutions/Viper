# System-Contract.md — changes required by the rework

**Class: historical.** The record of the pre-release rework. It binds nothing that ships, and its rules stop applying when `v1.0.0` is released; until then it is the evidence that R9 reconciles the system against.

**Applies to:** `internal/System-Contract.md` as of 2026-09-26 (after `HST-40` and `KEY-23`).
**Rule:** each change is made in the stage named beside it, together with the code, and never ahead
of it (`Rework-Plan.md` §0). Until then the contract keeps describing the code that exists.

Stages refer to `Rework-Plan.md` §12; plan sections are cited as "plan §n". Decisions are cited as
`[Dn.n]`, meaning `Decisions.md` §n.n.

---

# 1. Already applied

- **§22.1** — a 7-bit integer is minimally encoded; a longer spelling is `BinaryFormatException`
  (`HST-40`).
- **§14.2, §22.3** — keyed fields appear in strictly ascending key order; a repeated or out-of-order
  key is `BinaryFormatException` (`KEY-23`).

Nothing else is owed before R1. If the owner released the current format without the rework, the
contract would already be accurate for it.

---

# 2. Section by section

### §1 Purpose — R9a — applied

- No change of intent. Re-read once the rewrite is complete: "exhaustive" must still hold.

### §2 Architecture and responsibility boundaries — R1, R2, R4

- Diagram: `ValueReader / ValueWriter` → `WireReader / WireWriter`; the pipeline row loses the three
  metered streams; the engine row reads "typed codecs"; a row is added for type contracts
  (`TypeContract<T>`). *(R1, R2, R4)* — **R1 applied:** `WireReader / WireWriter` as ref structs over
  memory; the pipeline row names the read-ahead, `PayloadBuffer` and `MeteredWriteStream`, the one
  stream decorator still in use. **R2 applied:** the pipeline row loses `MeteredWriteStream` and says
  the phases are transforms over pooled buffers and the frame is built whole. **R4 applied:** the
  operation row is `OperationState`, the engine row reads "typed codecs", and a row is added for type
  contracts.
- §2.2: `SerializationOperation` → `OperationState`, a struct passed by `ref`; "created exactly once
  per public call" is unchanged (INV-1). *(R4)* — **R4 applied**, with the `Graph` traversal the
  state carries and the rule that an asynchronous method takes the one copy it owns.
- §2.3 byte boundary: restated over buffers — the reader knows its exact remaining length; there is no
  stream under the engine; a type contract receives only `MemberWriter` / `MemberReader` (INV-2).
  *(R1, R4)* — **R1 applied**, all but the type-contract clause. **R4 applied:** the type-contract
  clause.
- §2.4 traversal boundary: shapes and engine-owned codecs (plan §10.1); the division of labour
  between a type contract and the engine and the engine's call checks (plan §10.2); boxing only in a
  polymorphic slot (INV-17); the engine never awaits (INV-16). *(R4, R3)* — **R3 applied:** §2.4
  states INV-16; the rest is R4's. **R4 applied:** §2.4 names the codecs as the traversal owner, the
  shapes (INV-5), the type contract with the engine's call checks, and boxing only in the polymorphic
  slot (INV-17).
- State INV-15 (a data or graph error leaves no byte in the destination). *(R2)* — **R2 applied** as
  §2.6 "Atomic writes"; §2.5 states the phases over pooled buffers.

### §3 Public API surface — R3, R5, R8 — applied

- **Type list.** Add `PooledPayload` *(R3)* and `HkdfKeyProvider`, `XxHash3Checksum`,
  `XxHash128Checksum`, `ChaCha20Poly1305Encryption` *(R5)*. Rename `Crc32` → `Crc32Checksum`,
  `Deflate` → `DeflateCompression`, `Brotli` → `BrotliCompression`, `Aes256Gcm` →
  `Aes256GcmEncryption`, so no built-in collides with a BCL type *(R5)*. Remove `StreamExtensions`
  *(R3)*.
- **§3.1 serializer surface** replaced by plan §9.1, with its rules: empty input; no `byte[]` read
  overload and what a `null` array becomes; exactly one payload or frame without a bytes-consumed
  form; asynchronous reads accept V1 only. The sentence "reading needs a seekable stream" is deleted.
  *(R3)*
- **§3.1 populate-in-place** rewritten as `Populate` / `PopulateAsync` (plan §9.6): classes only; the
  struct `ref` forms are gone; only the root is populated; keyed contracts keep absent fields. *(R3)*
- **§3.2 stream extensions** deleted. *(R3)*
- **New:** `PooledPayload` ownership (plan §9.3); bytes consumed (plan §9.4); asynchrony, cancellation
  and failure (plan §9.5), including the **required V0 explanation and example**, verbatim;
  `DeserializeAsyncEnumerable` — a frame per operation, a clean end between frames, a broken end
  inside one, limits per frame and never per connection. *(R3)*
- **New:** the reflection entry points carry `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]`,
  and why. *(R8)*
- **R3 applied.** §3 lists `PooledPayload` and no `StreamExtensions`; the old §3.1 became §3.1
  "Serializer" (the surface and the rules of every entry point), §3.2 `PooledPayload`, §3.3
  populate-in-place, §3.4 bytes consumed and §3.5 asynchrony, which carries the required V0
  explanation and example verbatim — the plan asked for them in "§3.1", which is where the whole
  serializer section was before the split. The old §3.2 is gone.
- **R5 applied.** §3 lists the family-suffixed built-ins, `XxHash3Checksum`, `XxHash128Checksum`,
  `ChaCha20Poly1305Encryption` and `HkdfKeyProvider`, with a paragraph on why every built-in carries
  its family as a suffix and on the interfaces declaring no default members.
- **R8 applied.** §3 carries "The reflection path states its requirements": which entry points carry
  `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` (the 22 generic methods of `BinarySerializer`,
  the 4 of `BinaryFormatDumper`), why, which members carry neither, `IsAotCompatible` on both
  packages, and the test that holds it.

### §4 Options and configuration — R3, R5 — applied

- §4.1 builder: add `WithKeys` ×3; supplying keys through both `WithEncryption` and `WithKeys` is
  `BinaryConfigurationException` at `Build()` (plan §9.2). *(R3)*
- §4.1: a static key is checked against `IEncryptionAlgorithm.KeySizeInBytes` at `Build()`;
  `ChaCha20Poly1305Encryption` chosen for writing on a platform where `IsSupported` is false is
  `BinaryFormatNotSupportedException` at `Build()`. The §13.2 sentence "a key size belongs to the
  algorithm, so no entry point validates it on the way in" is reversed. *(R5)* — **R5 applied** to
  the `Build()` rejection list of §4.1, with the ChaCha20-Poly1305 refusal beside it.
- §4.1 V0 rules: unchanged — `RequireEncryption` / `RequireChecksum` with `WithVersion(0)` or
  `AllowV0Fallback` stay rejected in both directions.
- **§4.3 `FromHeader` and `FromStream`** deleted. `BinaryFormatInspector` (§19) is the way to read a
  header without reading the payload. *(R3)* — **R3 applied:** §4.1 carries `WithKeys`, the
  "keys have one place" rule and the key-id paragraph that lived in §4.3; §4.3 is now "Reading a
  header without reading the payload".

### §5 Serialization limits — R2, R6

**R6 applied.**

- `MaxWireBytes`: the most the adapter buffers from a source and the most the writer emits, relative
  to the operation's start. Same default. *(R2)* — **R2 applied** in §5.10.
- §5.10 phase limits: restate the order of checks of plan §6.1.4 — `MaxEncryptedBytes` against
  `onDiskLength` at the header; `MaxCompressedBytes` against the plaintext length after decryption;
  the ratio exactly at the header without encryption and exactly after decryption with it. *(R6)*
- Add the fixed 4 096-byte header bound — a format bound, `BinaryFormatException`, not a policy
  limit. *(R6)*

### §6 Resource accounting — R4 — applied

- The budget lives in `OperationState`. `EnterDepth()` returning a `ref struct` scope stays.
- Collection capacity and direct array allocation follow the bytes-backed rule (plan §10.1, INV-3).

### §7 Security stream mechanisms — R1, R2

- **Rewritten entirely** as "Metering and windowing over buffers" (plan §5.2). Every rule it states
  today is kept: budget versus truncation classification, origin-relative write budget,
  high-water-mark accounting across patches, window over-read is malformed, unknown keyed fields
  skipped without materialisation.
- **R1 applied — the read side and the write budget.** A `WireReader` over memory leaves no stream on
  the read path to describe, so §7.1 became "Metering on read" (read-ahead within the budget,
  classification by the bound broken, the position after a read), §7.3 became "The field window"
  (`WireReader.Slice`), and the `PayloadBuffer` budget was added. §7.2 `MeteredWriteStream`, which
  still copies the finished bytes to the destination, is left for R2.
- **R2 applied — the write side.** §7 retitled "Metering and windowing over buffers"; §7.2 became
  "Metering on write" (the `PayloadBuffer` budget, the patch that is never charged twice, the frame
  checked against `MaxWireBytes` before it leaves, no seek, `IOException` wrapped); §7.1 notes the
  array decoded where it lies and the pooled phase buffers on read.

### §8 Exception taxonomy — R3, R5, R6

**R6 applied.**

- §8.10 `NotSupportedException`: seekability removed. Remaining uses: a V0 payload from a non-seekable
  stream without a length; an asynchronous read that meets V0. *(R3)* — **R3 applied**, together with
  the §8.8 pipe failures (an `IOException` from a pipe, as from a stream; nothing else wrapped), the
  keys-twice case of §8.1 and `OperationCanceledException` in §8.10; `ObjectDisposedException` from a
  disposed `PooledPayload` is listed there too.
- §8.10: the keyed V0 write to a non-seekable destination is no longer a use. *(R1 — applied; moved
  from R2 by the owner's decision of 2026-09-27, `Owner-Review.md` log 53)*
- §8.8 `BinaryStreamException`: add `PipeReader` / `PipeWriter` failures. *(R3)*
- Add: `OperationCanceledException` from the asynchronous methods is standard .NET, outside the
  taxonomy. *(R3)*
- §8.1 `BinaryConfigurationException`: add the algorithm checks of plan §8.1 (ciphertext length,
  filled destination, decrypted length, key size, hash size) and keys supplied twice. *(R3, R5)* —
  **R5 applied**, with a destination refused by the algorithm with `ArgumentException`; §8.7 gains a
  resolved key of the wrong length and `HkdfKeyProvider` without a key id.
- §8.2 `BinaryFormatException`: add the header rules of plan §6.1 (service order, duplicate number,
  number 0, critical-bit mismatch, `id = None`, empty custom name, body not read exactly, the 4 KiB
  bound) and the payload rules of plan §6.3. *(R6)*
- §8.4 `BinaryFormatNotSupportedException`: add an unknown critical service, a set reserved
  payload-mode bit, and an unsupported ChaCha20-Poly1305. *(R5, R6)* — **R5 applied:** the
  unsupported ChaCha20-Poly1305, at `Build()` and on read.
- §8.5 `BinaryIntegrityException`: a flipped header byte of an encrypted frame fails the tag — every
  header byte, since the associated data is the header (INV-14). *(R6)*

### §9 Inner-exception preservation — none

### §10 Version and wire-format contract — R3, R6

**R6 applied.**

- §10.1 V1: the header is a list of services (plan §6.1); a new capability is a new service number;
  an unknown critical service is refused, an unknown non-critical one skipped. The references mode is
  a property of the frame. *(R6)*
- §10.2 V0: V0 differs from V1 only in what needs metadata — the header services and the reference
  mode; the read boundary of plan §7; keyed writes to any destination (the "requires a seekable
  destination" paragraph is deleted — **R1 applied**, moved from R2 by the owner's decision of
  2026-09-27; §14.2's matching sentence with it); the **required V0 explanation and example** of plan
  §9.5. "Unauthenticated by construction" stays true and stays. *(R1, R3)*
- §10.3 routing: no peek-and-rewind; the magic is decoded from the buffered source. *(R3)* — **R3
  applied**, with §10.2's read boundary and the required V0 text. §22.8's sentence that a V0 reader
  "does not require the source to end there" was made untrue for spans and sequences by the boundary
  rules, so it now names that exception; the rest of §22.8 stays for R6.

### §11 V1 header fields — R6

**R6 applied.**

- **Rewritten** from plan §6.1: layout, payload mode, service records, numbers, bodies, the check
  order, the 4 KiB bound. The rules "`Compression = None` → lengths equal" and
  "`Encryption = None` → lengths equal" are deleted.

### §12 Compression contract — R5 — applied

- The two halves of NX-01 stay: the ratio bounds the declared expansion, and decompression allocates
  as output arrives. The paragraph on `SupportsIncrementalDecompression` is replaced: decompression
  into a writer of exactly `expectedLength` is the only mode, for built-in and custom algorithms alike.
- The interface of plan §8.1.

### §13 Encryption contract — R5, R6

**R6 applied.**

- §13.1: `AuthenticatesAssociatedData` is required, with no default; the associated data is the exact
  header bytes (plan §6.2). *(R5, R6)*
- §13.2: key size checked at `Build()` for a static key and at resolution for a provided key;
  `HkdfKeyProvider` — derivation from a root key with the key id as HKDF info, the root key never
  exposed, the derived key an owned copy. *(R5)*
- `GetCiphertextLength`: exact and at least the plaintext length; an algorithm that cannot state it
  is unsupported. *(R5)*
- `ChaCha20Poly1305Encryption` and the `IsSupported` behaviour of the BCL type under it. *(R5)*
- The encrypted frame is written straight to the destination without a copy. *(R2, R5)* — **R2
  applied:** the ciphertext is a pooled buffer and the frame reaches a stream or a new array in one
  copy, which is the write; the associated data is built only under encryption and cleared. Writing
  straight into a buffer writer's span needs the exact ciphertext length of plan §8.1 and stays for R5.
- **R5 applied.** §13 carries the interface, the table of the service's checks, the frame sized
  before encryption and encrypted straight into the destination (a buffer writer's span, the new
  array, one pooled buffer for a stream), and ChaCha20-Poly1305 with its platform refusal; §13.1 says
  `AuthenticatesAssociatedData` has no default and there is no method without associated data; §13.2
  the key-size checks and `HkdfKeyProvider`. The two sentences giving "only known after encryption"
  as the reason `OnDiskLength` is outside the associated data (§13.1, §22.7) no longer hold and now
  say only that it is outside it and self-verifying; the associated data itself is R6's. §2.5 and
  §2.6 say where encryption's output goes and that the services hold an algorithm to what it
  states. The `Encrypt` signature returns the bytes written, by the owner's decision of 2026-09-28
  (`Owner-Review.md` log 57).

### §14 Contracts and members — R4, R6, R8 — applied

**R6 applied.**

- §14.1: rules unchanged; the member plan is `TypeContract<T>`, identical for reflection and any
  generated contract (INV-12). *(R4, R8)* — **R4 applied:** §14.1 opens with the type contract and
  `ReflectedContract<T>`; R8 adds the generated side.
- §14.2: keyed framing is `varint key · int32 length`, with the reason for the fixed length (plan
  §6.3.1); the field count carries the null fold (plan §6.3.2). *(R6)*
- Add: the type contract is the unit a generated contract replaces; nothing else is generated. *(R8)*
- **R8 applied.** §14.1 states that the type contract is the unit a generated contract replaces and
  that nothing else is generated, and names the conformance suite that fixes what a contract must
  produce. §21.3's list of deferred work now says the seam is in place and the generator is not.

### §15 Polymorphism — R4 — applied

- Add: the polymorphic slot is the only place the engine boxes. No change to tags or rules.

### §16 References and cycles — R2, R6

**R6 applied.**

- §16: the reference frame is one varint carrying null (plan §6.3.4); explicit ids kept, with the
  skip-desync reason for rejecting implicit ids. *(R6)*
- Cycle detection without references is an ancestor-stack search; the diagnostic is unchanged. *(R2)*
  — **R2 applied**, with the pooled reference tables.
- *Added in R4, outside this list, because the typed engine had to decide it:* a back reference that
  resolves to an object the declared type cannot hold is `BinaryFormatException`. Before R4 the
  object-typed setter raised `InvalidCastException` on such a payload, a framework name on a payload
  path (INV-10); the typed codec checks the resolved object's type. Pinned by REF-19.

### §17 Arrays and safe materialisation — R4 — applied

- Restate: an array whose count is backed by bytes is allocated at its final length and read into;
  otherwise elements accumulate in a pooled buffer and one final array is created. Collection
  capacity follows the same rule.

### §18 Naming and ownership model — R1, R2, R4

- Names: `WireReader`, `WireWriter`, `PayloadBuffer`, `OperationState`, `FormatterCache<T>`,
  `IScalarFormatter<T>`, `ISequenceShape<,>`, `IMapShape<,,>`, `TypeContract<T>`,
  `ReflectedContract<T>`, `MemberWriter`, `MemberReader`. Remove the three stream decorators,
  `ValueReader`, `ValueWriter`, `BinaryHeaderPeek`, and every reflective accessor cache of
  `Cache/`; state which caches remain (plan §12, R4) and that each is built once per type and safe
  under concurrent first use. — **R1 applied:** `WireReader`, `WireWriter` and `PayloadBuffer` named;
  `ValueReader` and `ValueWriter` removed. **R2 applied:** the three stream decorators were never named in §18; `EncodedFrame`
  and `RentedBytes`, the pipeline's pooled frame and phase output, are named. **R4 applied:** the
  typed names; `GraphReader`/`GraphWriter`, `SerializationOperation` and the non-generic formatter
  interfaces removed; "What is cached" states the three caches that remain and how they behave under
  concurrent first use.

### §19 Format inspection and diagnostics — R3, R6

**R6 applied.**

- `BinaryFormatInspector.Peek` over a span and a sequence; "requires a seekable stream and restores
  its position" applies only to the stream overload. *(R3)* — **R3 applied.**
- `BinaryHeaderInfo` and `BinaryFormatDumper` report the service records. *(R6)*
- **§19 rewritten for plan §9.7** *(R6, D9.32)*: the purpose (a binary frame read by a person, as JSON
  is); the surface — `DumpHeader` over a span, a sequence and a seekable stream (the `byte[]` overload
  removed), `Dump`, `Dump<T>` over a span and a sequence, `DumpValue<T>`, `Compare<T>`, and
  `BinaryDump`, `BinaryDumpNode`, `BinaryDumpNodeKind`, `BinaryDumpDifference`; what each reports; the
  text report with its example; the rules: never throws for a malformed frame (only
  `BinarySerializerException` is caught and kept in `Failure`, with `FailureOffset` and
  `FailurePath`), reads under the options' limits, decrypts only with the options' keys and never
  renders key material, renders values invariantly and times in UTC, bounds values in the tree
  (strings at 256 characters, blobs at 64 bytes); `Compare<T>` reports the first differing node.
- §3: the four new types in `ViShap.Viper.Diagnostics`. *(R6)*
- §2.4: the engine's trace seam — `OperationState.Trace`, null outside the dumper; only the codecs
  report to it; it receives offsets, names, kinds, generic values and read-only byte views from
  `WireReader`, and changes nothing a read does. *(R6)*

### §20 Stream ownership — R1, R3

- **Rewritten.** Any stream; exactly one V1 frame is read and nothing past it; V0 from a non-seekable
  stream needs a length; a seekable stream is left at the end of the root; after a failed or
  cancelled read the position is undefined. *(R3)* — **R3 applied.**
- **R1 applied:** a successful read leaves the stream where the decoded bytes end; a failed one may
  leave it anywhere up to the furthest byte read ahead.

### §21 Semantic clarifications — R3, R9a — applied

- §21.3 deferred list: remove "streaming (non-buffered) payloads" and "an async API"; add "an
  asynchronous engine" (rejected, INV-16); keep "a public formatter contract" and "source generators"
  with a pointer to `TypeContract<T>`. *(R9a)* — "an async API" was removed in R3, the stage that
  made it untrue; the rest waits for R9a. §24's `FromHeader`/`FromStream` box was likewise re-pointed
  at `WithKeys` in R3; the checklist is rebuilt in R9a.

### §22 Wire format — R6

**R6 applied.**

- **Rewritten** from plan §6:
  - §22.1: varint and minimal encoding for structural numbers; data fixed-width; the null fold.
  - §22.2 value framing: null in the first number; the reference frame of plan §6.3.4.
  - §22.3 shapes: counts as varints with the fold; keyed framing; strictly ascending keys.
  - §22.5 composites: `ImmutableArray<T>` per plan §6.3.3.
  - §22.6 envelope: plan §6.1 complete, with the byte examples.
  - **§22.7 associated data deleted** — it is the header (plan §6.2).
  - §22.8 V0: the boundary rules; the byte-identity property (INV-18).

### §23 Supported types — R4 — applied

- No type is added or removed. Re-verify every note against the typed engine, especially memory-like
  values, `ImmutableArray<T>` and `Lazy<T>`. *(Applied in R4: every note holds as written — a
  memory-like read wraps a fresh array exactly as long as the value, a default `ImmutableArray<T>`
  stays distinct from empty, and a read `Lazy<T>` holds its value without having created it; the
  round-trip corpus and the fixtures pass unchanged. The text needed no edit.)*

### Invariants — R1, R9a — applied

- **R1 applied:** the architecture checklist names `WireReader`/`WireWriter` as the only access to
  payload bytes.

- Every invariant INV-1…INV-18 of `Rework-Plan.md` §3 is stated in the section it governs. After the
  release the plan is a historical record, so an invariant written only there would no longer bind
  anything [D9.28].

### §24 Release checklist — R9a applied, R9b open (the `dotnet pack` box)

- Rebuilt. New boxes: every entry point reads non-seekable sources; no `MemoryStream` on the payload
  path; the allocation targets of plan §11 met or recorded as open; INV-14…INV-18 pinned; the
  conformance suite passes; AOT annotations present; `dotnet pack` succeeds for all three packages.
