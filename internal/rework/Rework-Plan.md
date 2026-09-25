# ViShap.Viper — Second Rework Plan

**Status:** Proposed. Nothing in this document has been applied to `src/`.
**Written:** 2026-09-25, against `fb1ad38` plus the NX-12 change.
**Owner decision required before R1 starts:** §13.
**Target:** the first public release, `v1.0.0`. The rework happens *before* it, while no payload,
no consumer and no compatibility promise exists.

This folder is the complete brief for the rework. Read it in this order:

```text
Rework-Plan.md               this file — why, what, in which order, and what must not be lost
Contract-Changes.md          every change System-Contract.md must receive, by section and by stage
QA-Plan-Changes.md           every change QA-Plan.md must receive, by section and by stage
Benchmark-Plan-Changes.md    every change Benchmark-Plan.md must receive, by section and by stage
```

The three change files are deliberately separate from the plans they amend. `System-Contract.md`,
`QA-Plan.md` and `Benchmark-Plan.md` stay the description of what the code does *now*; each is rewritten
from its change file at the stage that makes the change real, and never ahead of it. A contract that
describes code which does not exist yet is the drift this project spent two audits removing.

---

# 0. Rules for whoever executes this

1. **Stage by stage.** A stage starts only when the previous one's gate holds (§12). Never two at once.
2. **The suite is green at the end of every stage.** A stage that cannot end green is split, not merged.
3. **The wire does not change before R6.** Stages R1–R5 are internal. The eleven frozen fixtures in
   `tests/.../Fixtures/Wire/*.bin` are the oracle for all of them: if one fails before R6, the stage
   broke behaviour, and the fix is in `src/`, never in the fixture.
4. **The fixtures are re-frozen exactly once, at R6.** This is the single, recorded exception to the
   rule in `CLAUDE.md` that they are never regenerated. After R6 the rule applies again, forever.
5. **Carry the invariants of §3 over by construction, not by memory.** Every one of them has a
   structural test today; the test is rewritten alongside the code, never deleted.
6. **Contract, QA plan and benchmark plan change in the same stage as the code**, from the change
   files in this folder.
7. **Measure before claiming.** Every performance statement in this plan is a *target*. It becomes a
   claim only through `Benchmark-Plan.md` §28.

---

# 1. State at entry

What is and is not synchronised on the day this plan was written. Everything marked open is carried
into a stage below; nothing is left unowned.

| Area | State | Where it is handled |
|---|---|---|
| `src/` ↔ `System-Contract.md` | In sync. NX-01…NX-12 changed the contract in the same change as the code; NX-12 added `CompositeReader`/`CompositeWriter` to §18 | — |
| `src/` ↔ tests | In sync, 1 659 tests green in Debug and Release. One gap found while preparing this plan — the §5 defaults test did not know `MaxDecompressionRatio` — is closed, and the test now fails if a limit is added without a documented default | — |
| `QA-Plan.md` body ↔ NX rules | **Behind.** §30.3 records every NX fix and its pinning test, but the body sections (§11, §15, §18, §20, §22, §23) carry no checkpoint IDs for the new rules | `QA-Plan-Changes.md` §1 |
| `Benchmark-Plan.md` ↔ NX rules | **Behind.** Nothing measures the incremental decompression path, the ratio check or the collection-count fast path; FAIR-07 still describes a missing `IBufferWriter` entry point as permanent | `Benchmark-Plan-Changes.md` §1 |
| Package READMEs | **Release blocker.** `SERIALIZATION-README.md`, `CORE-README.md` and `METAPACK-README.md` are 0 bytes. `dotnet pack` fails with NU5040 for the serialization and meta packages, so CD would fail on the `v1.0.0` tag | R9 |
| `Architecture-Audit.md` | Its status section describes a passed point (85/85 tests). Superseded as a status report by this folder; kept as the record of the first rework's reasoning | R9 |
| Benchmark Track A | Harness built, no baseline captured | R0 (see §12 — this is the one measurement worth taking *before* the rework) |
| Benchmark Track B | Not started | After R9 |

---

# 2. Why a second rework

The first rework moved ownership to the right places: one operation object, one byte monopoly, one
traversal owner, algorithms without policy. It was correct, and nothing in it is reversed here. What
it did not change is the *material* those owners are built from, and two of those materials are now
the cause of nearly every recent fix.

## 2.1 Root cause one — the byte monopoly is built on `Stream`

`ValueReader`/`ValueWriter` hold a `Stream`. Everything below follows from that single choice:

```text
reading needs a seekable stream        FormatRouter peeks 8 bytes and rewinds (BinaryHeaderPeek)
V0 keyed writes need a seekable stream a field length is patched by moving Stream.Position
MemoryStream in every write            V1 buffers the payload into one, then ToArray()
two to four full payload copies        payload ToArray, compressed ToArray, encrypted ToArray, wire ToArray
a virtual call per byte                WriteRawByte / ReadOneByte go through two Stream layers
metering as Stream decorators          MeteredReadStream, MeteredWriteStream, WindowReadStream
no NetworkStream, no Pipe, no buffer   the public API is Stream and byte[] and nothing else
```

## 2.2 Root cause two — the engine is typed as `object`

`GraphReader.ReadValue` returns `object?`, `IScalarFormatter.Write` takes `object`, a member is read
through `Func<object, object?>`:

```text
every primitive is boxed on both directions
every member access is a delegate call on a boxed value
per-element metadata resolution in Add (GetGenericArguments, cache lookup per element)
Expression.Compile everywhere          ~100 IL2xxx/IL3050 diagnostics under AOT analysis
no shape a source generator can target without a second engine
```

## 2.3 The symptom that proves it: every fix now grows an interface

This happened inside one week, on a codebase that had just been reworked:

```text
ICompressionAlgorithm   span overload → + IBufferWriter overload + SupportsIncrementalDecompression
IEncryptionAlgorithm    two overloads without AAD + two with + AuthenticatesAssociatedData
ISequenceFormatter      CountOf needed a new process-wide cache to see generic collections
ICompositeFormatter     needed a new surface type to stop exposing ReadInt32
BinaryHeaderPeek        exists only because a Stream cannot be peeked
```

Each change was right on its own. Together they are the pattern the first audit called
"security by convention": the design no longer carries the requirement, so every requirement arrives
as an addition. Fixing it additively after `v1.0.0` would mean doing each of these again under a
compatibility constraint. Fixing it now means doing each once.

## 2.4 Why now is the only moment

After `v1.0.0` every item below costs a format version, a compatibility story, or a deprecated overload
that must be kept forever. Before it, the same items cost only work. There is no legacy: no tag, no
published package, no payload anyone else wrote.

---

# 3. What carries over — the invariants

The rework replaces the material, not the architecture. Each invariant below survives with a
structural test; the test is rewritten for the new types and never removed.

| # | Invariant | Held today by | Held after the rework by |
|---|---|---|---|
| INV-1 | One operation object per public call; no layer below builds limits, a budget or keys | `SerializationOperation` | `OperationState`, a struct passed by `ref` (no allocation) |
| INV-2 | Byte monopoly: exactly one read-side and one write-side primitive surface | `ValueReader`/`ValueWriter` | `WireReader`/`WireWriter` (`ref struct`) |
| INV-3 | A declared length is compared with the bytes that can still arrive before it drives an allocation | `RequireAvailable`, `IRemainingBytes` | the reader knows its remaining bytes exactly — a sequence always does |
| INV-4 | A loop bound over wire data exists only as a validated `ElementCount` | `ElementCount.Validate` + composite surface | unchanged, extended to typed shapes |
| INV-5 | One traversal owner: depth, nodes, identity, cycles, keyed layout, every element loop | `GraphReader`/`GraphWriter` | the typed engine; formatters still describe shapes and never loop over wire data |
| INV-6 | Algorithms are pure mechanics, called inside the phase barrier, never see a limit | `*Service` + `PhaseBudget` | unchanged |
| INV-7 | No process-wide mutable registry can change what an algorithm is | `AlgorithmCatalog` snapshot | unchanged |
| INV-8 | Only tags travel, never type names | `UnionMap` | unchanged |
| INV-9 | Every wire field has exactly one encoding (canonical wire) | strict bool, strict UTF-8, duplicates rejected, ids declared once | unchanged, and extended to varints (minimal encoding only) |
| INV-10 | The exception taxonomy is complete: nothing from a payload path leaves it under a framework name | engine-owned classification | unchanged |
| INV-11 | Key material is always an owned copy; the serializer clears only what it owns | `SecretKey`, `IKeyProvider` | unchanged |
| INV-12 | The member plan is a total order over the whole inheritance chain | `TypeContract` (NX-03, NX-04) | unchanged — and it becomes the generator's specification (§10) |
| INV-13 | Limits are policy, validated once, and a payload can never raise them | `SerializationLimits` | unchanged |

If a stage finds that meeting its goal requires weakening one of these, the stage stops and the
question goes to the owner. That is the one kind of discovery this plan does not pre-authorise.

---

# 4. Decisions on the proposals

The owner's brief, item by item, with what this plan does with it.

**"Break everything now — formats, wire, fixtures, tests, benchmarks."** *Accepted, with one
refinement:* break the **material**, keep the **architecture**. §3 is the list of what is not
re-derived. The first rework's audit found real defects in the architecture it replaced; this one
would find them again if the next implementer rebuilt the ownership model from scratch.

**"Replace the current readers and writers as if they never existed."** *Accepted for the
implementation, refined for the role.* `ValueReader`/`ValueWriter` are replaced by `WireReader`/
`WireWriter` over buffers. The role — the only access to payload bytes — is INV-2 and does not move.

**"Zero-alloc in v1.0.0."** *Accepted as named targets per path (§11), rejected as a slogan.* The
reflection path can reach zero allocation on the primitive and fixed-layout paths only if the engine
becomes typed (R4). What remains allocating is named: the result graph, strings, collection backing
stores, and the reference table when references are on.

**"V0 with built-in maximum compression by the best algorithm, as an eternal ultra-compact codec."**
*Accepted in a stronger form.* The contract forbids compression, checksum and encryption under V0
"because a V0 reader has nowhere to learn that they were". But V0's own premise (§10.2) is that both
ends are configured together — the same premise under which V0 already carries keyed contracts and
limits. Under that premise all three phases apply from configuration with zero header bytes. What V0
can never have is **self-description**, and that becomes its *only* difference from V1 (§7).
Refinements: compression is *configured*, not always on — always-on compression enlarges small
payloads and slows the fastest path, which contradicts "ultra-compact and fast"; and there is no
single best algorithm (Brotli wins on size and loses heavily on compression speed at quality 11;
Zstandard is the market choice and is not in the `net10.0` BCL — verified).

**"Fix V0's need for a seekable stream with references."** *Accepted; the diagnosis is corrected.*
References never needed seeking. The seekable requirement comes from **keyed contracts** (a field
length is patched by moving `Stream.Position`) and from **reading** (the router rewinds its peek).
Both disappear once the payload is built in the serializer's own buffer (R2) and read from a buffered
source (R3). V0 references are a separate change: allowed from configuration, like the phases (R7).

**"Develop V1 so it works with everything and is not deprecated by the next major change."**
*Accepted through one mechanism:* a header **extension area** — tag, length, bytes, with a critical
bit (W6). An unknown critical extension is rejected; an unknown non-critical one is skipped; all of
them are authenticated. New capabilities land as extensions in v1.x without a new format version.
This is how long-lived formats stay current, and it is the answer to "not deprecated".

**"New algorithms, finished interfaces instead of ten overloads."** *Accepted.* One method per
direction per family, associated data always a parameter, capabilities stated as data (§8). Added
built-ins are limited to what the BCL and the existing `System.IO.Hashing` dependency ship, so no
native dependency enters the core packages. Zstandard and LZ4 are offered as optional packages
(decision D-4).

**"Remove MemoryStream and extra allocations; support every stream including NetworkStream."**
*Accepted.* R2 and R3. One caveat that must be designed, not discovered: a buffered reader over a
**non-seekable** stream may read past the payload. V1 carries its frame length and reads exactly one
frame, so it is unaffected. A frameless V0 payload on a non-seekable stream therefore needs a
delimited source (an explicit length, a sequence, or a span) whenever the decoder cannot stop exactly
at the root's end (§7).

**"An API to serialize straight into a pool or buffers."** *Accepted.* `IBufferWriter<byte>` as the
primary write target, `ReadOnlySequence<byte>`/`ReadOnlySpan<byte>` as the primary read source, a
disposable pooled result for callers who want bytes without owning a writer, and `PipeReader`/
`PipeWriter` at the edge (§9). This is the shape MemoryPack and MessagePack-CSharp converged on.

**"Keep flexibility, BinaryContract and positional semantics, reflection, and prepare the ground for
generators (v2.0.0)."** *Accepted, with a consequence worth stating:* if the ground is prepared as
§10 describes, the generator changes **no byte** on the wire and **no public signature** — it only
replaces how `TypeContract` is built. That makes its introduction an additive minor release (v1.x),
not a major one. The owner may still call it v2.0.0 for visibility; nothing technical will require it.

**"Finish Benchmark Track B in v1.x."** *Accepted, and sharpened:* Track B is worthless before the
format is final, so it runs after R9. But one measurement is worth taking **before** the rework: a
Track A baseline at R0. Without it, no stage of this rework can prove it improved anything, and the
rework's own claims become the kind §28 forbids.

---

# 5. Target architecture

The layers of `System-Contract.md` §2 stay. Their contents change.

```text
Public API       BinarySerializer
                   write: IBufferWriter<byte> · byte[] · PooledPayload · Stream · PipeWriter
                   read:  ReadOnlySpan<byte> · ReadOnlySequence<byte> · byte[] · Stream · PipeReader
      │ one OperationState per call (struct, passed by ref)
Pipeline (V0|V1) framing · phase order · header + extensions + AAD · phase sizes
      │ phases are transforms: ReadOnlySpan<byte> → IBufferWriter<byte>, over pooled buffers
Engine           typed traversal: Write<T>/Read<T>; depth · nodes · identity · keyed layout · loops
      │ WireReader / WireWriter — ref structs, the only access to payload bytes
Formatters       IFormatter<T>: scalar · sequence shape · map shape · composite · object plan
      │
Algorithms       one contract per family, pure mechanics
```

## 5.1 Buffers and ownership

- **`PayloadBuffer`** (internal): a segmented writer built on `ArrayPool<byte>` that the serializer
  owns for the whole operation. It supports in-place patching of an earlier position, which is what
  keyed field lengths need. Patching happens only inside this buffer — never in a caller's
  `IBufferWriter<byte>`, whose earlier spans the interface does not promise to keep valid (the trap
  `Audit-Future.md` §4 warned about).
- **Write path:** engine → `PayloadBuffer` → phases (each writing into the next pooled buffer) →
  header written to a stack buffer → header and final segments copied once into the destination.
  One copy of the final bytes is inherent: V1's header carries lengths that exist only after the
  phases ran. It is bounded, pooled and cleared.
- **Read path:** the source is (or becomes) a `ReadOnlySequence<byte>`. The header is decoded
  in place; a phased payload is linearised once into a pooled buffer, because AES-GCM and the
  checksum need contiguous input; an unphased payload is decoded straight from the sequence.
- **Streams are adapters**, not the model: a `Stream` source is read into pooled segments up to one
  frame (V1) or up to the caller's declared length (V0); a `Stream` destination receives the final
  segments.

## 5.2 Metering

`MeteredReadStream`, `MeteredWriteStream` and `WindowReadStream` exist to count and bound bytes on a
`Stream`. Over buffers, the same guarantees are properties of the reader and writer themselves:

```text
MaxWireBytes            the adapter refuses to buffer more than this from a source, and the writer
                        refuses to emit more than this relative to the operation's start
remaining-bytes check   a sequence knows its length; WireReader.Remaining is exact, not a guess
keyed-field window      WireReader.Slice(length) — a reader over exactly the declared field
```

The three decorator types are deleted. Their tests (`Streams/`) are rewritten against the reader and
writer — the rules they pin (budget vs truncation classification, origin-relative write budget, window
over-read is malformed) all survive.

---

# 6. Wire format — the final V1 encoding

Each decision states the rule, the reason, and the alternative rejected. Byte-level detail belongs to
`System-Contract.md` §22 when R6 rewrites it.

**W1 — Structural metadata is varint; payload primitives stay fixed-width.** Counts, lengths, ids,
keys, tags beyond one byte, and every header number become LEB128 varints (minimal encoding only —
INV-9). An `int` member stays 4 bytes little-endian. *Why:* metadata is small and dominates small
payloads; fixed-width primitives keep decoding branch-free and keep the door open to copying a
fixed-layout struct block as one span later. *Rejected:* varint for payload integers (protobuf's
choice) — smaller for small values, slower for all, and it closes the block-copy door.

**W2 — Counts are unsigned varints.** A negative count stops being expressible, which removes one
class of malformed input rather than checking for it.

**W3 — The reference frame is one varint: `(id << 1) | isBackReference`.** One byte for the first 64
ids, against five today. *Rejected:* implicit ids (the reader assigns `next++` and the writer omits
first-occurrence ids). A reader that skips an unknown keyed field never traverses it and so never
assigns the ids the writer assigned inside it; the two counters diverge. Explicit ids are what make
`PreserveReferences` compatible with schema evolution (§16.2), and that property is kept.

**W4 — A keyed field is `varint key`, then a fixed `int32` length, then the payload.** The length is
the only structural number that is written before its value is known. Fixed width makes the patch
O(1) and keeps the encoding canonical. *Rejected:* a varint length — it either needs non-minimal
padding (breaks INV-9) or a move of the field's bytes once the length is known, which is O(size) per
nesting level and so quadratic in the depth of nested contracts.

**W5 — The null flag stays one byte.** *Deferred, not rejected:* a null bitmap per positional object.
It saves bytes on wide objects with many nullable members, and it complicates canonicality (bits for
non-nullable members must be zero). Decide from Track A size data after R6 (decision D-3).

**W6 — The V1 header is compact and extensible.**

```text
magic               4 bytes   unchanged
version             varint    1 — there is no legacy to distinguish from
flags               varint    compressed · checksummed · encrypted · references · key id · extensions
algorithm ids       varint    one per phase the flags say is present; custom names as today (≤256 bytes)
key id              string    when flagged
extensions          varint count, then per extension: varint tag · varint length · bytes
lengths             varint    uncompressed · compressed · on-disk, only for phases present
checksum            bytes     length implied by the algorithm, when flagged
```

Extension tags carry a **critical bit** (LSB): an unknown critical extension is
`BinaryFormatNotSupportedException`, an unknown non-critical one is skipped by its length. Every
extension is part of the associated data. This is the mechanism that lets v1.x add a capability
without a new version number (§4). A flag that is clear means its fields are absent, not zero, so a
plain V1 frame shrinks from 29 header bytes to roughly 10.

**W7 — A schema fingerprint, as an opt-in extension.** A hash of the positional member plan (types,
order, keys), written when configured and checked when present. Positional layout's one real weakness
is that a mismatched type decodes garbage instead of failing; this turns it into a precise
`BinaryTypeException`. Critical when present. Decision D-2 (recommended: yes).

**W8 — Strings are unchanged:** varint UTF-8 byte length, strict decoding.

**W9 — Little-endian is unchanged**, and is now decoded explicitly everywhere (NX-07 already did it
for the routing peek).

**W10 — A V1 frame is self-delimiting.** The header's on-disk length says exactly where the frame
ends. That is what allows reading exactly one frame from a non-seekable stream and awaiting exactly
one frame from a `PipeReader` (§9).

**W11 — Root canonicity is unchanged:** a payload is consumed exactly; trailing bytes inside a frame
are malformed.

**W12 — Fixtures are re-frozen once, at R6,** from the §23 corpus, and `CLAUDE.md` records that the
exception has been used.

---

# 7. V0 — the frameless codec, at parity

V0 keeps exactly one property V1 does not have — **no header byte at all** — and loses exactly one
capability — **self-description**. Everything else becomes equal.

| Capability | V0 today | V0 after R7 | Mechanism |
|---|---|---|---|
| Full type set, unions, keyed contracts, limits | yes | yes | unchanged |
| Keyed contracts on a non-seekable destination | no (`NotSupportedException`) | yes | patching inside the serializer's own buffer (R2) |
| Compression | no | **yes, from configuration** | both ends configured together; compressed streams are self-terminating |
| Checksum | no | **yes, from configuration** | fixed-size trailer after the payload, size known from configuration |
| Authenticated encryption | no | **yes, from configuration** | `nonce ‖ ciphertext ‖ tag`, empty associated data; needs a delimited source |
| `PreserveReferences` | no | **yes, from configuration** | both ends configured together, as for the phases |
| Self-description | no | no | the defining difference |
| Byte-identical to the V1 payload when nothing is configured | yes | yes | kept: §22.8's property survives |

**The downgrade argument changes shape, and gets stronger.** Under V1 a reader chooses how to
unwrap from the header, so a policy (`RequireEncryption`) is needed to refuse a substituted plaintext
frame. Under V0 the reader always applies its configured phases: a plaintext frame handed to a reader
configured for AEAD fails its tag. `RequireEncryption`/`RequireChecksum` under V0 become build-time
statements that the configuration contains those phases, which `Build()` can check completely.

**Delimiting.** Raw, compressed and checksummed V0 payloads are self-terminating: the decoder knows
where the root ends. An encrypted one is not — the tag sits at the end of an unknown length — so
encrypted V0 requires a delimited source. From a non-seekable stream, any V0 mode whose decoder may
read ahead also requires one (§4). The contract states both rules, and a violation is a
`NotSupportedException` at the call, never a silent over-read.

**Default compression for V0: none.** Decision D-1. Brotli is the size winner in the BCL and is slow
to compress at high quality; Deflate is fast and modest. Neither is right for every frameless
channel, and an always-on default would enlarge the small messages V0 exists for.

---

# 8. Algorithms and their contracts

## 8.1 One shape per family

```csharp
public interface ICompressionAlgorithm
{
    CompressionAlgorithm Kind { get; }
    string? CustomName { get; }

    void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination);
    int Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int maxOutputBytes);
}

public interface IChecksumAlgorithm
{
    ChecksumAlgorithm Kind { get; }
    string? CustomName { get; }
    int HashSize { get; }

    void Compute(ReadOnlySpan<byte> source, Span<byte> destination);
}

public interface IEncryptionAlgorithm
{
    EncryptionAlgorithm Kind { get; }
    string? CustomName { get; }
    EncryptionGuarantee Guarantee { get; }   // Confidentiality · Authenticated · AuthenticatedWithAssociatedData
    int KeySize { get; }
    int Overhead { get; }

    void Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key,
                 ReadOnlySpan<byte> associatedData, IBufferWriter<byte> destination);
    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> associatedData, IBufferWriter<byte> destination);
}
```

What disappears, and why each is no longer needed:

```text
span→span overloads                   the destination is a writer, so no size must be guessed
GetMaxCompressedLength/…Ciphertext…   same reason
the AAD-less overloads                associated data is always passed; an algorithm that ignores it
                                      says so through Guarantee, not through a missing overload
AuthenticatesAssociatedData           replaced by Guarantee, which also distinguishes plain encryption
SupportsIncrementalDecompression      decompression into a writer is the only mode
```

Input is a contiguous span on purpose. The serializer owns the buffer and can guarantee contiguity at
the cost of linearising a multi-segment source once; AES-GCM has no incremental API in the BCL, so the
alternative would push that copy into every algorithm. `KeySize` lets `Build()` refuse a wrong-sized
static key at configuration time instead of at first use.

## 8.2 Built-in set

Verified against the `net10.0` reference assemblies and `System.IO.Hashing` 10.0:

```text
compression   None · Deflate · ZLib · Brotli                     all in the BCL
checksum      None · Crc32 · Crc64 · XxHash64 · XxHash3 · XxHash128   System.IO.Hashing (already a dependency)
encryption    None · Aes256Gcm · ChaCha20Poly1305               BCL; ChaCha20Poly1305.IsSupported is checked
key providers Static · Delegate · Hkdf (derives a key per key id from a root key)   BCL HKDF
not in BCL    Zstandard · LZ4 · AES-GCM-SIV                     optional packages only (D-4)
```

No checksum becomes a MAC. Integrity against a deliberate attacker stays the job of authenticated
encryption; `IChecksumAlgorithm` documents that it detects accidental corruption only.

---

# 9. Public API

The shape is buffer-first; `Stream` and `byte[]` are conveniences over it.

```csharp
// write
void          Serialize<T>(IBufferWriter<byte> destination, T value);
byte[]        Serialize<T>(T value);
PooledPayload SerializePooled<T>(T value);               // IDisposable; Memory/Span over pooled bytes
void          Serialize<T>(Stream destination, T value); // any stream — no seek
ValueTask     SerializeAsync<T>(PipeWriter destination, T value, CancellationToken ct = default);

// read
T?            Deserialize<T>(ReadOnlySpan<byte> source);
T?            Deserialize<T>(ReadOnlySequence<byte> source);
T?            Deserialize<T>(Stream source);             // any stream — reads exactly one V1 frame
ValueTask<T?> DeserializeAsync<T>(PipeReader source, CancellationToken ct = default);
ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken ct = default);
```

**Asynchrony exists only at the frame edge.** An async read awaits until one whole frame is buffered —
the V1 header says how long it is — then decodes synchronously. The engine never awaits. This keeps the
traversal, its budgets and its depth scopes exactly as they are, and it is how the mature serializers
handle pipes. A frameless V0 payload is read asynchronously only from a delimited source.

**Populate-in-place and the header-driven overloads** multiply today: `StreamExtensions` alone has
twelve. Both are decisions for the owner (D-6, D-8) rather than for this plan, with a recommendation:
one populate-in-place entry per source kind, and a single factory
`BinarySerializerOptions.FromPayload(...)` in place of the header-driven extension overloads.

---

# 10. The typed engine and the generator's ground

## 10.1 The typed engine (R4)

```text
IFormatter<T>              internal; Write(ref WireWriter, in T, ref OperationState)
                                     Read(ref WireReader, ref OperationState) → T
FormatterCache<T>          static generic cache — no dictionary lookup on the hot path
ObjectFormatter<T>         built from TypeContract; one typed MemberBinding<TOwner, TMember> per member,
                           getter Func<TOwner, TMember>, setter by ref for struct owners
Sequence/Map/Composite     typed shapes; the engine still owns every loop (INV-5):
                           Engine.WriteSequence<TCollection, TElement>, Engine.ReadMap<…>
the object path            explicit, and only where polymorphism exists: a declared type with a union
                           map, an interface, object. Boxing happens there and nowhere else.
```

The metadata resolution that `Audit-Future.md` §3.2 found inside `Add` (a `GetGenericArguments()`
array and a cache lookup per element) disappears by construction: a typed shape resolves its delegates
once, when it is built.

## 10.2 What the generator will produce, and nothing else

The generator's target is exactly what `TypeContract` builds at runtime today:

```text
the member plan    members, in the INV-12 total order, with their layout (positional or keyed)
member access      typed getters and setters, including non-public members under [BinaryInclude]
construction       the parameterless constructor, or its absence
the union map      tag ↔ type
```

Scalars, collections, composites, the pipeline, the algorithms, the limits — none of it is generated.
That confinement is what keeps the generator from becoming a second serializer, and it is why its
arrival changes no byte and no signature.

## 10.3 Ground to lay now (R8)

- **An internal object-plan contract** that `TypeContract` implements through reflection and that a
  generated plan will implement through emitted code, looked up before reflection is consulted.
- **A conformance suite**: for every object shape in the corpus — positional, keyed, inherited,
  shadowed, overridden, union, struct — the plan (member order, keys, layout) and the bytes. The
  reflection path passes it now; the generator must pass the same suite, unchanged, in its release.
- **Honest trimming and AOT annotations.** The reflection path is annotated
  `RequiresDynamicCode`/`RequiresUnreferencedCode` at its public entry points, and `IsAotCompatible`
  stays off. A consumer under AOT analysis gets a warning at build time instead of a failure at run
  time. The generator's release is what removes the annotations for generated types.

---

# 11. Allocation targets

"Zero-alloc" is stated per path. Each line becomes a Track A checkpoint
(`Benchmark-Plan-Changes.md` §2) and is a target until measured.

| Path | Target per operation, after warm-up |
|---|---|
| `Serialize<T>` into a caller's `IBufferWriter<byte>`, V1, no phases, `T` a record of primitives | 0 bytes |
| same, with compression and/or encryption | 0 bytes (pooled phase buffers, returned and cleared) |
| `Serialize<T>` to `byte[]` | exactly the returned array |
| `Deserialize<T>` from a span, `T` a record of primitives | exactly the returned object |
| `Deserialize<T>` of a graph | exactly the graph: objects, strings, collections and their backing stores |
| an array of `n` elements read | exactly the final array — no intermediate `List<T>` |
| `PreserveReferences` on | the above plus the reference table, pooled per operation |
| per-call operation state | 0 bytes (`OperationState` is a struct) |

What cannot reach zero and is not pretended to: the result graph itself, strings, the backing store
of every materialised collection, and the first use of a type (plan construction).

---

# 12. Stages

Each stage lists its goal, the work, the gate that must hold before the next stage starts, and the
documents it rewrites. "Wire unchanged" means the eleven frozen fixtures pass without modification.

### R0 — Baseline lock

- Tag the entry commit `pre-rework`.
- Run Benchmark Track A on it and commit the raw results under `Baselines/pre-rework/`. This is the
  "before" of every later claim.
- Record a behavioural oracle beside the frozen fixtures: for every §23 type family and every graph
  shape of the corpus, the exact bytes today's writer produces, in V0 and V1, with and without
  references. R1–R5 must reproduce every byte.
- **Gate:** suite green; baseline committed; oracle committed.

### R1 — Wire primitives on buffers — *wire unchanged*

- `WireReader`/`WireWriter` as `ref struct`s over `ReadOnlySequence<byte>`/`PayloadBuffer`.
- `ValueReader`/`ValueWriter` become thin adapters over them, then are deleted once no caller remains.
- INV-2, INV-3, INV-4 structural tests rewritten for the new types in the same stage.
- **Gate:** fixtures and oracle byte-identical; no per-byte virtual call on the write path.

### R2 — Pipeline on pooled buffers — *wire unchanged*

- V0 and V1 write into `PayloadBuffer`; keyed lengths patched in place; `MemoryStream` and `ToArray`
  removed from the payload path; phases write into pooled buffers.
- `MeteredReadStream`, `MeteredWriteStream`, `WindowReadStream` replaced by reader/writer properties
  (§5.2).
- **Gate:** a V0 keyed write to a non-seekable destination succeeds; no `MemoryStream` remains under
  `Pipeline/`; fixtures and oracle byte-identical.

### R3 — Non-seekable reading and the new entry points — *wire unchanged*

- The router decodes the magic from the buffered source; `BinaryHeaderPeek` is deleted.
- Public entry points of §9, including the async frame-edge reads.
- **Gate:** reading from a non-seekable stream double and from a `Pipe` succeeds; `BinaryFormatInspector`
  works on spans; every cross-entry-point equivalence test (`Api/CrossEntryPointTests`) covers the new
  entry points.

### R4 — Typed engine — *wire unchanged*

- §10.1. The object path is kept only for polymorphic slots.
- **Gate:** fixtures and oracle byte-identical; Track A shows no boxing on the primitive path and
  meets the §11 targets that do not depend on R6; cold start (`ContractColdRunner`) measured against
  R0 and any regression explained in `internal/performance/`.

### R5 — Algorithm contracts — *wire unchanged*

- §8.1 interfaces; existing built-ins ported; new built-ins of §8.2 implemented; `HkdfKeyProvider`.
- **Gate:** every built-in round-trips through the pipeline; the catalog resolves each; no algorithm
  has more than one method per direction.

### R6 — Final wire format — *the break*

- W1–W12. V1 header rebuilt with flags and extensions; varint metadata; compact reference frame;
  keyed varint key with int32 length; schema fingerprint extension if D-2 is accepted.
- Re-freeze the fixtures once, from the corpus. Record the exception in `CLAUDE.md`.
- `System-Contract.md` §10, §11, §14, §16, §22 rewritten from `Contract-Changes.md`.
- **Gate:** new fixtures committed; every §22 rule pinned by a byte-level test; Track A size (B3)
  re-measured against R0 and the difference published in `internal/performance/`.

### R7 — V0 at parity

- §7: phases and references from configuration; the delimiting rules; `Build()` rules for policies
  under V0.
- **Gate:** every V0 row of §7's table is pinned by a test; a V0 frame produced with no configuration
  is byte-identical to the V1 payload of the same value.

### R8 — Generator ground

- §10.3: the object-plan contract, the conformance suite, the trimming and AOT annotations.
- **Gate:** the conformance suite passes on the reflection path; AOT analysis of a consumer project
  reports the reflection entry points and nothing unannotated.

### R9 — Re-gate and release

- `System-Contract.md`, `QA-Plan.md`, `Benchmark-Plan.md` fully reconciled from the change files.
- Package READMEs written — the §1 blocker.
- Track A re-measured on the release candidate; Track B executed (`Benchmark-Plan-Changes.md` §3).
- Release note: every breaking change since the last published description of the format (there is
  none, so the note describes the format rather than a migration).
- `Architecture-Audit.md` marked as the historical record of the first rework.
- **Gate:** `System-Contract.md` §24 fully checked, `QA-Plan.md` §32 fully checked, `dotnet pack`
  succeeds for all three packages, CD dry-run green.

---

# 13. Decisions required from the owner

Each has a recommendation. None blocks R0–R3; D-1, D-2, D-5 must be settled before R6.

| # | Decision | Recommendation | Needed by |
|---|---|---|---|
| D-1 | V0 default compression | None; configured explicitly | R7 |
| D-2 | Schema fingerprint extension (W7) | Yes, opt-in, critical when present | R6 |
| D-3 | Null bitmap for positional objects (W5) | Defer to Track A size data after R6 | after R6 |
| D-4 | Zstandard and LZ4 | Optional packages (`ViShap.Viper.Compression.Zstd`, `….Lz4`) with a native dependency, outside the core | R5 |
| D-5 | Async surface | Frame-edge only, as §9 | R3 |
| D-6 | Header-driven convenience | One factory, `BinarySerializerOptions.FromPayload`, in place of twelve extension overloads | R3 |
| D-7 | Publishing `ITypeFormatter` | Not in v1.0; revisit with the generator | R8 |
| D-8 | Populate-in-place matrix | One entry per source kind, member-encoded types only (as today) | R3 |
| D-9 | Format version number of the rebuilt V1 | Keep 1 — there is no legacy to distinguish from | R6 |
| D-10 | Generator release label | Minor (v1.x) is sufficient technically; the label is a product decision | R8 |

---

# 14. Risks

| Risk | Where | Mitigation |
|---|---|---|
| The typed engine costs more cold start and code size than it saves | R4 | Measured against R0 before the stage closes; `ContractColdRunner` already exists |
| A typed shape quietly takes a loop back from the engine | R4 | INV-5 structural tests are extended to typed shapes in the same stage, not after |
| Internal stages change behaviour unnoticed | R1–R5 | The wire does not change before R6, so the frozen fixtures and the R0 oracle catch it |
| The fixture exception becomes a habit | R6 | Used once, recorded in `CLAUDE.md`, and the rule is restored in the same change |
| Over-read from non-seekable streams | R3, R7 | Designed in §4 and §7; the delimiting rules are tested with a non-seekable double that fails on any read past the frame |
| The plan drifts from the code | all | Every stage rewrites its documents from the change files, in the stage |
| Release moves out | all | Accepted by the owner in the brief; R0–R3 already deliver the non-seekable and buffer entry points even if later stages slip |

---

# 15. Out of scope

- Implementing the source generator.
- A public formatter contract (D-7).
- A MAC family separate from authenticated encryption.
- Any native dependency in the three core packages.
- Competitor comparison before R9.
