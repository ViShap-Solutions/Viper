# System-Contract.md — changes required by the rework

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

### §1 Purpose — R9

- No change of intent. Re-read once the rewrite is complete: "exhaustive" must still hold.

### §2 Architecture and responsibility boundaries — R1, R2, R4

- Diagram: `ValueReader / ValueWriter` → `WireReader / WireWriter`; the pipeline row loses the three
  metered streams; the engine row reads "typed codecs"; a row is added for type contracts
  (`TypeContract<T>`). *(R1, R2, R4)*
- §2.2: `SerializationOperation` → `OperationState`, a struct passed by `ref`; "created exactly once
  per public call" is unchanged (INV-1). *(R4)*
- §2.3 byte boundary: restated over buffers — the reader knows its exact remaining length; there is no
  stream under the engine; a type contract receives only `MemberWriter` / `MemberReader` (INV-2).
  *(R1, R4)*
- §2.4 traversal boundary: shapes and engine-owned codecs (plan §10.1); the division of labour
  between a type contract and the engine and the engine's call checks (plan §10.2); boxing only in a
  polymorphic slot (INV-17); the engine never awaits (INV-16). *(R4, R3)*
- State INV-15 (a data or graph error leaves no byte in the destination). *(R2)*

### §3 Public API surface — R3, R5, R8

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
  and failure (plan §9.5), including the **required V0 explanation and example**, verbatim. *(R3)*
- **New:** the reflection entry points carry `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]`,
  and why. *(R8)*

### §4 Options and configuration — R3, R5

- §4.1 builder: add `WithKeys` ×3; supplying keys through both `WithEncryption` and `WithKeys` is
  `BinaryConfigurationException` at `Build()` (plan §9.2). *(R3)*
- §4.1: a static key is checked against `IEncryptionAlgorithm.KeySizeInBytes` at `Build()`;
  `ChaCha20Poly1305Encryption` chosen for writing on a platform where `IsSupported` is false is
  `BinaryFormatNotSupportedException` at `Build()`. The §13.2 sentence "a key size belongs to the
  algorithm, so no entry point validates it on the way in" is reversed. *(R5)*
- §4.1 V0 rules: unchanged — `RequireEncryption` / `RequireChecksum` with `WithVersion(0)` or
  `AllowV0Fallback` stay rejected in both directions.
- **§4.3 `FromHeader` and `FromStream`** deleted. `BinaryFormatInspector` (§19) is the way to read a
  header without reading the payload. *(R3)*

### §5 Serialization limits — R2, R6

- `MaxWireBytes`: the most the adapter buffers from a source and the most the writer emits, relative
  to the operation's start. Same default. *(R2)*
- §5.10 phase limits: restate the order of checks of plan §6.1.4 — `MaxEncryptedBytes` against
  `onDiskLength` at the header; `MaxCompressedBytes` against the plaintext length after decryption;
  the ratio exactly at the header without encryption and exactly after decryption with it. *(R6)*
- Add the fixed 4 096-byte header bound — a format bound, `BinaryFormatException`, not a policy
  limit. *(R6)*

### §6 Resource accounting — R4

- The budget lives in `OperationState`. `EnterDepth()` returning a `ref struct` scope stays.
- Collection capacity and direct array allocation follow the bytes-backed rule (plan §10.1, INV-3).

### §7 Security stream mechanisms — R2

- **Rewritten entirely** as "Metering and windowing over buffers" (plan §5.2). Every rule it states
  today is kept: budget versus truncation classification, origin-relative write budget,
  high-water-mark accounting across patches, window over-read is malformed, unknown keyed fields
  skipped without materialisation.

### §8 Exception taxonomy — R3, R5, R6

- §8.10 `NotSupportedException`: seekability removed. Remaining uses: a V0 payload from a non-seekable
  stream without a length; an asynchronous read that meets V0. *(R3)*
- §8.8 `BinaryStreamException`: add `PipeReader` / `PipeWriter` failures. *(R3)*
- Add: `OperationCanceledException` from the asynchronous methods is standard .NET, outside the
  taxonomy. *(R3)*
- §8.1 `BinaryConfigurationException`: add the algorithm checks of plan §8.1 (ciphertext length,
  filled destination, decrypted length, key size, hash size) and keys supplied twice. *(R3, R5)*
- §8.2 `BinaryFormatException`: add the header rules of plan §6.1 (service order, duplicate number,
  number 0, critical-bit mismatch, `id = None`, empty custom name, body not read exactly, the 4 KiB
  bound) and the payload rules of plan §6.3. *(R6)*
- §8.4 `BinaryFormatNotSupportedException`: add an unknown critical service, a set reserved
  payload-mode bit, and an unsupported ChaCha20-Poly1305. *(R5, R6)*
- §8.5 `BinaryIntegrityException`: a flipped header byte of an encrypted frame fails the tag — every
  header byte, since the associated data is the header (INV-14). *(R6)*

### §9 Inner-exception preservation — none

### §10 Version and wire-format contract — R3, R6

- §10.1 V1: the header is a list of services (plan §6.1); a new capability is a new service number;
  an unknown critical service is refused, an unknown non-critical one skipped. The references mode is
  a property of the frame. *(R6)*
- §10.2 V0: V0 differs from V1 only in what needs metadata — the header services and the reference
  mode; the read boundary of plan §7; keyed writes to any destination (the "requires a seekable
  destination" paragraph is deleted, R2); the **required V0 explanation and example** of plan §9.5.
  "Unauthenticated by construction" stays true and stays. *(R2, R3)*
- §10.3 routing: no peek-and-rewind; the magic is decoded from the buffered source. *(R3)*

### §11 V1 header fields — R6

- **Rewritten** from plan §6.1: layout, payload mode, service records, numbers, bodies, the check
  order, the 4 KiB bound. The rules "`Compression = None` → lengths equal" and
  "`Encryption = None` → lengths equal" are deleted.

### §12 Compression contract — R5

- The two halves of NX-01 stay: the ratio bounds the declared expansion, and decompression allocates
  as output arrives. The paragraph on `SupportsIncrementalDecompression` is replaced: decompression
  into a writer of exactly `expectedLength` is the only mode, for built-in and custom algorithms alike.
- The interface of plan §8.1.

### §13 Encryption contract — R5, R6

- §13.1: `AuthenticatesAssociatedData` is required, with no default; the associated data is the exact
  header bytes (plan §6.2). *(R5, R6)*
- §13.2: key size checked at `Build()` for a static key and at resolution for a provided key;
  `HkdfKeyProvider` — derivation from a root key with the key id as HKDF info, the root key never
  exposed, the derived key an owned copy. *(R5)*
- `GetCiphertextLength`: exact and at least the plaintext length; an algorithm that cannot state it
  is unsupported. *(R5)*
- `ChaCha20Poly1305Encryption` and the `IsSupported` behaviour of the BCL type under it. *(R5)*
- The encrypted frame is written straight to the destination without a copy. *(R2, R5)*

### §14 Contracts and members — R4, R6, R8

- §14.1: rules unchanged; the member plan is `TypeContract<T>`, identical for reflection and any
  generated contract (INV-12). *(R4, R8)*
- §14.2: keyed framing is `varint key · int32 length`, with the reason for the fixed length (plan
  §6.3.1); the field count carries the null fold (plan §6.3.2). *(R6)*
- Add: the type contract is the unit a generated contract replaces; nothing else is generated. *(R8)*

### §15 Polymorphism — R4

- Add: the polymorphic slot is the only place the engine boxes. No change to tags or rules.

### §16 References and cycles — R2, R6

- §16: the reference frame is one varint carrying null (plan §6.3.4); explicit ids kept, with the
  skip-desync reason for rejecting implicit ids. *(R6)*
- Cycle detection without references is an ancestor-stack search; the diagnostic is unchanged. *(R2)*

### §17 Arrays and safe materialisation — R4

- Restate: an array whose count is backed by bytes is allocated at its final length and read into;
  otherwise elements accumulate in a pooled buffer and one final array is created. Collection
  capacity follows the same rule.

### §18 Naming and ownership model — R1, R2, R4

- Names: `WireReader`, `WireWriter`, `PayloadBuffer`, `OperationState`, `FormatterCache<T>`,
  `IScalarFormatter<T>`, `ISequenceShape<,>`, `IMapShape<,,>`, `TypeContract<T>`,
  `ReflectedContract<T>`, `MemberWriter`, `MemberReader`. Remove the three stream decorators,
  `ValueReader`, `ValueWriter`, `BinaryHeaderPeek`.

### §19 Format inspection and diagnostics — R3, R6

- `BinaryFormatInspector.Peek` over a span and a sequence; "requires a seekable stream and restores
  its position" applies only to the stream overload. *(R3)*
- `BinaryHeaderInfo` and `BinaryFormatDumper` report the service records. *(R6)*

### §20 Stream ownership — R3

- **Rewritten.** Any stream; exactly one V1 frame is read and nothing past it; V0 from a non-seekable
  stream needs a length; a seekable stream is left at the end of the root; after a failed or
  cancelled read the position is undefined.

### §21 Semantic clarifications — R3, R9

- §21.3 deferred list: remove "streaming (non-buffered) payloads" and "an async API"; add "an
  asynchronous engine" (rejected, INV-16); keep "a public formatter contract" and "source generators"
  with a pointer to `TypeContract<T>`. *(R9)*

### §22 Wire format — R6

- **Rewritten** from plan §6:
  - §22.1: varint and minimal encoding for structural numbers; data fixed-width; the null fold.
  - §22.2 value framing: null in the first number; the reference frame of plan §6.3.4.
  - §22.3 shapes: counts as varints with the fold; keyed framing; strictly ascending keys.
  - §22.5 composites: `ImmutableArray<T>` per plan §6.3.3.
  - §22.6 envelope: plan §6.1 complete, with the byte examples.
  - **§22.7 associated data deleted** — it is the header (plan §6.2).
  - §22.8 V0: the boundary rules; the byte-identity property (INV-18).

### §23 Supported types — R4

- No type is added or removed. Re-verify every note against the typed engine, especially memory-like
  values, `ImmutableArray<T>` and `Lazy<T>`.

### §24 Release checklist — R9

- Rebuilt. New boxes: every entry point reads non-seekable sources; no `MemoryStream` on the payload
  path; the allocation targets of plan §11 met or recorded as open; INV-14…INV-18 pinned; the
  conformance suite passes; AOT annotations present; `dotnet pack` succeeds for all three packages.
