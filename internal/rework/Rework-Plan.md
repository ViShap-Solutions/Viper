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
Claude-Changes.md            every change CLAUDE.md receives, by stage
Retired.md                   every name, rule and term the rework removed, for the R9 sweep
Conformance-Audit-Brief.md   what the R9 conformance auditor rests on, asks and delivers
Decisions.md                 the owner's decisions, in Russian, with the byte diagrams and examples
                             they were taken on — the source this plan was written from
Owner-Review.md              the review of the previous plan and the owner's decision log (Russian)
```

**Precedence.** Where this plan and `Decisions.md` disagree, `Decisions.md` is right and this plan
is defective; fix the plan, do not choose. Every section below cites the decision it implements as
`[Dn.n]`, meaning `Decisions.md` §n.n.

The four change files are separate from the documents they amend on purpose. `System-Contract.md`,
`QA-Plan.md`, `Benchmark-Plan.md` and `CLAUDE.md` describe what the code does *now*; each is rewritten
from its change file in the stage that makes the change real, never ahead of it [D9.24].

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
6. **Documents change with the code.** Each stage applies its part of the four change files in the
   same change as its code — `CLAUDE.md` included, so no agent reads a description of a system that
   no longer exists [D9.24].
7. **Nothing decided here is re-decided during execution.** A decision that turns out to be
   unimplementable is reported to the owner with the evidence; it is not silently replaced.
8. **Measure before claiming.** Every performance statement in this plan is a target. It becomes a
   claim only through `Benchmark-Plan.md` §28.
9. **The owner commits.** Leave each stage's work in the working tree and report the changed paths
   with a one-line commit message, as `CLAUDE.md` describes.
10. **Branches and tags** follow `internal/Development-Workflow.md` [D9.22]. The rework is collected on
    `release/v1.0.0`, branched from `main`; each stage is worked on `rework/rN-<topic>` from
    `release/v1.0.0` and returns to it through a pull request. Nothing is published before R6; after
    R6 no beta is tagged — the owner decided on 2026-09-29 not to set `v1.0.0-beta.1`, because the package READMEs that `dotnet pack` requires are written only in R9 (`Owner-Review.md` log 64); after R9, `v1.0.0-rc.N`; the release is
    `release/v1.0.0` merged into `main` and `v1.0.0` tagged on `main`. The executor never creates or
    merges these branches and never tags `v*`.
11. **The benchmark harness lives with the code** [D9.27, D9.28]. At the end of every stage — R1–R6, R8
    and R9 — it builds, `--verify` passes every pair and `--smoke` passes; a checkpoint whose measured API the stage
    removes is rewritten or retired in that stage (`Benchmark-Plan-Changes.md` §2).
12. **Nothing of the previous system survives silently** [D9.28]. Every stage records in
    `Retired.md` what it removed or renamed — types, members, options, arguments, terms, rules,
    checkpoints — in the same change. R9 does not trust the change files: it reconciles every living
    document against the code independently, and searches the repository for every `Retired.md`
    entry.

---

# Progress

Updated by the executor when a stage's gate holds and its report is handed to the owner. A stage is
`closed` only after the owner has merged its branch into `release/v1.0.0`.

| Stage | Branch (from `release/v1.0.0`) | After the owner merges it | Status | Closed by (merge commit) |
|---|---|---|---|---|
| R0 — Baseline and oracle | `rework/r0-baseline` | — | closed | `530262a` |
| R1 — Wire primitives on buffers | `rework/r1-wire-primitives` | — | closed | `1a73c62` |
| R2 — Pipeline on pooled buffers | `rework/r2-pooled-pipeline` | — | closed | `7ac46c0` |
| R3 — Public surface and non-seekable reading | `rework/r3-public-surface` | — | closed | `48c7bf5` |
| R4 — Typed engine | `rework/r4-typed-engine` | — | closed | `c215131` |
| R5 — Algorithm contracts | `rework/r5-algorithm-contracts` | — | closed | `76b9aa5` |
| R6 — The final format | `rework/r6-final-format` | no tag: `v1.0.0-beta.1` deliberately not set (owner, 2026-09-29, `Owner-Review.md` log 64) | closed | `4a7d820` |
| R7 — Removed | — | — | — | — |
| R8 — Generator ground | `rework/r8-generator-ground` | — | closed | `a41f64a` |
| R9a — Reconciliation | `rework/r9a-reconcile` | — | not started | |
| R9b — Consumer documentation | `docs/v1-consumer-docs` | — | not started | |
| R9c — Conformance audit | `audit/v1-conformance` (separate session) | — | not started | |
| R9d — Audit fixes | `bugfix/v1-audit-<topic>`, one per group; skipped if nothing was found | — | not started | |
| R9e — Closure check | `audit/v1-conformance-closure` (separate session) | the owner may tag `v1.0.0-rc.1` on `release/v1.0.0` | not started | |
| Release | `release/v1.0.0` → `main` | the owner tags `v1.0.0` on `main` | not started | |

A defect found on a beta or an rc is fixed on `bugfix/<topic>` from `release/v1.0.0` and merged back;
the next tag is `beta.N+1` or `rc.N+1`.

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
| Package READMEs | **Release blocker.** `SERIALIZATION-README.md` and `METAPACK-README.md` are empty and `dotnet pack` fails on them with NU5040. `CORE-README.md` is empty too, but the Core package packs the repository's `README.md` instead | R9 |
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

The types the table leaves out follow the same rule, by the owner's decision of 2026-09-28
(`Owner-Review.md` log 60): the scalars that travel as a string — `Uri`, `Version`, `StringBuilder`,
`CultureInfo`, `TimeZoneInfo` — fold the string's length, like `string`, and are never framed;
`BitArray` is a class and folds its bit count (a varint) — [D4.2.3] calls it non-nullable, which it is
not; an array of rank greater than one folds its rank, and with references on writes it after the frame
as is; `Tuple<…>` and `Lazy<T>` begin with no number and take the flag byte, as a positional object
does. Rank, dimension lengths and the bit count are structural numbers, so varints.

```text
Uri "a"                  02 61                  null 00
BitArray of 9 bits       0A 02 8D 01            null 00
int[2,3]                 03 02 03 <6 × int32>   null 00
Tuple<int,string>        01 <int32> 04 ...      null 00
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

    // destination.Length == GetCiphertextLength(plaintext.Length); filled completely;
    // returns the number of bytes written.
    int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key,
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
Encrypt               result other than destination.Length → BinaryConfigurationException
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
                                                                an owned copy (SecretKey); HKDF-SHA-256,
                                                                info = UTF-8(KeyId), key size a parameter
                                                                (32 by default), optional salt, a null KeyId
                                                                is BinaryEncryptionKeyException
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
public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(Stream source, CancellationToken cancellationToken = default);
public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(PipeReader source, CancellationToken cancellationToken = default);

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

**Reading a stream of frames** [D9.21]. `DeserializeAsyncEnumerable<T>` reads V1 frames until the
source ends. Each frame is its own operation — its own `OperationState`, limits and budgets — so a
stream of frames is unbounded while every frame stays bounded. The source ending exactly between
frames completes the enumeration; ending inside a frame is `BinaryFormatException`. V0 is
`NotSupportedException`, for the reason above. Cancellation behaves as for `DeserializeAsync`; a
`PipeReader` does not consume a frame it has started. This is what a loop over `DeserializeAsync`
cannot do: a clean end of stream there is an empty input, indistinguishable from a broken one.

```csharp
await foreach (Order? order in serializer.DeserializeAsyncEnumerable<Order>(networkStream, cancellationToken))
    Handle(order);
```

Limits apply per frame, never per connection. A single message above 2 GiB, or a single value that
never ends, is out of scope by design: messages are materialised object graphs, and large or endless
data is sent as many frames.

## 9.6 `Populate` [D9.4]

Populates only **member-encoded classes** — a struct is read with `value = serializer.Deserialize<T>(…)`,
which behaves identically. The rules of contract §3.1 carry over: a type with a dedicated formatter is
`BinaryTypeException`; a null root or a root that is a back reference is `BinaryFormatException`; a
different union runtime type is `BinaryTypeException`; an empty input leaves the target untouched. Only
the root is populated — nested objects are created afresh — and a keyed contract keeps the current
value of every field absent from the payload.

## 9.7 Diagnostics [D9.32]

**Why.** A binary frame cannot be read by eye the way JSON or XML can. `ViShap.Viper.Diagnostics` is
what closes that gap: it turns a frame into text a person can read — what was written, where, and why
it does not read back. Today it renders five header fields. After R6 it renders the header, every
phase, and the payload as a tree, and it points at the byte and the member where a read fails.

**Surface** (namespace `ViShap.Viper.Diagnostics`, all new public types listed in contract §3):

```csharp
public static class BinaryFormatDumper
{
    // The header alone; no type, no key. The byte[] overload goes: an array converts to a span.
    public static string DumpHeader(ReadOnlySpan<byte> frame);
    public static string DumpHeader(ReadOnlySequence<byte> frame);
    public static string DumpHeader(Stream source);                       // seekable; position restored

    // The whole frame without a type: header, then each phase undone with the options' keys and
    // verified, then the payload as annotated hex. Bytes without the magic are shown as V0 hex.
    public static BinaryDump Dump(ReadOnlySpan<byte> frame, BinarySerializerOptions? options = null);

    // The whole frame with T as the schema: the payload as a tree.
    public static BinaryDump Dump<T>(ReadOnlySpan<byte> frame, BinarySerializerOptions? options = null);
    public static BinaryDump Dump<T>(ReadOnlySequence<byte> frame, BinarySerializerOptions? options = null);

    // Writes value with the options, then dumps what was written: "what exactly went on the wire".
    public static BinaryDump DumpValue<T>(T value, BinarySerializerOptions? options = null);

    // The first node where two frames of T differ, or null when they decode to the same tree.
    public static BinaryDumpDifference? Compare<T>(
        ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, BinarySerializerOptions? options = null);
}

public sealed class BinaryDump
{
    public int FormatVersion { get; }                    // 0 or 1
    public BinaryHeaderInfo? Header { get; }             // null for V0
    public int HeaderLength { get; }
    public int PayloadLength { get; }                    // after decryption and decompression
    public bool? ChecksumVerified { get; }               // null: no checksum, or not reached
    public bool? Decrypted { get; }                      // null: not encrypted; false: no key given
    public BinaryDumpNode? Root { get; }                 // null without T; partial on failure
    public BinarySerializerException? Failure { get; }   // null when the frame read completely
    public long? FailureOffset { get; }                  // offset in the payload
    public string? FailurePath { get; }                  // "Order.Lines[2].Note"
    public int MaxDepth { get; }                         // measured from the tree
    public int NodeCount { get; }
    public override string ToString();                   // the text report below
    public string ToJson();                              // the same, as JSON
    public string ToXml();                               // the same, as XML (Owner-Review.md log 62)
    public string ToHex();                               // 16 bytes a line, each line labelled with its node
}

public sealed class BinaryDumpNode
{
    public string Name { get; }                          // member name, "[3]", "{key}", "root"
    public string TypeName { get; }                      // the declared type; the runtime type in a union
    public BinaryDumpNodeKind Kind { get; }
    public long Offset { get; }                          // in the payload
    public int Length { get; }                           // bytes the node occupies on the wire
    public string? Value { get; }                        // a scalar, rendered invariantly; strings cut at 256 chars
    public int? Key { get; }                             // keyed field
    public byte? UnionTag { get; }
    public int? ReferenceId { get; }                     // with PreserveReferences
    public string? ReferenceTarget { get; }              // the path a back reference points to
    public IReadOnlyList<BinaryDumpNode> Children { get; }
}

public enum BinaryDumpNodeKind
{
    Null, Scalar, Sequence, Map, Object, KeyedObject, KeyedField, UnknownKeyedField, Union,
    BackReference, Composite
}

public sealed class BinaryDumpDifference
{
    public string Path { get; }
    public BinaryDumpNode? Expected { get; }
    public BinaryDumpNode? Actual { get; }
}
```

`BinaryHeaderInfo` reports the service records as flat members, by the owner's decision of 2026-09-28
(`Owner-Review.md` log 61): beside the version, the algorithms, their custom names and the key id, it
carries `PreserveReferences`, `UncompressedLength` (`int?`, null without compression), `Checksum`
(`ReadOnlyMemory<byte>`, empty without a checksum), `HeaderLength` and `OnDiskLength`. An unknown
non-critical service is skipped and not reported.

**The text report** — `ToString()`, the form a person reads:

```text
Viper V1 frame · 1 069 bytes · header 41 bytes
  compression  Brotli         4 096 → 1 000 bytes (×4.1)
  checksum     XxHash3        9F 2C 71 04 BE 55 03 3A   verified
  encryption   Aes256Gcm      key id "2026-q3"   decrypted, header authenticated
payload 4 096 bytes as Order · depth 4 · 38 nodes
@0000  Order                         object · 5 members
@0001  ├─ Id          Int64          42
@0002  ├─ Customer    String         "Alice"                 6 bytes
@0008  ├─ Lines       List<Line>     3 items
@0009  │  ├─ [0]      Line           object · 2 members
…
@0F3A  └─ Note        String         ✗ BinaryFormatException: invalid UTF-8
failure at @0F41 (3905) in Order.Lines[2].Note
```

**How it works.** One internal seam in the engine: `OperationState.Trace`, an internal observer that
is `null` for every serializer call and set only by the dumper. The codecs of `Engine/` — the entry of
every value (null flag, reference frame, union tag), the sequence, map, array and composite loops, the
object codec per member through `MemberReader`, the keyed codec per field — report `Enter` (name,
declared type, kind, offset), `Value<T>` (a scalar, generic, so nothing is boxed — INV-17) and `Exit`
(offset). Offsets come from `WireReader.Consumed`, and the bytes of a node reach the observer only as a
read-only view `WireReader` hands out (INV-2). Formatters and type contracts do not change: the codec
that owns the loop is the only caller (INV-5). The read itself is the ordinary read, under the
options' limits and budgets; a failure leaves the observer's stack open, which is where the path and
the offset of the failure come from, and the exception is kept rather than thrown.

**Rules.**

- The dumper never throws for a malformed or hostile frame: every `BinarySerializerException` is
  reported in `Failure`. It throws `ArgumentNullException` for a null argument and
  `NotSupportedException` for a stream that cannot seek, as today (§19 of the contract).
- It reads under the options' limits (`SerializationLimits.Default` when none are given), so a hostile
  frame costs the dumper what it would cost a reader. The tree is bounded by the node and element
  budgets; string values are cut at 256 characters and blobs at 64 bytes in the tree (`ToHex` shows
  every byte of the payload, bounded by `MaxWireBytes`).
- Decryption uses the options' keys; without a key the phases stop at the ciphertext, `Decrypted` is
  `false`, and the report says so. Key material is never rendered. A decrypted dump shows plaintext:
  the XML documentation says so.
- Values render with the invariant culture, and times in UTC, so a dump is the same on every machine.
- `Compare<T>` walks both trees in order and reports the first node whose kind, type, value, key, tag
  or child count differs.

**Where it lands.** R6, as its last step, after the format and the re-frozen fixtures: the offsets the
tests pin are then the final ones, and the header it renders is the service-record header. Before the
step, the read cells of the profile matrix are measured; after it, again — the difference is what the
switched-off seam costs, and a measurable difference stops the stage for the owner (alternatives: a
generic struct observer the JIT removes, or the seam only in a separate diagnostic codec set).

**Not in v1.0.** A command-line tool (`dotnet viper dump`), which needs a type loaded from an assembly;
reading a payload without a type from a schema file; and the path and offset added to the messages of
ordinary reads, which would need the seam switched on for every call. All three are additive later.

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
types, and the old fixtures and the oracle catch any unintended change on the way. A stage's
measurement follows `Development-Workflow.md` §6.5, "Замер рабочей ветки", against
`Baselines/pre-rework/`: by the raw files until R4, through `BASE-02` from R4 on. A stage measures
the suites its section of `Benchmark-Plan-Changes.md` names, and the soak run is always kept (it is
what shows a pooled buffer that never goes back to the pool). R1, R2, R3, R4 and R6 change the path
of every public call, so they also measure the profile matrix (`ProfileMatrixBenchmarks`): it is the
only measurement of a whole call under every profile, and a regression in the code a stage adds
around the primitives shows there, in the stage that caused it. R5 and R8 do not. Nothing else runs
on the machine while a stage is measured — a cell whose error grew several times over is
interference, not a result. "Wire unchanged"
means the thirteen fixtures and the oracle pass byte for byte. Each stage applies its part of the
four change files.

### R0 — Baseline and oracle — *wire unchanged*

- Tag the entry commit locally as `pre-rework` (not `v*`, so CD never sees it even if pushed), run the
  complete Benchmark Track A so the harness records a Baseline, delete the tag, and commit the raw
  results under `benchmarks/ViShap.Viper.Serialization.Benchmarks/Baselines/pre-rework/`.
- Record the byte oracle: one text file holding the SHA-256 of the writer's output for every case of
  the existing corpora — `tests/.../RoundTrip/Corpus*.cs` (primitives, time and system types, arrays,
  composites, collections) under every profile of `CorpusProfiles`, the V0 corpus of
  `Format/V0CorpusTests`, the reference graphs of `References/` and the keyed shapes of `Contracts/` —
  under V0 and V1, with references on and off wherever the format admits it. The oracle invents no
  case of its own. A test compares against it and, on a mismatch, prints the hex of both outputs for
  that case. The oracle is deleted at R6. [D9.19]
- **Gate:** baseline committed; oracle committed; suite green.

### R1 — Wire primitives on buffers — *wire unchanged*

- `WireReader` / `WireWriter` as `ref struct`s over `ReadOnlySequence<byte>` / `ReadOnlySpan<byte>`
  and `PayloadBuffer`. `ValueReader` / `ValueWriter` become thin adapters, then are deleted once no
  caller remains.
- The structural tests for INV-2, INV-3 and INV-4 are rewritten for the new types.
- V0 writes through `PayloadBuffer` here, not in R2, so a keyed V0 write to a non-seekable
  destination succeeds and V0-25 is inverted in this stage (owner's decision of 2026-09-27,
  `Owner-Review.md` log 53).
- **Gate:** fixtures and oracle byte-identical; no virtual call per byte on the write path; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11).

### R2 — Pipeline on pooled buffers — *wire unchanged*

- V0 and V1 write into `PayloadBuffer`; keyed lengths patched in place; atomic writes (INV-15).
  *(Done in R1 for the payload; R2 carries the phases, the destinations and STR-30.)*
- `MeteredReadStream`, `MeteredWriteStream`, `WindowReadStream` and every `MemoryStream` on the
  payload path are deleted (§5.2).
- Cycle detection by ancestor stack; pooled reference tables (§11).
- **Gate:** a V0 keyed write to a non-seekable destination succeeds; no `MemoryStream` under
  `Pipeline/`; an error in the middle of a graph leaves the destination empty; fixtures and oracle
  byte-identical; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11).

### R3 — Public surface and non-seekable reading — *wire unchanged*

- The whole surface of §9: buffer and sequence entry points, `PooledPayload`, bytes-consumed forms,
  `Populate`, asynchronous methods including `DeserializeAsyncEnumerable`, `WithKeys`. `StreamExtensions` and `FromHeader`/`FromStream` are
  deleted; `BinaryHeaderPeek` is deleted and the router decodes the magic from the buffered source.
- The V0 boundary rules (§7) and the V0 asynchronous rule with its required text (§9.5).
- **Gate:** every entry point reads a non-seekable source; a non-seekable double that fails on any
  read past the frame proves exactly one V1 frame is consumed; `Api/CrossEntryPointTests` covers every
  entry point; `Api/PublicSurfaceTests` matches contract §3; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11).

### R4 — Typed engine — *wire unchanged*

- §10.1 and §10.2: shapes and codecs, `FormatterCache<T>`, `TypeContract<T>` / `ReflectedContract<T>`,
  `MemberWriter` / `MemberReader` with the engine's checks, array and capacity rules, enum conversion
  without boxing, pooled asynchronous builders.
- Every one of today's 78 registered formatters is rewritten into its shape — scalars into
  `IScalarFormatter<T>`, collections into one `ISequenceShape` per generic definition, dictionaries
  into `IMapShape`, tuples, pairs, `Lazy<T>` and multi-dimensional arrays into typed composites, enums
  into `EnumFormatter<TEnum>`. The encoding of every type is unchanged (fixtures and oracle).
- **The folder `src/ViShap.Viper.Serialization/Cache/` is deleted**, together with the dictionary
  cache in `FormatterRegistry`: `ActivatorCache`, `MethodInvokerCache`, `DictionaryAccessorCache`,
  `TupleAccessorCache`, `LazyAccessorCache`, `FrozenFactoryCache`, `ImmutableFactoryCache`,
  `ReadOnlySequenceAccessorCache`, `CollectionCountCache`. Each exists because the engine works with
  `object` and `Type`; a typed shape calls `new`, `Add`, `Count`, `Key`, `Item1`, `Value`,
  `ToFrozenSet<T>` or `ImmutableArray.Create<T>` directly. What remains cached: `FormatterCache<T>`
  (a static field), the contract per type (the polymorphic slot looks up by runtime type), the union
  maps, and one shape factory per generic definition (one `MakeGenericType` per closed type).
  [D9.19]
- **Gate:** fixtures and oracle byte-identical; INV-5 and INV-17 structural tests pass; no type under
  `Cache/` remains and `Concurrency/CacheTests` covers exactly the caches that remain; the §11
  targets that do not depend on R6 are measured; `BASE-02` — the tool that reports a run against a
  baseline cell by cell — exists [D9.25], and the cold start (`ContractColdRunner`) is compared with
  `pre-rework` through it, any regression written up in `internal/performance/`; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11).

### R5 — Algorithm contracts — *wire unchanged*

- §8: the interfaces, the existing built-ins ported and renamed (§8.2), the new built-ins,
  `HkdfKeyProvider`, the services' checks.
- **Gate:** every built-in round-trips through the pipeline and resolves from the catalog;
  `KeySizeInBytes` is enforced at `Build()`; no interface has a default member; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11).

### R6 — The final format — *the break*

- §6 entire: the header with services and its 4 KiB bound, the associated data, varint structural
  numbers, the null fold, the reference frame, `ImmutableArray<T>`.
- Re-freeze the fixtures once, from the corpus; record the exception in `CLAUDE.md` and restore the
  rule in the same change. Delete the oracle.
- Last step: diagnostics (§9.7) — the engine's trace seam, `BinaryFormatDumper` over spans and
  sequences, `Dump`, `Dump<T>`, `DumpValue<T>`, `Compare<T>`, and the `BinaryDump` model with its
  text, JSON and hex renderings.
- **Gate:** every rule of contract §22 is pinned by a byte-level test; the new fixtures are committed;
  sizes (Track A §14) re-measured against R0 and the difference published in `internal/performance/`; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11); every node kind of `BinaryDumpNodeKind` and every failure class of contract §8 is pinned by a dumper test over the new fixtures; the read cells of the profile matrix before and after the trace seam are within error, or the difference is the owner's decision.

### R7 — Removed

The V0 phases it carried are cancelled (§7); what remained is in R2 (keyed writes to any destination)
and R3 (the read boundary).

### R8 — Generator ground

- The conformance suite `CONF-*`: for every object shape of the corpus — positional, keyed, inherited,
  shadowed, overridden, union, struct — the member order, keys, layout and bytes, written so that a
  generated contract can be run against the same cases.
- `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` on the reflection path's public entry points.
- **Gate:** `CONF-*` passes on `ReflectedContract<T>`; a consumer project built with AOT analysis
  reports the annotated entry points and nothing unannotated; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11).

### R9 — Re-gate and release

R9 is five sub-stages [D9.31]. Each is worked on its own branch cut from `release/v1.0.0` **after the
previous one has been merged into it**, so every branch starts from everything before it and nothing
is ever cut from another working branch. The branch kinds are those of `Development-Workflow.md` §2.1
and each branch follows its cycle (§2.5); the owner's commands, step by step, are in "R9 and the
release — the owner's sequence" below.

#### R9a — Reconciliation — `rework/r9a-reconcile` — `viper_refactorer`

- `System-Contract.md`, `QA-Plan.md` and `Benchmark-Plan.md` reconciled completely from the change
  files, and then **independently of them** [D9.28]:
  - every section of the contract read against `src/`;
  - every contract rule has at least one QA checkpoint, and every QA checkpoint names a rule that
    exists and a test that passes;
  - every benchmark checkpoint names an entry point and a suite that exist;
  - every invariant INV-1…INV-18 is stated in the contract, so none is left only in this plan;
  - the `Retired.md` sweep: every entry searched in `src/`, `tests/`, `benchmarks/`, the XML
    documentation, `CLAUDE.md`, the skills and every living document, with the result recorded per
    row.
- Every file under `internal/` classed and the class written at its head and in `CLAUDE.md`:
  normative (`System-Contract.md`), plan (`QA-Plan.md`, `Benchmark-Plan.md`), operational
  (`Development-Workflow.md`, `CLAUDE.md`, the skills, the package READMEs, `docs/`), or historical
  (`rework/`, `audit/`, `Architecture-Audit.md`, `Audit-Closure.md`, `Audit-Future.md`,
  `Audit-Refactor.md`), whose head says that its rules no longer apply. `internal/README.md` states
  the classes.
- The skills under `.claude/skills/` reconciled with the system, with the owner's approval [D9.24].
- The skill `viper_conformance_auditor` written from `Conformance-Audit-Brief.md`, for the owner to
  approve in the pull request [D9.29]; `Development-Workflow.md` §2.1 names it for `audit/` branches.
- `ViShap.Viper.Core.csproj` packs its own `CORE-README.md` instead of the repository's `README.md`,
  which stays the face of the repository [D9.30].
- **Gate:** contract §24 and QA plan §32 fully checked, except their `dotnet pack` and README boxes,
  which R9b closes; the independent reconciliation finds no
  divergence; every `Retired.md` row has a clean sweep; every file under `internal/` carries its class;
  `CLAUDE.md` and the skills describe the system; the auditor skill exists; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11).

#### R9b — Consumer documentation — `docs/v1-consumer-docs` — `viper_refactorer`

Written from the reconciled contract and the system as R9a left them, never from a plan or a change
file [D9.31].

- `docs/`, one page per subject: getting started; the entry points by family — buffer, stream,
  asynchronous, `Populate`, frame streams; options and limits; V1 and V0, with the explanation and
  example plan §9.5 requires; positional and keyed layouts and schema evolution; references;
  polymorphism; algorithms and keys; exceptions; AOT.
- The three package READMEs, drawn from `docs/`: `SERIALIZATION-README.md`, `CORE-README.md` (for an
  author of a custom algorithm or key provider), `METAPACK-README.md`. The repository's `README.md`
  links to them and to `docs/`.
- No adjective about performance anywhere (`Benchmark-Plan.md` §28).
- **Gate:** every page and README agrees with the contract; no `Retired.md` entry appears in them;
  `dotnet pack` succeeds for all three packages, each with its own non-empty README, which closes
  the `dotnet pack` and README boxes of contract §24 and QA plan §32; every example in `docs/` compiles
  and runs against the release branch.

#### R9c — Conformance audit — `audit/v1-conformance` — `viper_conformance_auditor`, a separate session

- The audit of `Conformance-Audit-Brief.md` over everything, `docs/` and the READMEs included. The
  report is the only file the branch adds.
- **Gate:** the report exists with a verdict; every finding carries its evidence, its side, its layer
  and its weight.

#### R9d — Audit fixes — `bugfix/v1-audit-<topic>` — `viper_refactorer`

- Skipped when the audit found nothing. Otherwise one branch per coherent group of findings, each cut
  from `release/v1.0.0` after the previous one is merged. A fix changes, in the same change, every
  document it makes untrue — contract, plans, `CLAUDE.md`, `docs/`, READMEs — and adds a
  `Retired.md` row for anything it removes. A finding the owner decides not to fix is recorded in the
  report with the owner's decision.
- **Gate:** every finding of weight "blocks the rc" is fixed or decided; the suite is green in Debug
  and Release; the benchmark harness builds, `--verify` passes every pair and `--smoke` passes (§0.11).

#### R9e — Closure check — `audit/v1-conformance-closure` — `viper_conformance_auditor`, a separate session

- The auditor verifies every finding closed against the merged `release/v1.0.0` and records it in the
  report. A fix that opened a new divergence is a new finding, and R9d runs again for it.
- A CD dry run: `dotnet pack` of all three packages with `-p:Version=1.0.0-rc.1`, exactly as `cd.yml`
  runs it, without pushing.
- **Gate:** the report's verdict is "the rc may be tagged". Only then does the owner tag
  `v1.0.0-rc.1`.

#### R9 and the release — the owner's sequence

`viper_refactorer` gives the owner the next step of this table with its exact commands at the start of
every session and at the end of every hand-over, including the steps that are not its own. "Cycle"
is the working-branch cycle of `Development-Workflow.md` §2.5 with `<release>` = `release/v1.0.0`.

```text
 1  R9a   cycle with rework/r9a-reconcile               viper_refactorer; the owner approves the auditor skill in the PR
 2  R9b   cycle with docs/v1-consumer-docs              viper_refactorer: docs/ and the READMEs from the reconciled contract
 3  R9c   cycle with audit/v1-conformance               a NEW session with viper_conformance_auditor; the report only
 4  R9d   if the report has findings:
          cycle with bugfix/v1-audit-<topic>            viper_refactorer; one branch per group of findings, each cut
                                                        after the previous one is merged
 5  R9e   cycle with audit/v1-conformance-closure       a NEW session, the same auditor: verifies closure.
                                                        A new finding → step 4 again, then step 5
 6  rc    only on the verdict "the rc may be tagged":
            git switch release/v1.0.0 && git pull
            git tag -a v1.0.0-rc.1 -m "v1.0.0-rc.1"
            git push origin v1.0.0-rc.1                          CD publishes the pre-release
 7  rc baseline (Development-Workflow §6.3):
            git worktree add ../viper-bench v1.0.0-rc.1
            cd ../viper-bench
            dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks --configuration Release -- --track A
            cd -
            git switch release/v1.0.0 && git pull
            git switch -c benchmark/v1.0.0-rc.1-baseline
            cp -r ../viper-bench/benchmarks/ViShap.Viper.Serialization.Benchmarks/Baselines/v1.0.0-rc.1 \
                  benchmarks/ViShap.Viper.Serialization.Benchmarks/Baselines/
            git worktree remove ../viper-bench
            git add -A && git commit -m "Record the v1.0.0-rc.1 baseline"
            git push -u origin benchmark/v1.0.0-rc.1-baseline
            PR → release/v1.0.0, merge commit
 8  a defect on the rc: cycle with bugfix/<topic>, then tag v1.0.0-rc.2 as in step 6
 9  release, once the rc holds:
            PR release/v1.0.0 → main, merge commit (no squash, no rebase)
            git switch main && git pull
            git tag -a v1.0.0 -m "v1.0.0"
            git push origin v1.0.0                               CD publishes v1.0.0
10  release baseline (mandatory): as step 7 on the v1.0.0 tag, branch benchmark/v1.0.0-baseline from
    main, PR → main
11  after the release: release/v1.0.0 is deleted; Track B is measured on the v1.0.0 tag
```

Never cut `docs/`, `audit/` or `bugfix/` from `rework/r9a-…` or from one another; never tag the rc
before the R9e verdict; never move or delete a pushed tag.

#### After the rc tag

- Track A on the `v1.0.0-rc.N` tag, which the harness records as that tag's Baseline — a pre-release
  baseline the owner asked for in this plan [D9.26] — taken from a worktree and committed through
  `benchmark/v1.0.0-rc.N-baseline`, as `Development-Workflow.md` §6.3 describes.
- A defect found on the rc is fixed on `bugfix/<topic>` from `release/v1.0.0`, with every document it
  touches, and the next tag is `rc.N+1`.

Track B — the comparison with other serializers — is measured after the release, on the `v1.0.0` tag.
Its adapters are written earlier, once R6 has made the format final: on `benchmark/track-b-adapters`
from `release/v1.0.0`, returned to it through a pull request, and never measured beyond a measurement
before the tag exists [D9.25].

**Tags along the way** [D9.22]: none published before R6 (the local `pre-rework` tag of R0 is never
pushed); `v1.0.0-beta.N` not set: after R6 the owner deliberately skipped it, since `dotnet pack` fails until the package READMEs of R9b exist (`Owner-Review.md` log 64); `v1.0.0-rc.N` once R9e is
closed — fixes only; `v1.0.0` on `main` after `release/v1.0.0` is merged.

---

# 13. After the release

Each of these is additive; none blocks a stage [D9.2, D9.20]:

- **Schema fingerprint** — a new critical service with a new number (§6.1.2).
- **Zstandard, LZ4, AES-GCM-SIV** — separate packages implementing the §8.1 interfaces.
- **Source generator** — §13.1.
- **Benchmark Track B** — on the `v1.0.0` tag.

**Live tracing of ordinary calls** (owner's note of 2026-09-29, not in v1.0). The diagnostics of §9.7
read a finished frame. Logging each ordinary `Serialize`/`Deserialize` as it runs is additive later:
the recommended shape is an options switch such as `WithTrace(sink)` whose sink receives the
`BinaryDump` of each operation — it publishes only the dump model that already exists, and needs the
seam in the write codecs as well. Publishing the observer itself was rejected for v1.0: it would
freeze the engine's traversal protocol, which stays internal like the shapes and codecs. Until then the
documented pattern is a `catch (BinarySerializerException)` that logs `Dump<T>` of the same bytes, and
`DumpValue<T>` for a write.

## 13.1 `ViShap.Viper.Generator` — how it joins the system

The generator is not built in v1.0; v1.0 builds the seam it plugs into (§10.2) and proves the seam
with the conformance suite (R8). This section fixes how it arrives, so that nothing in v1.0 has to be
reworked when it does.

**What it is.** A Roslyn incremental source generator, `netstandard2.0` as analyzers require, shipped
as a fourth package `ViShap.Viper.Generator` with the analyzer in `analyzers/dotnet/cs`. It is a
build-time dependency of the consumer, never a runtime one: `ViShap.Viper.Serialization` does not
reference it, and the meta-package `ViShap.Viper` references it only as a development dependency
(`PrivateAssets="all"`) if the owner chooses to bundle it then.

**What it produces — and nothing else** (§10.2, INV-5):

```text
per annotated type    one TypeContract<T> subclass: Create, Write, Read or ReadField — member order,
                      member access, construction, response to a known key
                      the member description (layout, members, keys) the engine checks calls against
                      the registration of that contract
never                 a length, a count, a loop over wire data, a limit, a null or reference frame,
                      a union tag, a byte — all of these stay in the engine
```

**How the engine finds it.** The engine resolves a type's contract in one place — the contract cache
of §10.1. With the generator, that resolution asks a registry first and falls back to
`ReflectedContract<T>`:

```text
FormatterCache<T> → object codec → contract for T:
    generated contract registered for T?  → use it
    otherwise                             → ReflectedContract<T> (v1.0 behaviour, unchanged)
```

Nothing else in the engine, the pipeline, the formatters or the wire changes. A type without a
generated contract behaves exactly as in v1.0; a type with one produces the same bytes (the
conformance suite `CONF-*` and the frozen fixtures are the arbiter, INV-12).

**What becomes public then** — decided when the generator ships, because generated code lives in the
consumer's assembly and can call only public API:

```text
TypeContract<T>, MemberWriter, MemberReader     public, or a narrower public façade over them
the registry                                    ContractRegistry.Register(...) called from a
                                                [ModuleInitializer]; or a static abstract member on the
                                                type; or a context passed in the options, like
                                                JsonSerializerContext
non-public [BinaryInclude] members              the type must be partial, or [UnsafeAccessor]
the release label                               v1.x minor — no byte and no existing signature changes
```

**Compile-time diagnostics.** Every rejection `ReflectedContract<T>` makes today at first use — an
unmarked member of a contract, `[BinaryKey]` without `[BinaryContract]`, duplicate keys or orders,
`[BinaryKey]` with `[BinaryIgnore]`, a delegate member, a union tag above 255, an abstract type with
no union — becomes a compiler error with its own diagnostic id. The runtime path keeps rejecting the
same things for types without a generated contract.

**What changes in the repository then:** a `src/ViShap.Viper.Generator` project; a test project for
the generator (snapshot tests of the emitted code and of every diagnostic); `CONF-*` run a second time
against generated contracts; CD packs and validates four packages instead of three; the contract
gains a generator section; the reflection path's `[RequiresDynamicCode]` annotations stay, and the
generated path carries none.

**What v1.0 must therefore already guarantee** — each is a v1.0 gate item, not a later task:

- the contract lookup has exactly one place where a registry can be consulted (R4);
- `TypeContract<T>` can be implemented outside the engine without access to anything but
  `MemberWriter` / `MemberReader` (R4, INV-2);
- the conformance suite runs a contract through the same cases regardless of how it was built (R8).

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
