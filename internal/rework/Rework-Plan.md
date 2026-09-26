# ViShap.Viper — Pre-release Rework Plan

**Status:** Approved by the owner on 2026-09-26. Stage R0 has not started. Two findings raised while
preparing the plan are already applied to `src/`, the contract and the QA plan (§1).
**Target:** the first public release, `v1.0.0`. The rework happens before it, while no published
format, package or consumer exists.
**Written against:** `dfb336b` plus the working-tree changes listed in §1.

This folder is the complete brief. Read it in this order:

```text
Rework-Plan.md               this file — what is built, in which order, and what must not be lost
Contract-Changes.md          every change System-Contract.md receives, by section and by stage
QA-Plan-Changes.md           every change QA-Plan.md receives, by section and by stage
Benchmark-Plan-Changes.md    every change Benchmark-Plan.md receives, by section and by stage
Decisions.md                 the owner's decisions, in Russian, with the byte diagrams and examples
                             they were taken on — the source this plan was written from
Owner-Review.md              the review of the previous plan and the owner's decision log (Russian)
```

**Precedence.** Where this plan and `Decisions.md` disagree, `Decisions.md` is right and this plan
is defective; fix the plan, do not choose. Every section below cites the decision it implements as
`[Dn.n]`, meaning `Decisions.md` §n.n.

The three change files are separate from the documents they amend on purpose. `System-Contract.md`,
`QA-Plan.md` and `Benchmark-Plan.md` describe what the code does *now*; each is rewritten from its
change file in the stage that makes the change real, never ahead of it.

---

# 0. Rules for whoever executes this

1. **One stage at a time.** A stage starts only when the previous stage's gate holds (§12).
2. **The suite is green at the end of every stage**, in Debug and in Release. A stage that cannot end
   green is split, not merged.
3. **The wire does not change before R6.** Through R1–R5 the thirteen frozen fixtures in
   `tests/.../Fixtures/Wire/*.bin` and the R0 byte oracle are the arbiter: a failure there means the
   stage changed behaviour, and the fix is in `src/`, never in the fixture or the oracle.
4. **The fixtures are re-frozen exactly once, at R6**, from the corpus. That is the single recorded
   exception to the `CLAUDE.md` rule that they are never regenerated; the rule is restored in the same
   change [D9.16].
5. **Invariants are carried by construction** (§3). Every one has a structural test; the test is
   rewritten with the code and never deleted. If meeting a stage's goal would weaken an invariant,
   the stage stops and the question goes to the owner. That is the one discovery this plan does not
   pre-authorise.
6. **Documents change with the code.** Each stage applies its part of the three change files in the
   same change as its code.
7. **Nothing decided here is re-decided during execution.** A decision that turns out to be
   unimplementable is reported to the owner with the evidence; it is not silently replaced.
8. **Measure before claiming.** Every performance statement in this plan is a target. It becomes a
   claim only through `Benchmark-Plan.md` §28.
9. **The owner commits.** Leave each stage's work in the working tree and report the changed paths
   with a one-line commit message, as `CLAUDE.md` describes.

---

# Progress

Updated by the executor when a stage's gate holds and its report is handed to the owner. A stage is
`closed` only after the owner has committed it.

| Stage | Status | Closed by (commit) |
|---|---|---|
| R0 — Baseline and oracle | not started | |
| R1 — Wire primitives on buffers | not started | |
| R2 — Pipeline on pooled buffers | not started | |
| R3 — Public surface and non-seekable reading | not started | |
| R4 — Typed engine | not started | |
| R5 — Algorithm contracts | not started | |
| R6 — The final format | not started | |
| R7 — Removed | — | — |
| R8 — Generator ground | not started | |
| R9 — Re-gate and release | not started | |

Status values: `not started` · `in progress` · `gate holds — awaiting commit` · `closed` · `blocked — <question>`.

---

# 1. State at entry

| Area | State | Handled in |
|---|---|---|
| `src/` ↔ `System-Contract.md` | In sync | — |
| Tests | 1 668 green in Debug and Release | — |
| Non-minimal 7-bit integers | **Applied.** A longer spelling of the same value is `BinaryFormatException` (`HST-40`, contract §22.1) | — |
| Keyed field order on read | **Applied.** Keys must be strictly ascending; `HashSet<int>` per keyed object removed (`KEY-23`, contract §14.2, §22.3) | — |
| Release workflow | **Applied.** `cd.yml`: stable `vX.Y.Z` only on `main` HEAD; pre-release `-alpha.N`/`-beta.N`/`-rc.N` from any branch on origin; a pre-release of an already released version is refused | — |
| `QA-Plan.md` body ↔ NX rules | Behind: NX fixes are pinned by tests and recorded in §30.3, but the body carries no checkpoints for them | `QA-Plan-Changes.md` §1 |
| `Benchmark-Plan.md` ↔ NX rules | Behind: nothing measures the incremental decompression path or the ratio check | `Benchmark-Plan-Changes.md` §1 |
| Package READMEs | **Release blocker.** `SERIALIZATION-README.md`, `CORE-README.md`, `METAPACK-README.md` are empty; `dotnet pack` fails with NU5040 | R9 |
| Benchmark Track A | Harness built; no baseline captured | R0 |

---

# 2. Why this rework

The first rework moved ownership to the right places — one operation object, one byte monopoly, one
traversal owner, algorithms without policy — and nothing in it is reversed. What it did not change
is the material those owners are made of, and two materials are the cause of nearly every recent fix.

**The byte monopoly is built on `Stream`.** Reading needs a seekable stream because the router peeks
eight bytes and rewinds. A V0 keyed write needs a seekable destination because a field length is
patched through `Stream.Position`. A plain `Serialize<T>(T)` goes through two `MemoryStream`s and two
`ToArray`s, plus a third `MemoryStream` for associated data built even without encryption. Every
`bool` and every varint byte is a virtual call through two stream layers.

**The engine is typed as `object`.** Every primitive is boxed in both directions, every member access
is a delegate call on a boxed value, and several `Add` methods resolve generic metadata per element.

**The symptom:** every fix grew an interface — an `IBufferWriter` overload and
`SupportsIncrementalDecompression` on compression, AAD overloads and `AuthenticatesAssociatedData` on
encryption, a process-wide count cache, a composite surface, `BinaryHeaderPeek`. Doing each of these
after `v1.0.0` would cost a format version or a deprecated overload kept forever. Before it, each
costs only work.

---

# 3. Invariants [D9.15]

Each is held by a structural test. The test is rewritten with the code in the stage that changes the
code, and never removed.

```text
INV-1   one OperationState (a struct passed by ref) per public call; nothing below the pipeline
        builds limits, a budget or keys
INV-2   byte monopoly: WireReader / WireWriter are the only access to payload bytes; a type contract
        receives only MemberWriter / MemberReader, which expose no bytes, no counts and no position
INV-3   a declared length or count is compared with the bytes that can still arrive before it drives
        an allocation; the same rule decides whether an array is allocated at its final length and
        what capacity a collection receives
INV-4   a loop bound over wire data exists only as a validated ElementCount
INV-5   one traversal owner: formatters describe a shape, the engine's codec owns the loop; a type
        contract (reflected or generated) supplies only member order, member access, construction
        and the response to a known key
INV-6   algorithms are pure mechanics, called inside the phase barrier, and never see a limit
INV-7   no process-wide mutable registry can change what an algorithm is
INV-8   only tags travel, never type names
INV-9   every wire field has exactly one encoding: bool is 0 or 1; UTF-8 is strict; varints are
        minimal; keyed field keys are strictly ascending; header service records are in ascending
        number, each number at most once, number 0 invalid; a known service's critical bit matches
        the contract; null is written once, in the first number of the value; no service record
        carries id None; reserved payload-mode bits are zero; a duplicate in a collection is
        malformed
INV-10  the exception taxonomy is complete: nothing on a payload path leaves under a framework name
INV-11  key material is always an owned copy; the serializer clears only what it owns
INV-12  the member plan is a total order over the whole inheritance chain, identical for
        ReflectedContract<T> and for any generated contract (conformance suite CONF-*)
INV-13  limits are policy, validated once; a payload can never raise them
INV-14  the header is authenticated whole: the associated data is the exact header bytes, from the
        first byte of the magic to the last byte of onDiskLength
INV-15  a data or graph error leaves no byte in the destination; encryption starts only after the
        whole payload is in the serializer's buffer
INV-16  the engine never awaits: await exists only at the frame edge; Engine/ and Formatters/ contain
        no asynchronous method
INV-17  boxing happens only in a polymorphic slot ([BinaryUnion], interface, object), verified by a
        counting test double
INV-18  a V0 payload is byte-identical to the V1 payload of the same value without references
```

---

# 4. Decisions at a glance

| Topic | Decision | Source | Plan |
|---|---|---|---|
| Scope | Everything ships in `v1.0.0`; afterwards only additive changes (new algorithm, fix, generator) | D1.1 | — |
| V1 header | A list of service records in canonical order; no separate extension mechanism | D2, D1.3 | §6.1 |
| Associated data | The exact header bytes | D3.1 | §6.2 |
| Null | Folded into the first number of the value | D4.2 | §6.3 |
| Reference frame | One varint, null included | D4.3 | §6.3 |
| V0 | Bare payload: no phases, no references, no header | D5 | §7 |
| Write path | Own pooled buffer and one copy; encrypted frames written straight to the destination | D6 | §5.1 |
| Algorithms | One method per direction, no default members; three new built-ins and one key provider; family-suffixed names | D9.11, D9.12, D9.18 | §8 |
| Public API | Buffer-first surface; `StreamExtensions` and `FromHeader`/`FromStream` removed; `WithKeys`, `Populate`, async at the frame edge | D9.3–D9.10 | §9 |
| Engine | Typed shapes with engine-owned codecs; `TypeContract<T>` seam for a later generator | D8, D9.13 | §10 |
| Allocations | Named targets per path | D9.14 | §11 |
| Stages | Internals R1–R5, format R6; R7 removed | D9.17 | §12 |
| After release | Schema fingerprint, Zstandard/LZ4, generator and its release label | D9.2 | §13 |
| Release workflow | Applied | D7 | §1 |

---

# 5. Target architecture

The layers of `System-Contract.md` §2 stay; their contents change.

```text
Public API       BinarySerializer — write: IBufferWriter<byte> · byte[] · PooledPayload · Stream · PipeWriter
                                    read:  ReadOnlySpan<byte> · ReadOnlySequence<byte> · Stream · PipeReader
      │ one OperationState per call (struct, by ref)
Pipeline (V0|V1) framing · phase order · header services · associated data · phase sizes
      │ phases are transforms over pooled buffers
Engine           typed codecs: depth · nodes · references · null · keyed framing · every loop over wire data
      │ WireReader / WireWriter — ref structs, the only access to payload bytes
Formatters       IScalarFormatter<T> · ISequenceShape<TC,TE> · IMapShape<TM,TK,TV> · typed composites
Contracts        TypeContract<T> — member order, access, construction (ReflectedContract<T> in v1.0)
      │
Algorithms       one interface per family, pure mechanics
```

## 5.1 Buffers and ownership [D6]

- **`PayloadBuffer`** (internal): a segmented writer over `ArrayPool<byte>`, owned by the serializer
  for the whole operation. It supports patching an earlier position in place, which is what keyed
  field lengths need. Patching never happens in a caller's `IBufferWriter<byte>`, whose earlier spans
  the interface does not promise to keep valid.
- **Write path, V0 and V1:** engine → `PayloadBuffer` → phases into pooled buffers → header → one copy
  of the final bytes into the destination. The copy is kept on purpose [D6.2]: a sizing pass costs
  more than the copy; lengths in a trailer would move every length check after allocation; chunked
  framing would need a home-made streaming AEAD; a fixed-width header hides the copy inside
  `ArrayBufferWriter`. The buffer makes every write atomic (INV-15).
- **Encrypted frames skip the copy** [D3.3]: the ciphertext length is known exactly before encryption
  (§8.1), so the header is written first and the algorithm encrypts straight into
  `destination.GetSpan(n)`. For `byte[]` and `Stream` destinations the copy *is* the write.
- **Read path:** the source is, or becomes, a `ReadOnlySequence<byte>`. The header (at most 4 KiB) is
  decoded in place; a phased payload is linearised once into a pooled buffer because AES-GCM and the
  checksum need contiguous input; an unphased payload is decoded straight from the sequence.
- **Streams are adapters.** A `Stream` source is buffered up to one V1 frame; a `Stream` destination
  receives the final bytes.
- Every pooled buffer that held payload bytes is cleared before it is returned.

## 5.2 Metering

`MeteredReadStream`, `MeteredWriteStream` and `WindowReadStream` are deleted. Their guarantees become
properties of the reader and writer:

```text
MaxWireBytes           the adapter buffers at most this much from a source; the writer emits at most
                       this much relative to the operation's start
remaining bytes        WireReader.Remaining is exact — a span or sequence knows its length
keyed field window     WireReader.Slice(length): a reader over exactly the declared field
```

Every rule the stream tests pin today survives, re-expressed against the reader and writer: budget
versus truncation classification, origin-relative write budget, high-water-mark accounting across
patches, window over-read is malformed, unknown keyed fields skipped without materialisation.

---

# 6. Wire format — the final encoding

"varint" means unsigned LEB128, seven bits per byte, the high bit set while more bytes follow, and
**minimal encoding only** (INV-9). All bytes in diagrams are hexadecimal.

## 6.1 V1 header [D2]

```text
magic          4 bytes   42 53 45 52      int32 0x52455342, little-endian — unchanged
version        varint    1 → 01
payload mode   varint    §6.1.1
service count  varint    number of service records
services       records   §6.1.2
onDiskLength   varint    length of the bytes after the header; always present, also with no service
payload        onDiskLength bytes
```

The whole header — magic through `onDiskLength` — is at most **4096 bytes**, a fixed format bound
like today's 256-byte header strings. It is enforced before each service body is read: the body must
fit the remaining header budget, otherwise `BinaryFormatException`. The largest legitimate v1.0
header is about 1.3 KiB. There is no separate bound on a body or on the record count; the 4 KiB bound
covers both. From a non-seekable stream at most 4 KiB is buffered before the header is known to be
valid. [D2.3.8]

Smallest frame — no service, no references, payload `01` (a non-null empty string):

```text
42 53 45 52   magic
01            version
00            payload mode
00            no service
01            onDiskLength = 1
01            payload
```

### 6.1.1 Payload mode

```text
bit 0    references — the payload uses reference frames (§6.3.4)
bit 1+   reserved, must be 0; a set unknown bit → BinaryFormatNotSupportedException
00 — no references, 01 — references
```

References are a property of the frame: the global `PreserveReferences()` option stays, the reader
follows the frame, not its own configuration [D2.2].

### 6.1.2 Service records

```text
kind     varint    (number << 1) | critical
length   varint    body length in bytes
body     length bytes
```

Rules:

- Two classes. A *transform* changes the frame's bytes (compression, encryption) and is always
  critical. An *annotation* describes them (checksum); the contract fixes its criticality. Anything
  that protects or verifies data is critical — a check is never silently skippable [D2.3.1, D2.3.7].
- Records appear in ascending number, each number at most once; otherwise `BinaryFormatException`.
- The order of transforms is fixed by the contract, not by the records: writing is serialize →
  checksum over the raw payload → compress → encrypt; reading reverses it.
- An unknown number with critical = 1 → `BinaryFormatNotSupportedException`; with critical = 0 → the
  body is skipped by its length. For a known number the critical bit must match the contract;
  otherwise `BinaryFormatException`.
- A body is read exactly: bytes left over or read past are `BinaryFormatException`. `length` is
  checked against the remaining header budget and the remaining bytes before the body is read.
- An absent phase is an absent record. A record whose algorithm id is `None` is
  `BinaryFormatException`. Today's "`Compression = None` → lengths equal" and
  "`Encryption = None` → lengths equal" rules disappear with the fields.

Numbers, in the order the phases apply on write; number 0 is reserved so zeroed memory is never read
as a service [D2.3.6]:

```text
number 0   —             kind 00 / 01 → BinaryFormatException
number 1   checksum      annotation   kind 03
number 2   compression   transform    kind 05
number 3   encryption    transform    kind 07
```

### 6.1.3 Service bodies

Every body that names an algorithm starts the same way [D2.4]:

```text
id      varint    the algorithm enum value
name    string    only when id = Custom: varint length, then UTF-8, 1…256 bytes; absent otherwise
```

An empty name with `id = Custom` is `BinaryFormatException`. `Custom` is 255 in all three enums, so
it is the two-byte varint `FF 01`.

**Checksum** (number 1, kind `03`): `id · [name] · hash`. The hash is the remainder of the body; its
length must equal the algorithm's `HashSizeInBytes` (1…255), otherwise `BinaryFormatException`.

```text
03  05  01 9A 3B C1 07        CRC-32 (id 1), 4-byte hash
```

**Compression** (number 2, kind `05`): `id · [name] · uncompressedLength varint`.

```text
05  03  02 E8 07                      Brotli (id 2), uncompressedLength 1000
05  09  FF 01 04 6C 7A 34 78 E8 07    Custom (255), name "lz4x", uncompressedLength 1000
```

**Encryption** (number 3, kind `07`): `id · [name] · keyId`. `keyId` uses the null fold of §6.3.2:
varint (length + 1), `0` meaning no key id, then UTF-8 of at most 256 bytes. The plaintext length is
not declared [D2.4.2].

```text
07  04  01 03 6B 37           AES-256-GCM (id 1), KeyId "k7"
```

Full frame — Brotli and AES-GCM, `KeyId = "k7"`, no references, no checksum:

```text
42 53 45 52              magic
01                       version
00                       payload mode
02                       two service records
05 03 02 E8 07           compression: Brotli, uncompressed 1000
07 04 01 03 6B 37        encryption: AES-GCM, KeyId "k7"
<onDiskLength varint>    = GetCiphertextLength(compressed length)
<ciphertext>             nonce 12 · ciphertext · tag 16
```

### 6.1.4 Order of checks on read [D2.4.1, D2.4.2]

```text
1. header          onDiskLength ≤ MaxEncryptedBytes and ≤ the bytes remaining;
                   compression without encryption — exact: uncompressedLength ≤ onDiskLength × MaxDecompressionRatio
                   compression with encryption    — coarse: uncompressedLength ≤ onDiskLength × MaxDecompressionRatio
                   uncompressedLength ≤ MaxPayloadBytes
2. decryption      output buffer ≤ onDiskLength — memory follows delivered bytes
3. after it        plaintext length ≤ MaxCompressedBytes; uncompressedLength ≤ plaintext length × ratio
4. decompression   the one allocation the ratio protects — only after step 3
5. checksum        verified over the raw payload before the engine reads a byte of it
```

Without compression the payload length is the plaintext length (with encryption) or `onDiskLength`
(without), and it is checked against `MaxPayloadBytes`. `RequireChecksum` and `RequireEncryption` are
satisfied by the presence of the record, and for encryption also by an algorithm that reports
`AuthenticatesAssociatedData = true`; otherwise `BinaryIntegrityException`, as today.

## 6.2 Associated data [D3.1]

The associated data is the exact header bytes on the wire, from the first byte of the magic to the
last byte of `onDiskLength`. There is no separate image; contract §22.7 is deleted. Flipping any
header byte of an encrypted frame fails the tag (INV-14). The associated data never exceeds 4 KiB.

## 6.3 Payload [D4]

### 6.3.1 Numbers

- **Structural numbers** — counts, lengths, reference ids, keys, header numbers — are unsigned
  varints; a negative count cannot be expressed. **Data** — `int`, `double`, … — stays fixed-width
  little-endian. The union tag stays one byte. [D4.1.2]
- **Keyed field:** `varint key · int32 length (little-endian, fixed) · payload`. The length is patched
  after the field is written; a varint would need either a non-minimal encoding or a move of the
  field's bytes, quadratic in the nesting depth. [D4.1.3]
- Rejected: zigzag varints for data (slower for all values, closes the fixed-block copy path); fixed
  width for structural numbers (three bytes more per count, reference and length).

### 6.3.2 Null — folded into the first number [D4.2]

Null is written exactly once, in the first number the value begins with: `0` is null, anything else
is the value plus one. The fold applies only when the declared type can be null — the condition that
today writes a flag.

```text
value                              references off                    references on
string                             varint (UTF-8 length + 1)         same — strings are never framed
sequence (byte[] included), map    varint (count + 1)                the reference frame carries null; count written as is
keyed object                       varint (field count + 1)          the reference frame carries null; field count as is
positional object                  flag byte 00 / 01                 the reference frame carries null
union                              flag byte, then the tag byte      the reference frame carries null, then the tag byte
Nullable<T> (T a value type)       flag byte 00 / 01, then T         same — value types are never framed
type that cannot be null           nothing                           nothing
```

```text
null (string)                          00
""                                     01
"hello"                                06 68 65 6C 6C 6F
List<int> of 3, references off         04 <int32> <int32> <int32>
List<int> of 3, references on          01 03 <int32> <int32> <int32>     first occurrence of id 0, count without + 1
keyed struct with 2 fields             02 ...                            cannot be null: field count without + 1
int? = 5                               01 05 00 00 00
int? = null                            00
```

With references on, the number after the reference frame carries no `+ 1`, because null already
lives in the frame; no value of that number is dead or needs a separate rejection [D4.2.2].

`byte[]` is an ordinary one-dimensional array: a sequence, framed when references are on. The blob
encoding appears only inside `BigInteger` and `BitArray`, which cannot be null, so the fold does not
touch it [D4.2.3].

### 6.3.3 `ImmutableArray<T>` [D4.2.4]

`ImmutableArray<T>` is a struct and cannot be null, but it has two empty states: `default`
(`IsDefault`) and `Empty`. `0` is `default`; anything else is the count plus one. Today's separate
"present" flag disappears.

```text
default                       00
Empty                         01
3 elements                    04 <elements>
ImmutableArray<T>? = null     00          Nullable flag
ImmutableArray<T>? = default  01 00       Nullable flag, then the fold
ImmutableArray<T>? = Empty    01 01
```

Rejected: writing `default` as `Empty` — it changes the value across a round trip.

### 6.3.4 Reference frame [D4.3]

One varint replaces today's marker byte plus `int32`:

```text
0                              null
((id << 1) | 0) + 1            first occurrence of id; the value's payload follows
((id << 1) | 1) + 1            back reference to id; the value ends here

first occurrence of id 0   → 01
back reference to id 0     → 02
first occurrence of id 5   → 0B
```

Ids stay explicit. With implicit ids (the reader counting `next++`), a reader that skips an unknown
keyed field would never assign the ids the writer assigned inside it, and the two counters would
diverge. Reference scopes (contract §16.2) are unchanged.

### 6.3.5 Keyed field order [D4.4] — applied

Keys are strictly ascending. A key not greater than the previous one is `BinaryFormatException` —
repeated ("Duplicate keyed field key") or out of order ("fields must appear in ascending key order") —
for a known key and for one that would be skipped alike. Because the wire and the contract are both
ordered by key, the new engine may find a member with a cursor over the contract instead of a
dictionary.

### 6.3.6 Rejected

Null bitmaps per object or per collection; omitting null keyed fields (the reader could not tell
"null" from "unknown to the writer", and a constructor default would replace the null) [D4.2.5].

## 6.4 Unchanged rules [D9.16]

- Little-endian everywhere.
- Strings: strict UTF-8, now with the folded length of §6.3.2.
- **Root canonicity:** the payload inside a V1 frame is consumed exactly; trailing bytes are
  `BinaryFormatException`. For span and sequence sources the same holds by default for V0, and for
  the frame boundary of V1 (§9.4).
- **Fixtures:** re-frozen once at R6 (§0 rule 4).

---

# 7. V0 — the frameless codec [D5]

V0 is the bare payload: no magic, no version, no service, no length. It exists for protocols that
already frame their messages — a length prefix, a message type, a channel.

- **No phases.** V0 has no compression, checksum or encryption, and none is added.
- **No references.** References are a property of the frame (§6.1.1), and V0 has no frame. A cycle
  under V0 is `BinaryTypeException`, as today.
- **V0 differs from V1 only in what needs metadata** — the header services and the reference mode.
  Everything the payload expresses is identical, and INV-18 pins it byte for byte.
- **Keyed writes to any destination.** V0 writes through the same `PayloadBuffer` as V1 and copies
  once, so a non-seekable destination works. Today's `NotSupportedException` disappears.
- **Where a V0 payload ends when read:**

```text
span / sequence       by default exactly one payload; trailing bytes → BinaryFormatException
bytes-consumed forms  stop at the end of the root and report where (§9.4) — for payloads back to back
seekable Stream       read ahead, then the position is restored to the end of the root
non-seekable Stream   not readable without a length → NotSupportedException
asynchronous read     not supported for V0 (§9.5)
```

- `Build()` keeps rejecting `RequireEncryption`/`RequireChecksum` together with `WithVersion(0)` or
  `AllowV0Fallback`, in both directions: a V0 payload has nothing to verify.

---

# 8. Algorithms [D9.11, D9.12]

## 8.1 Interfaces

One method per direction; no default interface members in v1.0. After the release a member can be
added only with a default implementation — the additive path.

```csharp
public interface ICompressionAlgorithm
{
    CompressionAlgorithm Kind { get; }
    string? CustomName { get; }

    void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination);

    // Writes exactly expectedLength bytes; more, fewer or an unterminated stream is BinaryFormatException.
    void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength);
}

public interface IChecksumAlgorithm
{
    ChecksumAlgorithm Kind { get; }
    string? CustomName { get; }
    int HashSizeInBytes { get; }                                         // 1…255
    void Compute(ReadOnlySpan<byte> source, Span<byte> destination);     // destination.Length == HashSizeInBytes
}

public interface IEncryptionAlgorithm
{
    EncryptionAlgorithm Kind { get; }
    string? CustomName { get; }
    bool AuthenticatesAssociatedData { get; }
    int KeySizeInBytes { get; }

    int GetCiphertextLength(int plaintextLength);                        // exact, and ≥ plaintextLength

    // destination.Length == GetCiphertextLength(plaintext.Length); filled completely.
    void Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key,
                 ReadOnlySpan<byte> associatedData, Span<byte> destination);

    // destination.Length == ciphertext.Length; returns the plaintext length.
    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> associatedData, Span<byte> destination);
}
```

```text
GetCiphertextLength examples   AES-256-GCM: plaintextLength + 28 (nonce 12, tag 16)
                               CBC + HMAC:  16 + roundUp(plaintextLength + 1, 16) + 32
```

Removed: `GetMaxCompressedLength`, `GetMaxCiphertextLength`, the span-to-span `Compress`/`Decompress`,
`SupportsIncrementalDecompression`, `Encrypt`/`Decrypt` without associated data. Rejected: an
`IBufferWriter` destination for encryption (the size is known exactly); `EncryptionGuarantee`
(no policy uses its middle level); default members (they produced the silent failures — associated
data discarded by default). An algorithm that cannot know its ciphertext length in advance cannot be
plugged in.

Checks the services perform:

```text
KeySizeInBytes        a static key — at Build(), BinaryConfigurationException;
                      a provided key — when resolved, BinaryEncryptionKeyException
GetCiphertextLength   negative or below plaintextLength → BinaryConfigurationException
Encrypt               destination not filled exactly → BinaryConfigurationException
Decrypt               result outside 0…ciphertext.Length → BinaryConfigurationException
Decompress            not exactly expectedLength bytes → BinaryFormatException
HashSizeInBytes       outside 1…255 → BinaryConfigurationException (NX-11)
```

## 8.2 Built-ins

```text
compression   DeflateCompression · BrotliCompression            ZLib is not added
checksum      Crc32Checksum · XxHash3Checksum (64-bit) · XxHash128Checksum (128-bit)
                                                                new ones from System.IO.Hashing, already a
                                                                dependency; Crc64 and XxHash64 are not added
encryption    Aes256GcmEncryption · ChaCha20Poly1305Encryption  new one from the BCL: nonce 12, tag 16, key 32
none          NoCompression · NoChecksum · NoEncryption          unchanged
key providers StaticKeyProvider · DelegateKeyProvider · HkdfKeyProvider
                                                                new: message key = HKDF(root key, info = KeyId);
                                                                the root key is never exposed; the derived key is
                                                                an owned copy (SecretKey)
```

**Names** [D9.18]. Every built-in carries its family as a suffix, in the style of `NoChecksum`, so
none collides with a BCL type (`System.IO.Hashing.Crc32`, `XxHash3`, `XxHash128`,
`System.Security.Cryptography.ChaCha20Poly1305`) or another library's. The renames happen in R5:
`Crc32` → `Crc32Checksum`, `Deflate` → `DeflateCompression`, `Brotli` → `BrotliCompression`,
`Aes256Gcm` → `Aes256GcmEncryption`. Enum member names (`ChecksumAlgorithm.Crc32`, …) do not change.

- `ChaCha20Poly1305.IsSupported == false` (the BCL type underneath `ChaCha20Poly1305Encryption`) →
  `BinaryFormatNotSupportedException` naming the algorithm:
  at `Build()` when it is chosen for writing, and when a frame that names it is read.
- New built-ins take the next free enum values; `Custom` stays 255. The default checksum stays none.
- Zstandard, LZ4 and AES-GCM-SIV are not in the `net10.0` BCL; they come after the release as separate
  packages (§13).

---

# 9. Public API [D9.3–D9.10]

## 9.1 `BinarySerializer`

```csharp
public BinarySerializer(BinarySerializerOptions? options = null);

// write
public void          Serialize<T>(IBufferWriter<byte> destination, T value);
public byte[]        Serialize<T>(T value);
public PooledPayload SerializePooled<T>(T value);
public void          Serialize<T>(Stream destination, T value);
public ValueTask     SerializeAsync<T>(Stream destination, T value, CancellationToken cancellationToken = default);
public ValueTask     SerializeAsync<T>(PipeWriter destination, T value, CancellationToken cancellationToken = default);

// read
public T?            Deserialize<T>(ReadOnlySpan<byte> source);
public T?            Deserialize<T>(ReadOnlySpan<byte> source, out int bytesConsumed);
public T?            Deserialize<T>(ReadOnlySequence<byte> source);
public T?            Deserialize<T>(ReadOnlySequence<byte> source, out SequencePosition consumed);
public T?            Deserialize<T>(Stream source);
public ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken cancellationToken = default);
public ValueTask<T?> DeserializeAsync<T>(PipeReader source, CancellationToken cancellationToken = default);

// populate an existing instance
public void          Populate<T>(ReadOnlySpan<byte> source, T target) where T : class;
public void          Populate<T>(ReadOnlySpan<byte> source, T target, out int bytesConsumed) where T : class;
public void          Populate<T>(ReadOnlySequence<byte> source, T target) where T : class;
public void          Populate<T>(ReadOnlySequence<byte> source, T target, out SequencePosition consumed) where T : class;
public void          Populate<T>(Stream source, T target) where T : class;
public ValueTask     PopulateAsync<T>(Stream source, T target, CancellationToken cancellationToken = default) where T : class;
public ValueTask     PopulateAsync<T>(PipeReader source, T target, CancellationToken cancellationToken = default) where T : class;
```

**Removed:** `Deserialize<T>(byte[])` and every `Deserialize` taking an existing instance (class and
`ref` struct); the whole `StreamExtensions` class, including `stream.Serialize`;
`BinarySerializerOptions.FromHeader` ×3 and `FromStream` ×3. **Kept:** `BinaryFormatInspector`.

**Rules for every entry point:**

- An empty input is not a payload: `BinaryFormatException`.
- There is no `byte[]` read overload: an array converts to `ReadOnlySpan<byte>`, so
  `Deserialize<T>(bytes)` and `Populate(bytes, target)` compile unchanged. A `null` array becomes an
  empty span and is rejected as an empty payload (`BinaryFormatException`), not `ArgumentNullException`
  [D9.9].
- Without a bytes-consumed form, a span or sequence is exactly one V0 payload or exactly one V1 frame
  (§9.4).
- Asynchronous reads accept V1 only (§9.5).

## 9.2 Keys for reading [D9.3]

Reading V1 already takes its algorithms from the header. Header-driven configuration existed only
because a key for reading could be supplied solely together with a choice of encryption for writing.
The builder separates the two:

```csharp
public BinarySerializerOptionsBuilder WithKeys(ReadOnlySpan<byte> key, string? keyId = null);
public BinarySerializerOptionsBuilder WithKeys(Func<string?, byte[]?> keyResolver);
public BinarySerializerOptionsBuilder WithKeys(IKeyProvider keys);
```

`WithEncryption(algorithm, key | resolver | provider, keyId)` is unchanged and still supplies keys.
Supplying keys through both `WithEncryption` and `WithKeys` is `BinaryConfigurationException` at
`Build()` — keys have one place.

```csharp
var reader = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithKeys(keyId => vault.Get(keyId))
    .Build());
Order? order = reader.Deserialize<Order>(stream);   // any V1 frame, encrypted or not
```

## 9.3 `PooledPayload` [D9.7]

A sealed class implementing `IDisposable`. It holds a rented array; `Memory` and `Span` are valid until
`Dispose`; `Dispose` is idempotent, clears the bytes and returns the array to the pool; access after
`Dispose` is `ObjectDisposedException`. A struct is rejected: a copy of a struct is a second owner, a
double `Dispose` would return the array twice, and two later operations would share it.

```csharp
using PooledPayload payload = serializer.SerializePooled(order);
await socket.SendAsync(payload.Memory, cancellationToken);
```

## 9.4 Bytes consumed [D9.8]

The native position type of each source: `out int bytesConsumed` for a span, `out SequencePosition
consumed` for a sequence, which goes straight to `PipeReader.AdvanceTo`. With the form, reading stops
at the end of the V0 root or the V1 frame and reports where; without it, trailing bytes are
`BinaryFormatException`. The rule is the same for V0 and V1.

## 9.5 Asynchrony [D9.5, D9.6]

**The engine never awaits** (INV-16). An asynchronous read awaits one whole V1 frame — the header
gives its length — and decodes it synchronously; an asynchronous write builds the frame synchronously
in the serializer's buffer and awaits only the output. A fully asynchronous engine is rejected.

```text
cancellation token   observed while bytes are awaited; decoding a frame already in memory is not
                     interrupted — it is bounded by the limits
PipeReader           on cancellation or failure nothing is consumed: AdvanceTo(frame start, examined end)
Stream (read)        bytes taken are not given back; after cancellation or failure the position is
                     undefined and the stream is unusable for further framing
write                cancellation before output starts leaves nothing (the buffer is atomic);
                     cancellation during WriteAsync / FlushAsync may leave part of a frame — a
                     property of the destination
OperationCanceledException   standard .NET, outside the taxonomy of contract §8
```

Asynchronous methods use `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]`.

**V0 is read synchronously, from the caller's frame.** An asynchronous read that meets V0
(`AllowV0Fallback` on, no magic) throws `NotSupportedException` naming the rule. Asynchronous V0
*writes* are allowed. The following explanation and example are **required** in contract §10.2 and
§3.1 (R3), in the XML documentation of `DeserializeAsync`, `PopulateAsync` and `AllowV0Fallback`, and
in the consumer documentation under `docs/` at release:

> V0 carries neither a magic number nor a length: it is a codec for protocols that already frame their
> messages — a length prefix, a message type, a channel. The protocol knows where a message ends, so
> the caller already holds one message's bytes and reads them synchronously. Waiting asynchronously
> is for a reader that does not know where the message ends; with V0 the protocol knows, not Viper.

```csharp
// V1 from a socket: Viper knows the frame boundary — the header carries the length
Order? order = await serializer.DeserializeAsync<Order>(networkStream, cancellationToken);

// V0 inside your own protocol: the protocol knows the frame boundary
var compact = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithVersion(0).AllowV0Fallback().Build());

while (true)
{
    ReadResult read = await pipe.ReadAsync(cancellationToken);
    ReadOnlySequence<byte> buffer = read.Buffer;

    // the protocol: a 4-byte little-endian length, then the V0 payload
    if (TryReadFrame(ref buffer, out ReadOnlySequence<byte> frame))
    {
        Order? message = compact.Deserialize<Order>(frame);   // synchronous: the frame is in memory
        Handle(message);
    }

    pipe.AdvanceTo(buffer.Start, buffer.End);
    if (read.IsCompleted) break;
}

static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frame)
{
    var reader = new SequenceReader<byte>(buffer);
    if (!reader.TryReadLittleEndian(out int length) || reader.Remaining < length)
    {
        frame = default;
        return false;
    }

    frame = buffer.Slice(reader.Position, length);
    buffer = buffer.Slice(frame.End);
    return true;
}
```

## 9.6 `Populate` [D9.4]

Populates only **member-encoded classes** — a struct is read with `value = serializer.Deserialize<T>(…)`,
which behaves identically. The rules of contract §3.1 carry over: a type with a dedicated formatter is
`BinaryTypeException`; a null root or a root that is a back reference is `BinaryFormatException`; a
different union runtime type is `BinaryTypeException`; an empty input leaves the target untouched. Only
the root is populated — nested objects are created afresh — and a keyed contract keeps the current
value of every field absent from the payload.

---

# 10. The typed engine and the generator seam

## 10.1 Shapes and codecs [D9.13]

Formatters describe a shape; the loop over wire data lives in the engine's codec, so INV-5 is a
property of the code rather than a convention. A single `IFormatter<T>` for every type is rejected:
a collection formatter would own the loop over a count read from the wire. "Shape" is used in the
sense of contract §22.3 — the form a value is encoded in.

```csharp
internal interface IScalarFormatter<T>
{
    void Write(ref WireWriter writer, T value);
    T Read(ref WireReader reader);
}

internal interface ISequenceShape<TCollection, TElement>
{
    int? CountOf(TCollection collection);
    TCollection Create(int capacity);
    void Add(ref TCollection builder, TElement element);
    TCollection Complete(TCollection builder);
}

internal interface IMapShape<TMap, TKey, TValue>
{
    int? CountOf(TMap map);
    TMap Create(int capacity);
    void Add(ref TMap builder, TKey key, TValue value);
    TMap Complete(TMap builder);
}
// objects: TypeContract<T> (§10.2); composites (tuples, KeyValuePair, Lazy, multi-dimensional arrays):
// typed composites over CompositeWriter / CompositeReader, which expose no raw integer (NX-12)

internal sealed class SequenceCodec<TCollection, TElement>(ISequenceShape<TCollection, TElement> shape)
{
    public TCollection Read(ref WireReader reader, ref OperationState state)
    {
        ElementCount count = reader.ReadCount(CountKind.Collection, ref state);
        var builder = shape.Create(count.CapacityHint);
        for (int i = 0; i < count.Value; i++)
            shape.Add(ref builder, Engine.Read<TElement>(ref reader, ref state));
        return shape.Complete(builder);
    }
}
```

`FormatterCache<T>.Instance` holds one codec per `T` — scalar, sequence, map, object, `Nullable<T>`,
polymorphic slot — built once; a lookup is a static field read.

**Enumeration on write:** `T[]`, `List<T>` and `ImmutableArray<T>` through a span
(`CollectionsMarshal.AsSpan`); BCL collections with a struct enumerator (`HashSet<T>`,
`Dictionary<K,V>`, `Queue<T>`, `Stack<T>`, `SortedSet<T>`, …) through it, in their own shape; anything
else through `IEnumerable<T>`, which costs one enumerator allocation.

**Arrays on read:** when `count × sizeof(T) ≤ the bytes remaining` — an element in memory is no larger
than on the wire, as for primitives and fixed-width unmanaged structs — the array is allocated at its
final length and read into directly. Otherwise elements accumulate in a rented buffer and one array of
the final length is created at the end. NX-01's rule holds on both paths.

**Collection capacity on read:** when `count × the element's minimum wire size ≤ the bytes remaining`,
the capacity is `count`; otherwise growth starts from `CapacityHint` (at most 1 024) [D9.14].

**Boxing** happens only in a polymorphic slot (INV-17). Enums are converted through `Unsafe.As`, not
`Convert.ChangeType`.

## 10.2 The generator seam [D8]

In v1.0 the object seam has the *shape of a method*: writing and reading members are methods of the
type's contract, not an engine loop over a list of delegates. Only reflection implements it in v1.0.
A later source generator implements the same seam additively. The seam is `internal`; what becomes
public is decided when the generator ships. A method shape is strictly more general than a data
shape: a list of member descriptions can always be run by a method with a loop, but straight-line
code without delegates cannot be expressed as a list.

```csharp
internal abstract class TypeContract<T> : TypeContract    // TypeContract: today's description — layout, members, keys, constructibility
{
    // Creates an instance to read into: the parameterless constructor; default for a struct.
    public abstract T Create();

    // Positional: one writer.Member call per member, in plan order.
    // Keyed: one writer.Field call per member, in ascending key order.
    public abstract void Write(ref MemberWriter writer, in T value);

    // Positional only: one reader.Member call per member, in plan order.
    public abstract void Read(ref MemberReader reader, ref T value);

    // Keyed only. Called by the engine for each field on the wire; false means the key is unknown
    // to this contract, and the engine skips the field by its length.
    public abstract bool ReadField(ref MemberReader reader, int key, ref T value);

    // Polymorphic slot: the one place the engine boxes.
    internal sealed override void WriteBoxed(ref MemberWriter writer, object value) => Write(ref writer, (T)value);
}

internal ref struct MemberWriter
{
    public void Member<TMember>(TMember value);            // positional member: the whole value, with null / frame
    public void Field<TMember>(int key, TMember value);    // keyed field: key, int32 reserved, value, length patched
}

internal ref struct MemberReader
{
    public TMember Member<TMember>();                      // positional member
    public TMember Value<TMember>();                       // the current keyed field's value, exactly once per ReadField
}
```

Reading a polymorphic slot mirrors `WriteBoxed`: the engine maps the union tag to the runtime type's
contract and calls its non-generic entry, which is typed inside. Names: `TypeContract<T>` is an
abstract class so members with default implementations can be added after publication without
breaking generated code; `IObjectPlan<T>` and `ObjectShape` were rejected [D8.3].

**The division of labour:**

```text
the contract supplies   member order, member access, instance creation, the response to a known key
the engine owns         null and the reference frame, the union tag, depth, budgets, cycles, the keyed
                        field count, keys and field lengths, the field window, the loop over fields on
                        the wire, skipping unknown keys, "the field was read exactly"
MemberWriter / Reader   expose no bytes, no counts, no position — only a member's value
the engine checks       every call against the contract's description; a mismatch is
                        BinaryTypeException, never a distorted wire
```

**The engine's checks:**

```text
write, positional   the i-th Member<TMember> call ⇒ typeof(TMember) == Members[i].MemberType;
                    after Write: calls == Members.Length
write, keyed        the i-th Field<TMember>(key) call ⇒ key == Members[i].Key (the description is ordered
                    by key) and the type matches; after Write: calls == Members.Length
read, positional    as for write, for Member<TMember>()
read, keyed         ReadField returned true ⇒ Value<TMember>() was called exactly once, with the type of
                    that key's member, and the field window was consumed exactly;
                    returned false ⇒ Value was not called
any mismatch        BinaryTypeException naming the type and the member — a contract error, not a data error
```

The `typeof(TMember)` comparison in a generic method folds to a constant; the cost is a counter and a
comparison per member.

**Engine order, writing an object of declared type `D` with value `v`:**

```text
1. null and the reference frame (§6.3.2, §6.3.4)
2. depth + 1, node budget − 1, cycle detection
3. runtime type R ≠ D: the union tag byte, then R's contract
4. keyed: the field count varint (+ 1 when D can be null and references are off)
5. contract.Write(ref writer, v) — the contract calls only Member / Field
6. the call count check
```

**Reading** mirrors it, with two ordering rules: the instance is created (`Create`) and registered in
the reference table **before** its members are read, so a cycle back to it resolves — under
`Populate` the supplied instance replaces `Create`; and for a keyed contract the engine reads the
field count, then for each field the key, the `int32` length, the check against remaining bytes and a
window over exactly the field, calls `ReadField`, and on `true` requires the window consumed exactly,
on `false` skips by the length.

**The reflection implementation:**

```csharp
internal sealed class ReflectedContract<T> : TypeContract<T>
{
    private readonly MemberAccessor<T>[] _accessors;                  // one per member, in description order
    private readonly FrozenDictionary<int, MemberAccessor<T>> _byKey;  // keyed only

    public override void Write(ref MemberWriter writer, in T value)
    {
        foreach (var accessor in _accessors)
            accessor.WriteTo(ref writer, in value);
    }

    public override void Read(ref MemberReader reader, ref T value)
    {
        foreach (var accessor in _accessors)
            accessor.ReadFrom(ref reader, ref value);
    }

    public override bool ReadField(ref MemberReader reader, int key, ref T value) =>
        _byKey.TryGetValue(key, out var accessor) && accessor.ReadFieldFrom(ref reader, ref value);
}

internal delegate TMember Getter<T, TMember>(in T owner);
internal delegate void Setter<T, TMember>(ref T owner, TMember value);

internal abstract class MemberAccessor<T>
{
    public abstract void WriteTo(ref MemberWriter writer, in T owner);
    public abstract void ReadFrom(ref MemberReader reader, ref T owner);
    public abstract bool ReadFieldFrom(ref MemberReader reader, ref T owner);
}

internal sealed class MemberAccessor<T, TMember> : MemberAccessor<T>
{
    private readonly Getter<T, TMember> _get;
    private readonly Setter<T, TMember> _set;
    private readonly int? _key;

    public override void WriteTo(ref MemberWriter writer, in T owner)
    {
        if (_key is { } key)
            writer.Field(key, _get(in owner));
        else
            writer.Member(_get(in owner));
    }

    public override void ReadFrom(ref MemberReader reader, ref T owner) =>
        _set(ref owner, reader.Member<TMember>());

    public override bool ReadFieldFrom(ref MemberReader reader, ref T owner)
    {
        _set(ref owner, reader.Value<TMember>());
        return true;
    }
}
```

- `MemberAccessor<T, TMember>` is created once per member through `MakeGenericType`; the getter and
  setter are compiled once. Nothing is boxed.
- The setter takes `ref T`, so a struct owner is assigned in place rather than in a copy.
- Order and keys follow today's `TypeContractCache.Build` rules (INV-12: `[BinaryOrder]`, then ordinal
  name, then inheritance level, base first) with every rejection it makes today.
- The reflection path's public entry points carry `[RequiresDynamicCode]` and
  `[RequiresUnreferencedCode]` (R8).

**Illustration — not normative.** What a generator might emit after the release, to show the seam is
sufficient. Registration and access to non-public members are decided then (§13):

```csharp
[BinaryContract]
public partial class Person
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public int Age { get; set; }
    [BinaryKey(3)] public Person? Manager { get; set; }
}

// generated, Person.Viper.g.cs
partial class Person
{
    private sealed class __ViperContract : TypeContract<Person>
    {
        public __ViperContract() : base(MemberLayout.Keyed,
            Member<string>(key: 1, "Name"), Member<int>(key: 2, "Age"), Member<Person?>(key: 3, "Manager")) { }

        public override Person Create() => new();

        public override void Write(ref MemberWriter w, in Person v)
        {
            w.Field(1, v.Name);
            w.Field(2, v.Age);
            w.Field(3, v.Manager);
        }

        public override void Read(ref MemberReader r, ref Person v) => throw new NotSupportedException();

        public override bool ReadField(ref MemberReader r, int key, ref Person v)
        {
            switch (key)
            {
                case 1: v.Name = r.Value<string>(); return true;
                case 2: v.Age = r.Value<int>(); return true;
                case 3: v.Manager = r.Value<Person?>(); return true;
                default: return false;
            }
        }
    }

    [ModuleInitializer]
    internal static void __ViperRegister() => ContractRegistry.Register(new __ViperContract());
}
```

```text
Serialize(person), references off:
  varint field count 04 (3 + 1)
  w.Field(1, "Ada")  → 01 · 04 00 00 00 · 04 41 64 61
  w.Field(2, 36)     → 02 · 04 00 00 00 · 24 00 00 00
  w.Field(3, null)   → 03 · 01 00 00 00 · 00
  check: 3 calls, keys 1, 2, 3
```

---

# 11. Allocation targets [D9.14]

**Mechanisms:**

```text
cycle detection on write     an ancestor stack of the current path (a pooled array no longer than
(references off)             MaxDepth) with a linear search by reference; a cycle is BinaryTypeException,
                             as today; the per-operation HashSet is removed
reference tables             pooled per operation, cleared and returned
collection capacity on read  §10.1
async state machines         PoolingAsyncValueTaskMethodBuilder
AesGcm / ChaCha20Poly1305    a new instance per operation, so the key schedule lives only inside the
                             call; a per-key cache is rejected
```

**Targets after warm-up.** Each line is a benchmark checkpoint and a target until measured:

```text
write to IBufferWriter, V1 or V0, no phase, a record of primitives and strings   0 bytes
the same with Brotli                                                             0 bytes of managed memory
the same with Deflate                                                            one DeflateStream
the same with encryption                                                         one AesGcm / ChaCha20Poly1305
Serialize<T>(T) → byte[]                                                         exactly the returned array
SerializePooled                                                                  one PooledPayload
read from a span, a record of primitives                                         exactly the returned object
read a graph                                                                     exactly the graph — no intermediate
                                                                                 copy or reallocation when the count
                                                                                 is backed by bytes
with PreserveReferences                                                          as without
asynchronous methods                                                             as the synchronous ones
first use of a type                                                              its codec and contract, once
```

What cannot reach zero and is not pretended to: the result graph, strings, the backing store of every
materialised collection, and the first use of a type.

---

# 12. Stages [D9.17]

Internals first (R1–R5), then the format (R6): the encoding code is written once, directly in the new
types, and the old fixtures and the oracle catch any unintended change on the way. "Wire unchanged"
means the thirteen fixtures and the oracle pass byte for byte. Each stage applies its part of the
three change files.

### R0 — Baseline and oracle — *wire unchanged*

- Tag the entry commit locally as `pre-rework` (not `v*`, so CD never sees it even if pushed), run the
  complete Benchmark Track A so the harness records a Baseline, delete the tag, and commit the raw
  results under `benchmarks/ViShap.Viper.Serialization.Benchmarks/Baselines/pre-rework/`.
- Record the byte oracle: one text file holding the SHA-256 of the writer's output for every case of
  the corpus — every §23 type family and every graph shape, under V0 and V1, with references on and
  off. A test compares against it and, on a mismatch, prints the hex of both outputs for that case.
  The oracle is deleted at R6.
- **Gate:** baseline committed; oracle committed; suite green.

### R1 — Wire primitives on buffers — *wire unchanged*

- `WireReader` / `WireWriter` as `ref struct`s over `ReadOnlySequence<byte>` / `ReadOnlySpan<byte>`
  and `PayloadBuffer`. `ValueReader` / `ValueWriter` become thin adapters, then are deleted once no
  caller remains.
- The structural tests for INV-2, INV-3 and INV-4 are rewritten for the new types.
- **Gate:** fixtures and oracle byte-identical; no virtual call per byte on the write path.

### R2 — Pipeline on pooled buffers — *wire unchanged*

- V0 and V1 write into `PayloadBuffer`; keyed lengths patched in place; atomic writes (INV-15).
- `MeteredReadStream`, `MeteredWriteStream`, `WindowReadStream` and every `MemoryStream` on the
  payload path are deleted (§5.2).
- Cycle detection by ancestor stack; pooled reference tables (§11).
- **Gate:** a V0 keyed write to a non-seekable destination succeeds; no `MemoryStream` under
  `Pipeline/`; an error in the middle of a graph leaves the destination empty; fixtures and oracle
  byte-identical.

### R3 — Public surface and non-seekable reading — *wire unchanged*

- The whole surface of §9: buffer and sequence entry points, `PooledPayload`, bytes-consumed forms,
  `Populate`, asynchronous methods, `WithKeys`. `StreamExtensions` and `FromHeader`/`FromStream` are
  deleted; `BinaryHeaderPeek` is deleted and the router decodes the magic from the buffered source.
- The V0 boundary rules (§7) and the V0 asynchronous rule with its required text (§9.5).
- **Gate:** every entry point reads a non-seekable source; a non-seekable double that fails on any
  read past the frame proves exactly one V1 frame is consumed; `Api/CrossEntryPointTests` covers every
  entry point; `Api/PublicSurfaceTests` matches contract §3.

### R4 — Typed engine — *wire unchanged*

- §10.1 and §10.2: shapes and codecs, `FormatterCache<T>`, `TypeContract<T>` / `ReflectedContract<T>`,
  `MemberWriter` / `MemberReader` with the engine's checks, array and capacity rules, enum conversion
  without boxing, pooled asynchronous builders.
- **Gate:** fixtures and oracle byte-identical; INV-5 and INV-17 structural tests pass; the §11
  targets that do not depend on R6 are measured; cold start (`ContractColdRunner`) measured against
  R0, and any regression written up in `internal/performance/`.

### R5 — Algorithm contracts — *wire unchanged*

- §8: the interfaces, the existing built-ins ported and renamed (§8.2), the new built-ins,
  `HkdfKeyProvider`, the services' checks.
- **Gate:** every built-in round-trips through the pipeline and resolves from the catalog;
  `KeySizeInBytes` is enforced at `Build()`; no interface has a default member.

### R6 — The final format — *the break*

- §6 entire: the header with services and its 4 KiB bound, the associated data, varint structural
  numbers, the null fold, the reference frame, `ImmutableArray<T>`.
- Re-freeze the fixtures once, from the corpus; record the exception in `CLAUDE.md` and restore the
  rule in the same change. Delete the oracle.
- **Gate:** every rule of contract §22 is pinned by a byte-level test; the new fixtures are committed;
  sizes (Track A §14) re-measured against R0 and the difference published in `internal/performance/`.

### R7 — Removed

The V0 phases it carried are cancelled (§7); what remained is in R2 (keyed writes to any destination)
and R3 (the read boundary).

### R8 — Generator ground

- The conformance suite `CONF-*`: for every object shape of the corpus — positional, keyed, inherited,
  shadowed, overridden, union, struct — the member order, keys, layout and bytes, written so that a
  generated contract can be run against the same cases.
- `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` on the reflection path's public entry points.
- **Gate:** `CONF-*` passes on `ReflectedContract<T>`; a consumer project built with AOT analysis
  reports the annotated entry points and nothing unannotated.

### R9 — Re-gate and release

- `System-Contract.md`, `QA-Plan.md` and `Benchmark-Plan.md` reconciled completely from the change
  files; `Architecture-Audit.md` marked as the historical record of the first rework.
- The three package READMEs written.
- Track A re-measured on the release candidate.
- A CD dry run.
- **Gate:** contract §24 fully checked; QA plan §32 fully checked; `dotnet pack` succeeds for all three
  packages.

Track B — the comparison with other serializers — runs after the release, on the `v1.0.0` tag.

---

# 13. After the release

Each of these is additive; none blocks a stage [D9.2, D8.8]:

- **Schema fingerprint** — a new critical service with a new number.
- **Zstandard, LZ4, AES-GCM-SIV** — separate packages with their own dependencies.
- **Source generator** — implements `TypeContract<T>`. Decided at that time: whether `TypeContract<T>`,
  `MemberWriter` and `MemberReader` become public; access to non-public `[BinaryInclude]` members
  (`partial` or `[UnsafeAccessor]`); registration (module initializer and registry, a static abstract
  member on the type, or a context in the options); the release label.
- **Benchmark Track B.**

---

# 14. Risks

| Risk | Where | Mitigation |
|---|---|---|
| The typed engine costs more cold start and code size than it saves | R4 | Measured against R0 before the stage closes; `ContractColdRunner` exists |
| A typed shape quietly takes a loop back from the engine | R4 | Shapes have no access to counts; INV-5 structural tests extended in the same stage |
| `ref struct` readers and writers reshape the whole engine, not one type | R1, R4 | Expected; the engine becomes `ref struct`s or static generic methods. The stages are split so each ends green |
| Internal stages change behaviour unnoticed | R1–R5 | The wire does not change before R6; fixtures and the oracle catch it |
| The fixture exception becomes a habit | R6 | Used once, recorded in `CLAUDE.md`, the rule restored in the same change |
| Over-read from non-seekable sources | R3 | Designed in §7 and §9.5; tested with a double that fails on any read past the frame |
| A custom algorithm cannot state its ciphertext length | R5 | Documented as unsupported (§8.1); every AEAD in the BCL states it |
| The plan drifts from the code | all | Every stage rewrites its documents from the change files, in the stage |

---

# 15. Out of scope

- Implementing the source generator.
- A public formatter contract.
- A MAC family separate from authenticated encryption.
- Any native dependency in the three core packages.
- Comparison with other serializers before the release.
