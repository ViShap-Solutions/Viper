# ViShap.Viper — Audit remediation design

**Input:** `internal/audit/Problems.cs` (19 probes, all CONFIRMED by the user's run)
**Tracked as:** `System-Contract.md` §21.3 "Deferred hostile-audit hardening"
**Repo status:** pre-`v1.0.0`, no git tags, no golden wire files under `Fixtures/Wire/`
→ **wire-format changes are still free**; nothing here needs a V2 codec purely for compatibility.

Legend: **Verdict** = is the probe evidence of a defect · **Fix** = concrete remediation ·
**Blast radius** = wire / public API / performance impact.

---

# 0. Triage summary

| ID | Verdict | Severity | Nature |
|----|---------|----------|--------|
| S01 | **not a defect** (agreed) — missing *policy*, not a bypass | medium | new opt-in option |
| S02 | defect | **high** | header not bound to AEAD |
| S03 | defect | **critical** — proven `StackOverflow`, uncatchable | budget coverage |
| S04 | defect | medium | budget coverage |
| S05 | defect | medium | missing read-side payload bound |
| S06 | defect | high | allocate-before-validate |
| S07 | defect | **high** | key-buffer ownership |
| S08 | defect | **high** | key-buffer ownership + use-after-dispose |
| S09 | defect | medium | truncated decompression accepted |
| S10 | defect | medium | non-canonical payload accepted |
| S11 | defect | medium | eager materialization before limit |
| S12 | **partly agreed** — A: not a defect · B: real gap | medium | cumulative keyed-field budget |
| C01 | defect | high | `ref` struct overload never reads the value |
| C02 | defect | medium | reference identity limited to nested objects |
| C03 | **architectural** | medium | global ref ids vs. skip-tolerant keys |
| C04 | defect | **high** | implicit positional polymorphism corrupts data |
| C05 | defect | medium | contradictory attributes leak a member |
| C06 | defect | medium | write budget is absolute, not operation-relative |
| C07 | defect | low | framework exception leaks |

Additional defects found during this analysis, **not** in `Problems.cs` — all reproduced:

| ID | Observation |
|----|-------------|
| A01 | `Deserialize<T>(bytes, existingInstance)` on a non-member-encoded type silently misparses: `List<int>{1,2,3}` → `Count=0, Capacity=3`. |
| A02 | `Deserialize<T>(ref T)` on a member-bearing struct under `PreserveReferences` returns garbage (`X=0, Y=1280`) — the 5-byte reference frame is never consumed. Same root cause as C01. |
| A03 | `Serialize<object>(value)` writes the runtime type's members and reads back a bare `object` — silent total data loss (a special case of C04, hidden further by S10). |

---

# 1. Cross-cutting infrastructure

Five small pieces of infrastructure remove most of the individual findings. Build these first.

## 1.1 `BudgetScope` on `ITypeFormatter` (fixes S03, S04; enables C02)

`ITypeFormatter` is `internal`, so this is not a public API change.

```csharp
internal enum BudgetScope { Leaf, Structural, SelfManaged }

internal interface ITypeFormatter
{
    bool CanHandle(Type declaredType);
    void Write(BinaryPayloadWriter writer, object value, Type declaredType);
    object Read(BinaryPayloadReader reader, Type declaredType);

    // Fail-safe default: a new formatter is budgeted unless it opts out.
    BudgetScope Scope => BudgetScope.Structural;
}
```

- `Leaf` — fixed-size / self-contained encodings: `PrimitiveFormatter<T>`, `String`, `Enum`, `Half`,
  `Int128`, `UInt128`, `IntPtr`, `UIntPtr`, `Rune`, `BigInteger`, the 6 time formatters,
  the 8 numeric formatters, `StringBuilder`, `CultureInfo`, `Guid`, `Uri`, `Version`, `BitArray`,
  `Delegate`. ~30 one-line overrides.
- `Structural` — everything that recurses: arrays, collections, dictionaries, memory-like,
  tuples, `Lazy`, immutable/frozen/read-only. No edits needed (it is the default).
- `SelfManaged` — `NestedFormatter` only, *if* the reference work of §1.5 is deferred; after that
  refactor `NestedFormatter` becomes `Structural` too and the third category can be dropped.

`BinaryPayloadWriter.WriteValue` / `BinaryPayloadReader.ReadValue` then do, for
`Scope == Structural`:

```csharp
using var scope = Budget.EnterDepth();      // fixes S03
Budget.ConsumeObjectGraphNodes(1);          // fixes S04
```

Note that the default `MaxDepth = 512` now also counts container levels and
`MaxObjectGraphNodes = 1_000_000` now also counts containers. Both are deliberate behavioral
changes and belong in `System-Contract.md` §17/§20.

## 1.2 Availability check before allocation (fixes S06, hardens every blob read)

Every `new byte[lengthFromWire]` must first prove the bytes can even exist.

```csharp
internal interface IRemainingByteSource { long RemainingBytes { get; } }
```

implemented by `BoundedReadStream` (`Remaining`) and `BudgetedReadStream` (`_maxBytes - _bytesRead`).

`DeserializationGuard.ReadExactly` gains a pre-flight:

```csharp
long? available = stream switch
{
    IRemainingByteSource s => s.RemainingBytes,
    { CanSeek: true }      => stream.Length - stream.Position,
    _                      => null
};
if (available is { } a && length > a)
    throw new BinaryFormatException($"{what} declares {length} bytes but only {a} remain.");
```

This kills S06 (a 29-byte frame declaring 8 MB no longer allocates 8 MB) *and* the same
amplification for strings and byte blobs inside the payload, which today is bounded only by
`MaxStringBytes` / `MaxByteBlobBytes` (4 MB / 16 MB **per declaration**).

Additionally `V1FormatCodec.ReadAndUnwrap` should compare `header.OnDiskLength` against the wire
budget's remaining bytes before calling `ReadExactly`, so the error is a `BinaryLimitException`
naming `MaxWireBytes` rather than a truncation error.

## 1.3 Bounded materialization on write (fixes S11)

14 call sites do `((IEnumerable)value).Cast<object?>().ToList()` before any limit check
(`MutableAddFormatterBase`, `DictionaryFormatterBase`, `ImmutableBuilderFormatterBase`,
`ImmutableDictionaryFormatterBase`, `CustomCollectionFormatter`, `Frozen*`, `ReadOnly*`,
`ImmutableArray/Queue/Stack`, `ArraySegment`, `ObjectGraphDumper`).

```csharp
// BinaryPayloadWriter
internal List<object?> MaterializeBounded(IEnumerable source, long max, string what)
{
    if (source is ICollection c && c.Count > max)     // O(1) fast path, no enumeration
        throw new BinaryLimitException($"{what} {c.Count} exceeds the configured maximum of {max}.");

    var items = new List<object?>();
    foreach (var item in source)
    {
        if (items.Count == max)
            throw new BinaryLimitException($"{what} exceeds the configured maximum of {max}.");
        items.Add(item);
    }
    return items;
}
```

The existing `Validate*ForWrite` call stays afterwards — that is what consumes the *element budget*;
`MaterializeBounded` only enforces the per-collection cap early. Do not consume the budget twice.

## 1.4 Operation-relative write budget (fixes C06)

`BudgetedWriteStream` measures absolute stream positions, so a destination already at offset 40 is
"over budget" before a single byte is written.

```csharp
private readonly long _origin;      // inner.CanSeek ? inner.Position : 0, captured in the ctor
// budget test becomes:  (endPosition - _origin) > _maxBytes
```

Apply the same relative arithmetic in `Position.set`, `Seek` and `SetLength`, and delete the
constructor's "already positioned beyond the maximum" check. `MaxWireBytes` then means *bytes
produced by this operation*, which is what the name and `System-Contract.md` §20 imply.

## 1.5 Reference layer rework (fixes C02, C03; completes C01/A02)

Today reference framing lives exclusively in `BinaryPayloadWriter.WriteNestedTracked` /
`ReadNestedTracked`, i.e. only objects encoded member-by-member take part in identity. Move it up
into `WriteValue` / `ReadValue`:

```
WriteValue(value, declaredType):
    null flag (reference types / Nullable<T>)
    if (_preserveReferences && !effectiveType.IsValueType && formatter.Scope == Structural)
        marker + id            // a back-reference short-circuits here
    formatter.Write(...)
```

Consequences:

1. **C02 fixed** — a shared `List<int>`, array or dictionary keeps identity across the graph.
2. **Boxed structs stop being framed.** Today `WriteNestedTracked` writes a marker + id for value
   types too, even though a boxed struct can never be shared; that dead frame is exactly what
   corrupts `Deserialize(ref T)` in A02.
3. **Identity registration protocol.** The reader must register an instance *before* reading its
   children, otherwise cycles cannot close. Formatters split into:
   - *early-registering*: nested objects, mutable collections/dictionaries
     (create → `reader.RegisterReference(id, instance)` → fill);
   - *late-registering*: arrays, immutable/frozen collections, tuples, `Lazy` — the instance does not
     exist until its children are read. A back-reference resolving to a not-yet-registered id must
     throw a deterministic `BinaryFormatException` ("reference to an object that is still being
     constructed — a cycle through an immutable container is not representable"), never dangle.
     Pre-allocating arrays to register them early would re-introduce the allocation amplification
     that `System-Contract.md` §17 forbids, so arrays stay late-registering.

**C03 — scope discipline.** With keyed contracts an unknown key is length-skipped, so every object id
defined inside it disappears. A global id table therefore makes schema evolution data-dependent
(C03's `BinaryFormatException: … not found`). Make id visibility follow the ancestor chain instead of
the whole payload:

- the writer keeps a *stack* of scopes; entering a keyed field payload pushes one, leaving pops it;
- a back-reference is emitted only when the target id is visible in the current stack (the object is
  an ancestor, or a sibling **within the same field**); otherwise the object is written out again;
- ids stay globally unique (single counter), so the reader never has to reconcile numbering;
- the reader mirrors push/pop around each keyed field payload and resolves ids by searching the stack.

Skipping an unknown sibling field can then never produce a dangling reference, while cycles back to
ancestors still resolve. The documented cost: an object shared between two *sibling keyed fields* is
duplicated on read. Cross-field cycles (`A.Field1 = B; B.Parent = A`) still work, because `A` is an
ancestor of the field being read.

Rejected alternatives: per-field independent scopes (breaks ancestor cycles → infinite recursion on
write) and "forbid `PreserveReferences` together with `[BinaryContract]`" (removes working
functionality).

---

# 2. Findings, one by one

## S01 — AES-configured reader accepts plaintext · **not a defect, add a policy**

Agreed with the note in `Problems.cs`: an `Encryptor` is a *capability*, the header says how a
*specific message* is protected, and V1 already rejects a header/`keyId` mismatch. The probe only
proves `Encryptor != RequireEncryption`.

It does matter as a companion to S02, though: binding the header into the AEAD stops *tampering*, but
an attacker can still replace the whole message with an unencrypted one. Add explicit downgrade
protection:

```csharp
public bool RequireEncryption { get; init; }   // BinarySerializerOptions
public bool RequireChecksum   { get; init; }
```

enforced in `V1FormatCodec.ReadAndUnwrap` right after `ReadFrom`:

```csharp
if (_requireEncryption && header.Encryption == EncryptionAlgorithm.None)
    throw new BinaryIntegrityException(
        "The payload is unencrypted, but this serializer is configured to require encryption.");
```

plus `.RequireEncryption()` / `.RequireChecksum()` on the builder. `BinaryIntegrityException` is the
right category: the failure is a protection downgrade, not a malformed stream.

**Blast radius:** additive, opt-in. **Tests:** plaintext rejected when required; encrypted accepted;
V0 rejected outright when `RequireEncryption` is set.

## S02 — V1 header is not authenticated · high

`BinaryFormatHeaderV1` is written in the clear and never enters the AEAD computation
(`Aes256Gcm.Encrypt` passes no associated data), so `PreserveReferences`, the algorithm kinds, the
custom algorithm names, `keyId` and the checksum bytes are all attacker-malleable while the GCM tag
still validates. Flipping `PreserveReferences` alone changes how the entire payload is parsed;
flipping `ChecksumAlgorithm` to `None` disables verification.

**Fix — bind the header as AAD.**

1. Core: add AAD-aware overloads with default implementations, so existing custom algorithms compile
   unchanged:

```csharp
public interface IEncryptionAlgorithm
{
    ...
    bool AuthenticatesAssociatedData => false;

    int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> associatedData, Span<byte> destination)
        => Encrypt(plaintext, key, destination);

    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> associatedData, Span<byte> destination)
        => Decrypt(ciphertext, key, destination);
}
```

   `Aes256Gcm` overrides both (`aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData)`) and
   reports `AuthenticatesAssociatedData => true`.

2. `IEncryptor.Encrypt/Decrypt` take `ReadOnlySpan<byte> associatedData` (add default-implemented
   overloads forwarding to the current signatures for source compatibility).

3. `BinaryFormatHeaderV1.BuildAssociatedData()` — a canonical encoding of **version, compression kind
   + custom name, checksum kind + custom name, encryption kind + custom name, keyId,
   PreserveReferences, UncompressedLength, CompressedLength, checksum length + bytes**.
   `OnDiskLength` is excluded because it is only known *after* encryption; it is already
   self-verifying (a wrong value truncates the read or fails the tag).

4. `V1FormatCodec.Serialize` reorders to: serialize → checksum → compress → build AAD → encrypt →
   write header + ciphertext. `ReadAndUnwrap` rebuilds the AAD from the parsed header before
   `Decrypt`.

5. Optional hardening: when `RequireEncryption` is on and the resolved algorithm reports
   `AuthenticatesAssociatedData == false`, throw `BinaryConfigurationException` — otherwise a custom
   algorithm silently keeps the old, unauthenticated behavior.

**Blast radius:** V1 bytes are unchanged (AAD is never stored), but previously produced *encrypted*
payloads no longer authenticate. Acceptable pre-1.0; must be called out in the release notes.

**Tests:** every header byte flipped individually → `BinaryIntegrityException`; round trip still
works; `NoEncryption` unaffected; a custom non-AEAD algorithm keeps working.

## S03 — recursive collection types bypass depth · **critical**

`MaxDepth` is only charged in `WriteNestedTracked`/`ReadNestedTracked`. A type like
`class Tree : List<Tree>` is claimed by `CustomCollectionFormatter`, which never enters a depth
scope, so nesting is unbounded on both read and write.

This is not merely a limit bypass. Verified in an isolated process: a hand-built **~1.2 MB** V1 frame
(200 000 nesting levels, ~6 bytes each) with **default limits** kills the process with an uncatchable
`StackOverflowException` inside `BinaryPayloadReader.ReadValue`. Any service that deserializes
untrusted bytes is remotely crashable and no `try/catch` can save it.

**Fix:** §1.1 (`BudgetScope.Structural` → `EnterDepth()` in both `ReadValue` and `WriteValue`).
With `MaxDepth = 512` the same payload fails with `BinaryLimitException` after 512 levels.

**Tests:** recursive-collection payload at `MaxDepth + 1` → `BinaryLimitException` on read *and* on
write; jagged arrays, dictionaries of dictionaries, tuple nesting; keep a large synthetic-depth
regression payload in the limits test file so CI covers the crash case.

## S04 — containers are not object-graph nodes · medium

Same root cause: only `NestedFormatter` calls `ConsumeObjectGraphNodes`. `List<List<int>>` with
`MaxObjectGraphNodes = 1` reads 4 containers happily.

**Fix:** §1.1 (`ConsumeObjectGraphNodes(1)` for structural formatters).
**Tests:** container-only graphs hit the node budget; the budget is cumulative across a payload;
back-references (under the reworked reference layer) do not double-charge.

## S05 — V0 read path has no payload bound · medium

`V0FormatCodec.Serialize` wraps `BudgetedWriteStream(wire) → BudgetedWriteStream(payload)`, but all
three `Deserialize` overloads wrap only `BudgetedReadStream(wire, MaxWireBytes)`. `MaxPayloadBytes`
is therefore unenforced on read — asymmetric with both the write path and V1.

**Fix:**

```csharp
using var wire    = new BudgetedReadStream(source, _limits.MaxWireBytes,    "wire",    leaveOpen: true);
using var payload = new BudgetedReadStream(wire,   _limits.MaxPayloadBytes, "payload", leaveOpen: true);
using var reader  = new BinaryReader(payload, Encoding.UTF8, leaveOpen: true);
```

in all three overloads. **Tests:** V0 read of a payload one byte over `MaxPayloadBytes` →
`BinaryLimitException`; exactly at the limit → succeeds.

## S06 — allocation precedes the wire budget check · high

`ReadAndUnwrap` calls `DeserializationGuard.ReadExactly(stream, header.OnDiskLength, …)`, which does
`new byte[length]` *before* reading, so a 29-byte frame allocates its declared 8 MB and only then
fails the `MaxWireBytes = 64` budget. Amplification here is ~290 000×; with default limits a single
29-byte frame can force a 64 MB allocation.

**Fix:** §1.2. **Tests:** `GC.GetAllocatedBytesForCurrentThread()` around a frame declaring a huge
`OnDiskLength` stays small; same for an over-declared string and byte blob inside the payload; the
error is `BinaryLimitException` when the declaration exceeds a configured maximum and
`BinaryFormatException` when it merely exceeds the bytes actually present.

## S07 — resolver-supplied key buffer is zeroed · high

`Encryptor.ResolveKey` returns the resolver's own array, and the `finally` in `Encrypt`/`Decrypt`
runs `CryptographicOperations.ZeroMemory(key)` on it. A resolver that returns a cached key (the
normal implementation) has its key destroyed after the first use; the *second* encryption then runs
with an all-zero key — the probe decrypts that ciphertext with `new byte[32]`. This is a silent
downgrade to a known key, i.e. total loss of confidentiality, and it also corrupts every other
consumer of that cache.

**Fix:** the encryptor must never mutate memory it does not own.

```csharp
private byte[] ResolveKeyCopy(string? keyId)   // returns a private copy; the caller zeroes the copy
```

Copy the resolver's result into a fresh array (or a pooled buffer cleared on return) and zero *that*.
Document that key material handed out by a resolver stays the caller's property.

**Tests:** the resolver's array is unchanged after `Encrypt`/`Decrypt`; two consecutive encryptions
through the same resolver produce ciphertexts that both decrypt with the real key and neither with a
zero key.

## S08 — disposal zeroes the caller's key; a disposed encryptor still encrypts · high

`Encryptor(algorithm, key)` stores the caller's array by reference and `Dispose()` zeroes it, so
disposing the encryptor destroys a key the caller may still be using elsewhere. Worse, `_disposed` is
never checked, so the disposed instance keeps encrypting — with the now all-zero key.

**Fix:**

1. defensive copy in the ctor: `_fixedKey = (byte[])key.Clone();` (and validate non-empty);
2. `Dispose()` zeroes only that copy;
3. `ObjectDisposedException.ThrowIf(_disposed, this)` at the top of `Encrypt` and `Decrypt`;
4. **`Encryptor.None` must be immune** — it is a process-wide singleton reached through
   `BinarySerializerOptions.Default`. Once (3) exists, a single `using var e = options.Encryptor;`
   anywhere would poison every serializer in the process. Give the private ctor an `_isShared = true`
   flag and make `Dispose()` a no-op for it.

**Tests:** the caller's key is intact after `Dispose`; use after `Dispose` → `ObjectDisposedException`;
`Encryptor.None.Dispose()` followed by a normal round trip still works.

## S09 — Deflate accepts truncated output · medium

`Deflate.Decompress` fills `destination` and returns; it never checks whether the deflate stream had
*more* data. `Compressor.Decompress` only asserts `written == uncompressedLength`, so a payload whose
real decompressed size is 1024 is accepted as a valid 4-byte payload — the other 1020 bytes are
silently dropped. That is a format-confusion primitive (two readers can disagree about the same
bytes), and with `ChecksumAlgorithm.None` nothing else catches it. `Brotli` is unaffected:
`BrotliDecoder.TryDecompress` returns `false` when the destination is too small.

**Fix:** after the fill loop, probe for one more byte:

```csharp
if (totalRead == destination.Length && deflate.ReadByte() != -1)
    throw new BinaryFormatException(
        "Deflate decompression produced more data than the declared uncompressed length.");
```

**Tests:** declared length < actual → `BinaryFormatException`; declared length > actual → the existing
"produced N bytes, expected M"; exact match → success; both algorithms covered.

## S10 — trailing payload bytes are ignored · medium

After the root value is read, V1 never checks that the raw payload was fully consumed, so
`[123][456]` deserializes as `123`. Keyed fields already enforce this ("payload contains trailing
bytes after decoding …"); the root does not. It also masks other bugs — A03 (`Serialize<object>`)
only looks harmless because the leftover bytes are dropped.

**Fix:** in `V1FormatCodec.DeserializePayload` and both `existingInstance` paths, after reading:

```csharp
if (ms.Position != rawPayload.Length)
    throw new BinaryFormatException(
        $"Payload contains {rawPayload.Length - ms.Position} trailing byte(s) after the root value.");
```

V0 is deliberately excluded: it is a headerless raw payload that may legitimately be embedded in a
larger stream.

**Tests:** V1 trailing byte → `BinaryFormatException`; exact payload → success; V0 with trailing data
still succeeds (documented difference).

## S11 — the writer materializes the whole sequence before the limit · medium

`MutableAddFormatterBase.Write` does `.Cast<object?>().ToList()` first and validates afterwards, so a
1000-element lazy sequence is fully enumerated (side effects included) under
`MaxCollectionLength = 1`. For an infinite `IEnumerable<T>` this never terminates.

**Fix:** §1.3, applied at all 14 sites. **Tests:** enumeration stops at `max + 1` items; an infinite
generator throws `BinaryLimitException` instead of hanging; the `ICollection` fast path does not
enumerate at all.

## S12 — keyed fields and the element budget

**Question A (should unknown keyed fields consume `MaxTotalElements`?) — no, agreed.**
`MaxTotalElements` bounds *data elements*; keyed fields are schema/structure. Coupling them would
make `MaxTotalElements = 2` mean "an object may not have a third field", which mixes two unrelated
dimensions. The probe is not evidence that `MaxTotalElements` is broken.

**Question B (a cumulative field budget) — real gap, fix it.** `MaxKeyedFields` is per object
(`ReadKeyedMembers`), so 100 000 objects × 1 000 fields = 10⁸ fields, each individually legal. The
only real ceiling today is `MaxPayloadBytes` (64 MB ÷ ~6 bytes per skipped field ≈ 10⁷ fields), and
each field costs a `HashSet<int>` insert plus a skip — work that is accounted nowhere.

**Fix:**

```csharp
// SerializationLimits
public long MaxTotalKeyedFields { get; init; } = 10_000_000;   // mirrors MaxTotalElements
// SerializationBudget
public void ConsumeKeyedFields(long count) { ... }             // same monotonic pattern
```

charged with `fieldCount` in `ReadKeyedMembers` *before* the loop (so skipped/unknown fields count —
that is the whole point) and with `membersByKey.Count` in `WriteKeyedMembers`. `MaxKeyedFields` keeps
its per-object meaning. Add `Validate()` coverage and the `System-Contract.md` limit-table entry.

**Tests:** many small keyed objects exceeding the cumulative budget → `BinaryLimitException`; a single
object below `MaxKeyedFields` still passes; unknown keys count toward the cumulative budget.

## C01 / A02 — `Deserialize<T>(ref T)` never reads the value · high

`BinaryPayloadReader.Deserialize<T>(ref T)` calls `PopulateMembers(boxed, typeof(T))` directly. For
`int` the member plan is empty (`Int32.m_value` is `readonly` → excluded), so nothing is read and the
caller's value is silently left untouched. For a member-bearing struct under `PreserveReferences` it
is worse: the 5-byte marker/id frame written by `WriteNestedTracked` is never consumed, so members
are read from misaligned bytes (`X=0, Y=1280` — verified).

**Fix:** mirror the writer instead of reimplementing it:

```csharp
public void Deserialize<T>(ref T existingInstance) where T : struct
    => existingInstance = (T)ReadValue(typeof(T))!;
```

A struct is copied by value anyway, so "populate in place" and "read and assign" are observationally
identical under positional layout, and this automatically tracks any framing the writer adds.

**Tests:** `ref int`, `ref` member-bearing struct, with and without `PreserveReferences`, V0 and V1;
`ref` struct with a nested class member.

## C02 — collection references are not preserved · medium

`PreserveReferences` only frames member-encoded objects, so a `List<int>` shared by two properties
becomes two instances. `System-Contract.md` §16 promises "reference identity where supported" without
carving collections out.

**Fix:** §1.5. If the full rework is deferred, the contract must state explicitly that identity is
preserved for member-encoded objects only.
**Tests:** a shared `List<T>`, `T[]` and `Dictionary<K,V>` keep identity; a self-referencing list
round trips; a cycle through an immutable collection fails with the deterministic "still being
constructed" message.

## C03 — skipping unknown keys breaks the reference table · architectural

Reference ids are global to the payload, but an unknown keyed field is length-skipped without being
parsed, so every id defined inside it is lost. A later back-reference to such an object fails with
`BinaryFormatException: Reference to object id N was not found`. The two features V1 advertises —
`PreserveReferences` and schema-evolution tolerance — are silently incompatible, and the failure is
data-dependent (it appears only when a shared object happens to be written first inside a removed
field).

**Fix:** the scope-stack discipline of §1.5 — ids are visible only along the ancestor chain, siblings
cannot reference each other across keyed fields, so skipping one is always safe. Duplicated
sibling-shared objects are the documented cost.

If that rework is deferred, at minimum make the failure legible: track `bool _skippedUnknownKeys` in
the reader and, when a lookup misses while it is set, throw with an actionable message naming schema
evolution as the cause instead of the current generic text.

**Tests:** an old schema reading a new payload where the removed field held the first occurrence of a
shared object → success (not an exception) under the scope rework; a cross-field cycle to an ancestor
still resolves; sibling sharing round trips with duplication and is asserted explicitly.

## C04 / A03 — implicit positional polymorphism · high

`WritePolymorphicOrPlainNested` builds the member plan from `value.GetType()` while the reader builds
it from `declaredType`. Without a `[BinaryUnion]` map, `Serialize<Base>(new Derived{A=11,Z=22})`
writes `Derived`'s plan (`A`, then `Z` — ordinal name order) and the reader consumes the first int as
`Base.Z`, yielding `Z == 11`: **silent data corruption, no exception**. `Serialize<object>(x)` is the
degenerate case — the runtime type's members are written and *nothing* is read back (A03, verified).

`System-Contract.md` §15 already requires: *"implicit positional polymorphism must never silently
reinterpret a derived layout as a base layout."*

**Fix:** in `WritePolymorphicOrPlainNested`, when there is no polymorphic map and
`value.GetType() != declaredType`:

```csharp
throw new BinaryTypeException(
    $"Declared type '{declaredType}' received a value of runtime type '{value.GetType()}', " +
    $"but '{declaredType.Name}' has no [BinaryUnion] map — the derived layout cannot be read back. " +
    $"Add [BinaryUnion(tag, typeof({value.GetType().Name}))] to '{declaredType.Name}'.");
```

Nothing that works today breaks: without a union map the reader always instantiates `declaredType`,
so any mismatch was already unreadable — only the failure moves from silent corruption at read to a
clear exception at write.

**Tests:** base-declared derived value → `BinaryTypeException`; `object`-declared value →
`BinaryTypeException`; with `[BinaryUnion]` → the correct runtime type; interface-declared members;
the same rule inside collections (`List<Base>` holding a `Derived`).

## C05 — `[BinaryKey]` + `[BinaryIgnore]` leaks the member · medium

`BuildContractPlan` computes `unmarked = !HasKey && !HasIgnore` and then `keyed = HasKey`, so a member
carrying *both* attributes passes the "exactly one of" check and is serialized despite
`[BinaryIgnore]`. The error message already promises "exactly one of"; the code does not enforce it.
A member the developer believes is excluded (the probe names it `Secret`) goes on the wire.

**Fix:** reject the contradiction where the other contract violations are rejected:

```csharp
var contradictory = candidates.Where(c => c.HasKey && c.HasIgnore).ToArray();
if (contradictory.Length > 0)
    throw new BinaryTypeException(
        $"'{type}' member(s) [{…}] have both [BinaryKey] and [BinaryIgnore] — " +
        "a contract member needs exactly one of them.");
```

Consider the mirror case in `BuildPositionalPlan`: `[BinaryInclude]` + `[BinaryIgnore]` currently
resolves silently to "ignore wins". Make it explicit — either throw, or document the precedence in
`System-Contract.md` §13.

**Tests:** both attributes → `BinaryTypeException` at plan build (i.e. on first serialize *and*
deserialize); `[BinaryIgnore]` alone on a contract member is still legal and excluded.

## C06 — the write budget counts the destination's pre-existing offset · medium

See §1.4. A 33-byte frame fits `MaxWireBytes = 40` at offset 0 and is rejected at offset 40, so
appending to a non-empty `FileStream`/`MemoryStream` fails for reasons unrelated to the data.

**Tests:** the same payload at offsets 0 / 40 / 10 000 behaves identically; the budget still trips on
the bytes actually written; keyed back-patching (`Position` set backwards, then forwards) stays
correct.

## C07 — invalid `Guid` leaks `ArgumentException` · low

`GuidFormatter.Read` does `new Guid(reader.RawReader.ReadBytes(16))`; on a truncated payload
`ReadBytes` returns fewer bytes and the `Guid` constructor throws `ArgumentException`, escaping the
`BinarySerializerException` hierarchy. `Int128Formatter` and `UInt128Formatter` have the identical
pattern (`BinaryPrimitives.ReadInt128LittleEndian` over a short array → `ArgumentOutOfRangeException`).
`System-Contract.md` §22 requires that raw parser exceptions never escape.

**Fix:** a guarded fixed-size read, allocation-free:

```csharp
// DeserializationGuard
internal static void ReadExactly(Stream stream, Span<byte> destination, string what)
```

filling the span and throwing `BinaryFormatException($"{what} ended early…")` on a short read. Use it
from all three formatters with a `stackalloc byte[16]`.

**Tests:** a 1-byte payload for `Guid`, `Int128` and `UInt128` → `BinaryFormatException`; a sweep
asserting that no `ArgumentException`/`EndOfStreamException` escapes any formatter on truncated input.

## A01 — `Deserialize<T>(bytes, existingInstance)` on non-member types · new, high

`BinaryPayloadReader.Deserialize<T>(T existingInstance)` always calls `PopulateMembers`, which uses
the *member plan* of `T`. For `List<int>` that plan is `{ Capacity }` (public get/set), while the
payload was written by `ListFormatter` as `count, elements…`. Verified: serializing `List<int>{1,2,3}`
and populating an existing list yields `Count = 0, Capacity = 3` — the element count is parsed as
`Capacity` and the elements are dropped, with no exception. The same applies to dictionaries, arrays,
`StringBuilder`, and any type claimed by a non-nested formatter.

**Fix:** reject the unsupported shape instead of misparsing:

```csharp
if (TypeFormatterRegistry.Resolve(typeof(T)) is not NestedFormatter)
    throw new BinaryTypeException(
        $"Populate-in-place is only supported for member-encoded types; '{typeof(T)}' is handled by " +
        $"a dedicated formatter. Use Deserialize<{typeof(T).Name}>() instead.");
```

(Supporting in-place fill for collections is possible later — clear and `Add` — but it must be an
explicit contract decision, not an accident.) With S10's trailing-byte check this case would start
throwing `BinaryFormatException` anyway; the targeted message is better.

**Tests:** `List<T>`, `Dictionary<K,V>`, `T[]` and `string` existing-instance overloads →
`BinaryTypeException`; a member-encoded class still populates in place and returns the same reference.

---

# 3. Suggested sequencing

Each phase is an independently reviewable branch; phases 1–3 have no cross-dependencies.

**Phase 1 — key material (no wire impact): S07, S08.**
Confidentiality bugs, self-contained in `Encryptor`. Ship first.

**Phase 2 — hostile input (no wire impact): S03, S04, S06, S05, S09, S11, S12-B, C07.**
Builds §1.1–§1.3. S03 is the release blocker (remote, uncatchable crash).

**Phase 3 — type-layer correctness (write-side rejections): C04/A03, C05, A01, C01/A02, C06.**
Turns four silent-corruption paths into exceptions. Public behavior changes, so it wants its own
release note.

**Phase 4 — strictness and policy (wire semantics): S10, S01 (`RequireEncryption`), S02 (AAD).**
S02 invalidates previously produced encrypted payloads — do it before `v1.0.0`, never after.

**Phase 5 — reference layer rework (wire format under `PreserveReferences`): C02, C03 (§1.5).**
The largest change; needs the identity-registration protocol and its own test matrix. Must land
before `v1.0.0`, otherwise `PreserveReferences` ships with the limitations documented instead.

**Documentation to update in lockstep:** `System-Contract.md` §13 (attribute contradictions), §15
(implicit polymorphism), §16 (reference scope rules), §17/§20 (containers now charge depth and the
node budget; new `MaxTotalKeyedFields`), §21.3 (move items from "deferred" to "covered") and the
release checklist; `CLAUDE.md` (new formatters must declare their `BudgetScope` and must use
`MaterializeBounded` on the write path).
