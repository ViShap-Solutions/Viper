# ViShap.Viper — System Contract

**Target release:** v1.0.0  
**Status:** Normative product contract  
**Scope:** `src/` production behavior only  
**Test framework reference:** xUnit 2.9+ (see `QA-Plan.md`)  
**Benchmark framework reference:** BenchmarkDotNet (see `Benchmark-Plan.md`)

---

# 1. Purpose

This document defines the observable and normative behavior of ViShap.Viper. It is the source of truth for public API semantics, format/version behavior, configuration, limits, resource accounting, contracts, polymorphism, reference semantics, exception taxonomy, stream behavior, and release conditions.

It is meant to be exhaustive: the public surface (§3), the wire format down to the byte (§22) and the
set of supported types with their encodings (§23) are all specified here, so a test — or a second
implementation — can be written from this document without reading the source. Where the document and
the code disagree, that is a defect in one of them, not a matter of interpretation; §22 and §23 are
pinned by tests for exactly that reason.

The goal is to make the system deterministic to implement, deterministic to test, and difficult to interpret in conflicting ways.

A successful `Serialize → Deserialize` round trip is not sufficient proof of correctness. The contract includes exact exception category, wire-version semantics, member-selection rules, ordering, runtime type identity, reference identity where supported, resource-limit behavior, and stream ownership.

---

# 2. Architecture and responsibility boundaries

The system is layered, and each rule has exactly one owner:

```text
Public API            BinarySerializer · StreamExtensions · attributes · exceptions
                      SerializationLimits · I*Algorithm · IKeyProvider · SecretKey
      │ creates exactly one operation per public call
SerializationOperation    Limits snapshot · Budget · PhaseBudget · Keys · policies
      │
FormatPipeline (V0|V1)    framing · phase order · header + AAD · phase sizes
      │  MeteredReadStream / MeteredWriteStream · WindowReadStream
PayloadEngine             traversal · depth · graph nodes · references · TypeContract
      │  ValueReader / ValueWriter — the only access to payload bytes
Formatters                type encoding only
      │
Algorithms                pure mechanics over spans
```

Dependencies point strictly downwards. No layer below the pipeline knows `SerializationLimits`.

## 2.1 Configuration

`BinarySerializerOptions` is the immutable configuration snapshot used by one `BinarySerializer`.

```csharp
BinarySerializerOptions.Default
BinarySerializerOptions.Configure()....Build()
```

Options carry **algorithms**, not orchestrators: `ICompressionAlgorithm`, `IChecksumAlgorithm`,
`IEncryptionAlgorithm`, an `IKeyProvider`, limits and policies. Nothing supplied through the public
API participates in enforcing a limit.

Properties have no public setters; `Configure()...Build()` is the only construction path, and it
validates once. Invalid configuration fails with `BinaryConfigurationException`.

## 2.2 Operation boundary

`SerializationOperation` is internal state created **exactly once per public call** by
`BinarySerializer` and passed down. No layer below constructs limits, a budget or a key provider of
its own.

It carries:

- `Limits` — the validated configuration snapshot;
- `Budget` — cumulative `TotalElements`, `ObjectGraphNodes`, `KeyedFields` and current `Depth`;
- `Phases` — payload/compressed/encrypted size policy;
- `Keys` — the key provider, if any;
- policies — `PreserveReferences`, `RequireEncryption`, `RequireChecksum`.

A budget is never shared between independent public calls. When a V1 header declares a different
reference mode than the local configuration, the engine continues with the same budget object, so
accounting stays cumulative for the whole call.

`SerializationLimits` answers **"what is allowed?"**.
`SerializationBudget` answers **"what has this operation already consumed?"**.

## 2.3 The byte boundary

`ValueReader` and `ValueWriter` are the only types that touch payload bytes. They expose checked
primitives: fixed-size reads that fail with `BinaryFormatException` instead of a framework exception,
length-prefixed strings and blobs bounded by their limits, and counts that can only be obtained as a
validated `ElementCount`.

There is no raw escape hatch. A formatter holds a `ValueReader`/`ValueWriter` and nothing else, so
"read a length and allocate it" is not expressible.

## 2.4 The traversal boundary

`GraphReader`/`GraphWriter` are the only recursion over an object graph. They own:

- depth accounting and unwinding;
- object-graph node accounting;
- reference identity and its scopes;
- cycle detection;
- the keyed-contract layout;
- element loops for every sequence and map.

A formatter describes a **shape** — scalar, sequence, map or composite — and supplies a builder. It
never owns a loop over attacker-controlled data, so it cannot omit an accounting step.

## 2.5 Phase-specific components

`PhaseBudget` inside the pipeline is the single place that checks payload, compressed and encrypted
sizes. `CompressionService`, `ChecksumService` and `EncryptionService` are internal and receive no
limits: they are called **inside** the barrier, never instead of it.

Public algorithm contracts (`ICompressionAlgorithm`, `IChecksumAlgorithm`, `IEncryptionAlgorithm`)
contain no serializer policy, so an external implementation cannot weaken a limit.

---

# 3. Public API surface

This is the whole public surface. Anything not listed is internal, and adding to this list is an API
change that belongs in a release note.

**`ViShap.Viper`** — `BinarySerializer`, `BinarySerializerOptions`, `BinarySerializerOptionsBuilder`,
`StreamExtensions`, and the attributes `[BinaryContract]`, `[BinaryKey]`, `[BinaryIgnore]`,
`[BinaryInclude]`, `[BinaryOrder]`, `[BinaryUnion]`.

**`ViShap.Viper.Security`** — `SerializationLimits`.

**`ViShap.Viper.Compression`** — `CompressionAlgorithm`, `ICompressionAlgorithm`, `NoCompression`,
`Deflate`, `Brotli`.

**`ViShap.Viper.Checksum`** — `ChecksumAlgorithm`, `IChecksumAlgorithm`, `NoChecksum`, `Crc32`.

**`ViShap.Viper.Crypto`** — `EncryptionAlgorithm`, `IEncryptionAlgorithm`, `NoEncryption`,
`Aes256Gcm`, `SecretKey`, `IKeyProvider`, `StaticKeyProvider`, `DelegateKeyProvider`.

**`ViShap.Viper.Metadata`** — `BinaryHeaderInfo`, `BinaryFormatInspector`.

**`ViShap.Viper.Diagnostics`** — `BinaryFormatDumper`.

**`ViShap.Viper.Exceptions`** — the hierarchy of §8.

Deliberately **not** public: the payload engine, the value primitives, the format pipelines, the
formatter contracts, the algorithm orchestrators, and the operation and budget types. Every one of
them enforces part of the resource policy or the traversal protocol, and publishing any of them would
let a caller step around it.

Every public type and member carries XML documentation. Both packages build with
`GenerateDocumentationFile`, so the documentation ships beside the assembly and a consumer sees it on
hover; the compiler reports an undocumented public member as a CS1591 warning. The documentation is
written for that consumer: what the member does, what it takes, what it returns and which exception
it raises. It does not cite this document, and it does not record how the code came to look the way
it does.

## 3.1 Serializer

The public `BinarySerializer` surface includes:

```csharp
BinarySerializer(BinarySerializerOptions? options = null);

void Serialize<T>(Stream destination, T data);
byte[] Serialize<T>(T data);

T? Deserialize<T>(Stream source);
T? Deserialize<T>(byte[] bytes);

T? Deserialize<T>(Stream source, T existingInstance) where T : class;
T? Deserialize<T>(byte[] bytes, T existingInstance) where T : class;

void Deserialize<T>(Stream source, ref T existingInstance) where T : struct;
void Deserialize<T>(byte[] bytes, ref T existingInstance) where T : struct;
```

Exact overloads exposed by the compiled public assembly are authoritative.

`byte[]` inputs are caller-owned. Serializer-created temporary streams remain internal.

Caller-provided streams are never disposed, never rewound, and are read only as far as the payload
extends.

An **empty `byte[]` is not a payload**: no wire version encodes a value in zero bytes, not even a
null root, which costs one byte. Every byte-array read overload therefore rejects it with
`BinaryFormatException` before any routing happens, and the existing instance or `ref` target is left
untouched. A zero-length array is never silently read as `default(T)`.

Populate-in-place semantics:

- `Deserialize<T>(…, T existingInstance)` is defined only for **member-encoded** types. A type
  claimed by a dedicated formatter (collection, dictionary, array, string, …) is rejected with
  `BinaryTypeException`; it must not be reinterpreted through that type's member plan.
- `Deserialize<T>(…, ref T existingInstance)` reads the root exactly as the writer framed it and
  assigns the result. A struct is copied by value, so this is observationally identical to populating
  in place, and it stays correct under every framing the writer may add.

## 3.2 Stream extensions

`StreamExtensions` builds a serializer per call, for occasional use and for payloads that describe
their own configuration. The surface is:

```csharp
void Serialize<T>(this Stream destination, T data, BinarySerializerOptions? options = null);

T? Deserialize<T>(this Stream source, BinarySerializerOptions options);
T? Deserialize<T>(this Stream source, SerializationLimits? limits = null);
T? Deserialize<T>(this Stream source, byte[]? key, SerializationLimits? limits = null);
T? Deserialize<T>(this Stream source, Func<string?, byte[]?> keyResolver, SerializationLimits? limits = null);

T? Deserialize<T>(this Stream source, T existingInstance, BinarySerializerOptions options) where T : class;
T? Deserialize<T>(this Stream source, T existingInstance, SerializationLimits? limits = null) where T : class;
T? Deserialize<T>(this Stream source, T existingInstance, byte[]? key, SerializationLimits? limits = null) where T : class;
T? Deserialize<T>(this Stream source, T existingInstance, Func<string?, byte[]?> keyResolver, SerializationLimits? limits = null) where T : class;

void Deserialize<T>(this Stream source, ref T existingInstance, BinarySerializerOptions options) where T : struct;
void Deserialize<T>(this Stream source, ref T existingInstance, SerializationLimits? limits = null) where T : struct;
void Deserialize<T>(this Stream source, ref T existingInstance, byte[]? key, SerializationLimits? limits = null) where T : struct;
void Deserialize<T>(this Stream source, ref T existingInstance, Func<string?, byte[]?> keyResolver, SerializationLimits? limits = null) where T : struct;
```

The overloads that take a key, a key resolver, or neither configure themselves from the payload's
header through `BinarySerializerOptions.FromStream` and therefore require a seekable stream.

**The header supplies algorithms, never policy.** It says how the payload was wrapped — compression,
checksum, encryption, key id — and nothing it contains can raise a resource ceiling. The limits
applied are the caller's `limits` argument, or `SerializationLimits.Default` when it is omitted. An
application reading untrusted data through these overloads passes its own policy exactly as it would
through `BinarySerializerOptions`.

Every overload leaves the caller's stream open.

---

# 4. Options and configuration

## 4.1 Canonical builder

```csharp
BinarySerializerOptions.Configure()
```

is the supported builder entry point.

Supported builder operations include:

```csharp
WithCompression(ICompressionAlgorithm)
WithChecksum(IChecksumAlgorithm)
WithEncryption(IEncryptionAlgorithm, ReadOnlySpan<byte> key, string? keyId)
WithEncryption(IEncryptionAlgorithm, Func<string?, byte[]?> keyResolver, string? keyId)
WithEncryption(IEncryptionAlgorithm, IKeyProvider, string? keyId)
WithVersion(int)
PreserveReferences(bool)
WithLimits(SerializationLimits)
AllowV0Fallback(bool)
RequireEncryption(bool)
RequireChecksum(bool)
RegisterCustomCompression(string, Func<ICompressionAlgorithm>)
RegisterCustomChecksum(string, Func<IChecksumAlgorithm>)
RegisterCustomEncryption(string, Func<IEncryptionAlgorithm>)
Build()
```

Null required arguments follow normal .NET argument semantics and use `ArgumentNullException`.

Custom algorithms are registered **on the builder** and snapshotted into the options. There is no
process-wide registry, and built-in algorithms cannot be substituted: what encrypts a payload is
determined by the options that were built, not by global state another component may have mutated.

A registration is a factory, and it is invoked once per resolution — that is, while a payload is
being read, because the header names the algorithm and writing uses the configured instance. What
runs, and how often, is therefore decided by the payload rather than by the caller, so a factory is
not allowed to take an operation outside the exception taxonomy: a factory that throws, and one that
returns `null`, are both `BinaryConfigurationException` naming the registration, with whatever the
factory threw preserved as `InnerException` (§9). An exception that is already part of the taxonomy
propagates unchanged, since wrapping it would add nothing the caller could not already catch. This is
the read-side counterpart of the rule for a `Lazy<T>` factory (§23), which propagates unwrapped
because it runs on the write path, over the caller's own value, at a point the caller chose.

`Build()` rejects contradictory configuration with `BinaryConfigurationException`:

- a write version that is not a supported wire format;
- `RequireEncryption` without an encryption algorithm;
- `RequireEncryption` with an algorithm that does not authenticate associated data;
- `RequireChecksum` without a checksum algorithm;
- `RequireEncryption` or `RequireChecksum` together with `WithVersion(0)`;
- `RequireEncryption` or `RequireChecksum` together with `AllowV0Fallback`.

The last two close both directions of the same contradiction: a headerless payload carries no
protection, so a policy demanding protection could be satisfied neither when writing one nor when
reading one (§10.2, §21.1). A configured algorithm without a policy is a capability, not a demand,
and stays legal under version 0 — it simply does not apply to what version 0 writes.

Missing key material is **not** among them. Every `WithEncryption` overload takes the key source in
the same call as the algorithm and rejects a null one with `ArgumentNullException`, so a builder
holding an algorithm without key material is not a state a caller can produce. `Build()` keeps the
guard as an invariant over the options it constructs, but no configuration reaches it.

Options derived from a payload rather than built — `FromHeader` and `FromStream` (§4.3) — may well
name an encryption algorithm the caller supplied no key for. That is a reader missing a key, not a
contradictory configuration: it is diagnosed when the payload is read, as
`BinaryEncryptionKeyException` (§8.7), alongside a key provider that resolves to nothing and a key
that does not match the header's `keyId`.

`WithVersion(n)` selects the format used for **writing** only, and is validated here rather than at
the first `Serialize`. `AllowV0Fallback` is the separate, **read-side** choice of whether a stream
without the magic number may be read as V0; writing V0 does not require it, and enabling it does not
change what is written.

## 4.2 Default configuration

Default behavior is V1 unless changed by configuration.

Default algorithm choices are:

```text
Compression = None
Checksum    = None
Encryption  = None
PreserveReferences = false
AllowV0Fallback    = false
```

The exact default algorithm instances are implementation details; the observable default semantics are part of this contract.

## 4.3 `FromHeader` and `FromStream`

`BinarySerializerOptions.FromHeader(...)` constructs options from V1 header metadata and caller-supplied key material or key resolver.

`KeyId` is metadata used to select the key. It is not key material and is never treated as a secret.

A key resolver receives the header `keyId`.

`BinarySerializerOptions.FromStream(...)` uses `BinaryFormatInspector.Peek(...)` and therefore requires a seekable stream.

Invalid supplied limits fail with `BinaryConfigurationException`.

---

# 5. Serialization limits

The canonical default limits are:

| Limit | Default |
|---|---:|
| `MaxDepth` | `512` |
| `MaxArrayLength` | `1_000_000` |
| `MaxCollectionLength` | `1_000_000` |
| `MaxDictionaryEntries` | `1_000_000` |
| `MaxStringBytes` | `4_000_000` |
| `MaxByteBlobBytes` | `16_000_000` |
| `MaxTotalElements` | `10_000_000` |
| `MaxObjectGraphNodes` | `1_000_000` |
| `MaxKeyedFields` | `1_000_000` |
| `MaxTotalKeyedFields` | `10_000_000` |
| `MaxPayloadBytes` | `64 MiB` |
| `MaxCompressedBytes` | `64 MiB` |
| `MaxEncryptedBytes` | `64 MiB + 64 KiB` |
| `MaxWireBytes` | `80 MiB` |

All configured limits must be strictly positive. Zero or negative configuration values are configuration errors:

```text
BinaryConfigurationException
```

A binary value of zero is not inherently invalid. For example, an empty collection, zero-length string/blob, and zero-length payload are valid wherever the corresponding wire type allows them.

The distinction is:

```text
configuration value <= 0 → BinaryConfigurationException
wire count/length < 0    → BinaryFormatException
wire count/length > max  → BinaryLimitException
```

## 5.1 `MaxDepth`

Limits structural nesting depth across the **whole** graph. Every structural value enters a depth
scope: member-encoded objects, arrays, collections, dictionaries, tuples and every other composite.
Scalars do not.

Depth accounting is operation-scoped and unwinds on both successful and exceptional exit; a failed
entry never leaves the depth incremented.

Because the payload engine owns the recursion, a recursive container type such as
`class Tree : List<Tree>` is bounded exactly like a recursive object graph. A payload nested deeper
than the limit fails with `BinaryLimitException` on read **and** on write, and can therefore never
reach the CLR stack limit.

A depth limit violation is:

```text
BinaryLimitException
```

## 5.2 `MaxArrayLength`

Limits one-dimensional array length and is also used for array-specific dimension/product validation.

## 5.3 `MaxCollectionLength`

Limits per-collection element count.

## 5.4 `MaxDictionaryEntries`

Limits per-dictionary entry count.

## 5.5 `MaxStringBytes`

Limits UTF-8 encoded byte length, not character count.

## 5.6 `MaxByteBlobBytes`

Limits byte-oriented payloads.

## 5.7 `MaxTotalElements`

Cumulative per-operation element budget. It must never decrease during one operation.

It is deliberately distinct from keyed-field metadata counts.

## 5.8 `MaxObjectGraphNodes`

Counts newly materialized structural nodes for the whole operation: member-encoded objects **and**
container instances — arrays, collections, dictionaries, tuples and other composites. A back
reference to an already materialized object does not create another node.

This limit is distinct from `MaxTotalElements` and `MaxDepth`.

## 5.9 `MaxKeyedFields`

Limits the number of fields in **one** keyed object.

It is a structural field-count limit, not a replacement for the cumulative element budget.

It is evaluated **before** `MaxTotalKeyedFields`, on both directions, so an object that breaches both
at once reports the per-object ceiling. A caller therefore always learns which limit the payload
actually broke.

## 5.9a `MaxTotalKeyedFields`

Cumulative per-operation count of keyed fields, including unknown fields that are skipped. It bounds
the aggregate metadata work of a payload made of many small keyed objects, each of which satisfies
`MaxKeyedFields` on its own.

It is deliberately separate from `MaxTotalElements`: field counts are schema metadata, element counts
are data.

## 5.10 Phase limits

`MaxPayloadBytes` bounds logical uncompressed payload.

`MaxCompressedBytes` bounds the compressed representation.

`MaxEncryptedBytes` bounds the encrypted/on-disk representation.

`MaxWireBytes` bounds physical stream bytes consumed/produced by one operation.

---

# 6. Resource accounting rules

`SerializationBudget` is cumulative within one operation and monotonic.

`ConsumeElements(n)`:

- rejects negative `n` with `BinaryFormatException`;
- rejects cumulative overflow beyond `MaxTotalElements` with `BinaryLimitException`;
- otherwise increases the cumulative count by exactly `n`.

`ConsumeObjectGraphNodes(n)` and `ConsumeKeyedFields(n)` follow the same pattern against
`MaxObjectGraphNodes` and `MaxTotalKeyedFields`.

`EnterDepth()` checks the limit before incrementing and returns a scope that restores the previous
depth exactly once. The scope is a `ref struct`, so entering a structural node costs no allocation.

A failed `EnterDepth()` must not leave the depth incremented.

Element counts are charged where they are read or written, inside `ElementCount.Validate`, which is
the only way to obtain an `ElementCount`. Validation and charging therefore cannot be separated from
using a count.

---

# 7. Security stream mechanisms

There are two mechanisms, in three types.

**Metering** — counting what one operation consumes or produces, relative to where it started.

## 7.1 `MeteredReadStream`

> Caps the bytes this operation reads from a caller-owned stream.

- counts from zero regardless of the caller stream's absolute position;
- exposes `RemainingBytes` — the lesser of the remaining budget and the bytes the source can still
  physically deliver — so a declared length is rejected before it drives an allocation. A chain of
  meters propagates the physical truth, because each one answers for itself;
- classifies a declaration it cannot satisfy by which bound it broke: beyond the configured ceiling
  is `BinaryLimitException`, while within the ceiling but beyond the remaining bytes means the
  payload is shorter than it claims, which is `BinaryFormatException`;
- bounds a *read* by the budget alone, so an ordinary short read stays a truncation for the caller
  to report rather than being reclassified as a limit violation;
- wraps underlying `IOException` as `BinaryStreamException`;
- never disposes the caller's stream.

Read paths compose it: V1 meters the wire; V0 meters the wire and the payload independently, so
`MaxPayloadBytes` applies symmetrically to reading and writing.

## 7.2 `MeteredWriteStream`

> Caps the bytes this operation produces.

- the budget is relative to the destination's starting position, so appending to a stream that
  already holds data costs the operation nothing;
- rewinds used for keyed-field length patching do not double-charge: the budget follows the
  high-water mark;
- tracks its own cursor rather than polling the underlying stream on every write;
- wraps underlying `IOException` as `BinaryStreamException`;
- never disposes the caller's stream.

**Windowing** — exposing exactly one declared subrange.

## 7.3 `WindowReadStream`

> Exposes exactly one declared subrange of an already metered stream.

Used for known keyed-field payloads. If a field declares `N` bytes, its decoder may consume at most
`N` bytes through the window and cannot read into the next field. The window knows its
`RemainingBytes` and classifies an over-read as `BinaryFormatException`, because running past a
declared window means the payload is shorter than it claims.

A window never materializes the field payload merely to enforce the boundary, and a known field is
decoded through it while sharing the parent operation's budget and reference state.

Unknown keyed fields are skipped in bounded chunks rather than copied into a single attacker-sized
byte array.
# 8. Exception taxonomy

The exception hierarchy is part of the public API contract:

```text
BinarySerializerException
├── BinaryConfigurationException
├── BinaryFormatException
│   └── BinaryLimitException
├── BinaryFormatNotSupportedException
├── BinaryIntegrityException
├── BinaryEncryptionException
│   └── BinaryEncryptionKeyException
├── BinaryStreamException
└── BinaryTypeException
```

## 8.1 `BinaryConfigurationException`

Invalid serializer/provider/security configuration.

Examples:

- invalid `SerializationLimits`;
- invalid configuration combinations;
- unusable configured provider state.

Not for null public arguments.

## 8.2 `BinaryFormatException`

Malformed or structurally invalid binary input.

Examples:

- truncated header/payload;
- malformed 7-bit integers;
- negative wire counts/lengths;
- invalid markers;
- inconsistent header lengths;
- malformed keyed payload structure.

When EOF means the declared binary structure is incomplete, raw `EndOfStreamException` must not escape the parser.

## 8.3 `BinaryLimitException`

Subtype of `BinaryFormatException`.

Use only when the binary value is structurally parseable but violates a configured resource/security limit.

Examples:

- max depth;
- max array/collection/dictionary size;
- max string/blob size;
- cumulative element budget;
- graph-node budget;
- keyed-field limit;
- payload/compressed/encrypted/wire phase limit.

## 8.4 `BinaryFormatNotSupportedException`

Recognizable but unsupported format/configuration.

Examples:

- unsupported format version;
- unknown built-in algorithm enum;
- missing custom registration.

## 8.5 `BinaryIntegrityException`

Evidence that data cannot be trusted as intact/authentic.

Examples:

- checksum mismatch;
- AES-GCM authentication failure;
- cryptographic tag failure.

A wrong AES key that causes authenticated-decryption failure is an integrity failure when the payload reaches the cryptographic authentication boundary.

## 8.6 `BinaryEncryptionException`

Operational encryption/decryption failure not better classified as format, integrity, or key-availability failure.

## 8.7 `BinaryEncryptionKeyException`

Key material availability or identity/configuration failure.

Examples:

- no key;
- resolver returns no key;
- configured key identity mismatch;
- key provider cannot supply usable material.

`KeyId` is a selector; the header never supplies secret key bytes.

## 8.8 `BinaryStreamException`

Underlying caller-stream I/O failure.

The original `IOException` is preserved as `InnerException`.

`BinarySerializer` must not globally catch `IOException` around the complete router/codec operation. Stream ownership and attribution are handled at stream boundaries.

## 8.9 `BinaryTypeException`

Invalid CLR type/contract/object-graph semantics.

Examples:

- invalid contract plan;
- invalid polymorphism map;
- invalid runtime type for declared polymorphic slot;
- invalid reference target;
- cycle rejection when the graph contract disallows cycles.

## 8.10 Standard .NET exceptions

These remain intentionally outside `BinarySerializerException`:

- `ArgumentNullException` — required public argument is null;
- `ArgumentException` — invalid direct caller argument;
- `NotSupportedException` — unsupported API capability, e.g. required seekability: reading, which
  must detect the format version before consuming anything, and writing a keyed contract under a
  format that does not buffer the payload (§10.2, §14.2).

Do not wrap every exception merely to force taxonomy symmetry.

---

# 9. Inner-exception preservation

A wrapper exception must preserve the lower-level exception when the lower-level failure is part of the documented diagnostic contract.

Examples:

```text
IOException
    → BinaryStreamException.InnerException

CryptographicException
    → BinaryIntegrityException.InnerException

InvalidDataException
    → BinaryFormatException.InnerException

custom algorithm factory exception
    → BinaryConfigurationException.InnerException
```

An unqualified:

```csharp
catch (BinarySerializerException)
{
    throw;
}
```

has no semantic purpose and must not be added.

A `catch` that performs required cleanup before rethrow is valid.

---

# 10. Version and wire-format contract

Two wire formats ship in v1.0. They are peers with different jobs, not a current format and a
deprecated one: V1 is the self-describing envelope, V0 the compact headerless codec. Neither is
derived from the other, neither is scheduled for removal, and a reader never guesses which one it is
holding (§10.3).

## 10.1 V1

V1 is the default format, and the one to use whenever the payload outlives the context that produced
it: stored data, data crossing a trust boundary, data whose schema will move, data read by a party
that was not configured by the writer.

Beyond the payload itself it provides:

- V1 header metadata;
- compression selection;
- checksum selection;
- encryption selection;
- `KeyId`;
- `PreserveReferences` metadata and reference framing;
- logical/physical length metadata.

The type system, `[BinaryUnion]` polymorphism, keyed contracts and the configurable resource limits
are not V1 features — they belong to the payload and to the engine, and apply to every format
(§10.2).

The V1 header is a security/format boundary and must validate all attacker-controlled lengths before they can drive an allocation.

A V1 payload is canonical: after the root value is read, the payload must be consumed exactly.
Trailing bytes are `BinaryFormatException`. The same rule already applies inside every keyed field.

## 10.2 V0

V0 is a self-contained compact codec: the payload and nothing else. It is the deliberate choice for a
caller who wants the smallest representation the engine can produce and who already knows, out of
band, what the bytes are.

It fits when:

- the transport is private or tightly coordinated, so both ends are configured together;
- framing and context already exist outside the payload — a message type, a length prefix, a channel;
- a compact codec path is worth more than a metadata-carrying envelope;
- IPC or another low-overhead channel is part of the design.

It is the wrong choice for data at rest, for anything that must be compressed, checksummed or
encrypted, and for anything read by a party the writer did not configure — those are V1's job.
Schema evolution is not on that list: a keyed contract (§14.2) is payload-level and works under V0
too.

What V0 does not have:

- a header of any kind: no magic number, no version, no algorithm names, no length metadata;
- a compression, checksum or encryption phase. Algorithms configured on the options are not applied
  to a V0 write, because a V0 reader has nowhere to learn that they were;
- reference preservation: `PreserveReferences` does not apply, and a cycle is `BinaryTypeException`
  (§16).

What V0 keeps is everything that lives below the envelope: the whole type system of §23,
`[BinaryUnion]` polymorphism, keyed contracts, the limits and budgets of §5–6, and metering on both
directions (§7.1). V0 is not a reduced engine — it is the same engine without a header, and for one
value under one layout the payload bytes are identical in both formats (§22.8).

Keyed contracts are a property of the type, not of the format, so a `[BinaryContract]` type encodes
identically under both. One consequence is visible to the caller: a keyed field's length is written
ahead of the field and patched once the field's size is known, so the payload stream must be
seekable. V1 buffers the payload and always satisfies this; under V0, which writes straight to the
destination, the requirement falls on the caller's destination stream, and one that cannot seek is
`NotSupportedException` (§8.10). The `byte[]` entry points of §3 buffer into memory, so they are
always seekable and are never affected. Positional writing carries no such requirement under either
format.

V0 and V1 must remain distinct wire formats. V1-specific behavior must not be accidentally required to parse valid V0 payloads.

A V0 payload is **unauthenticated by construction**. There is no header, so there is no associated
data to authenticate, no tag and no checksum; nothing about the bytes can be verified before they are
decoded, and the reader's only assurance that they are a V0 payload at all is that the caller said so.
V0 is therefore only appropriate on a channel that authenticates itself — a local IPC endpoint, a
mutually authenticated session, a file the process alone controls. This is a boundary of the format,
not a gap in it: a payload that must carry its own protection is a V1 payload.

That boundary is enforced at configuration time rather than left to be discovered. `Build()` rejects
`RequireEncryption` or `RequireChecksum` combined with `WithVersion(0)` or with `AllowV0Fallback`
(§4.1), which closes both directions: a serializer carrying a protection policy can neither produce
nor accept a V0 payload, so §21.1 needs no exception for the headerless format.

The cost of carrying no metadata is that nothing in a V0 payload identifies it. Reading V0 is
therefore an explicit decision and never an inference: `AllowV0Fallback` is what separates a
deliberate compact payload from unrelated bytes, and without it a stream that does not present the
V1 magic number is rejected rather than parsed (§10.3). The name is read-side only.

Writing V0 is selected by `WithVersion(0)` alone. `AllowV0Fallback` governs only whether a stream
without the magic number may be *read* as V0, so the two choices are independent in both directions.

## 10.3 Routing

Format routing first identifies the version from a seekable source.

If V1 magic/version is recognized, V1 is selected.

Otherwise the bytes are unidentified, and the serializer does not guess: V0 is selected only when the
caller enabled `AllowV0Fallback` and V0 is registered. Without that opt-in, unidentified input is
`BinaryFormatException`. The opt-in is a statement about the source, not about the format — it says
the caller knows that this channel carries headerless payloads.

Unsupported recognized versions produce `BinaryFormatNotSupportedException`.

Routing code may map an `IOException` raised by the version-detection probe to `BinaryStreamException`, but must not blanket-wrap the entire codec operation.

---

# 11. V1 header fields

The V1 header carries:

```text
Compression
CustomCompressionName
ChecksumAlgorithm
CustomChecksumName
Encryption
CustomEncryptionName
KeyId
PreserveReferences
UncompressedLength
CompressedLength
OnDiskLength
Checksum
```

Phase consistency rules include:

```text
Compression == None  → CompressedLength == UncompressedLength
Encryption == None   → OnDiskLength == CompressedLength
```

Lengths must be non-negative and within their corresponding phase limits.

Checksum length must fit the header representation.

Each header string — `CustomCompressionName`, `CustomChecksumName`, `CustomEncryptionName` and
`KeyId` — is limited to **256 UTF-8 bytes**. The ceiling is fixed by the format, not configured:
these fields name an algorithm or select a key, so `MaxStringBytes`, which bounds payload data, does
not apply to them. A declared header string above the ceiling is `BinaryFormatException`, because a
fixed format bound describes malformed input rather than a policy breach; a configured value too
large to write is `BinaryConfigurationException`.

Header truncation is `BinaryFormatException`.

---

# 12. Compression contract

The compression layer enforces:

```text
raw input ≤ MaxPayloadBytes
compressed output ≤ MaxCompressedBytes
compressed input ≤ MaxCompressedBytes
expected decompressed output ≤ MaxPayloadBytes
```

Phase sizes are checked by the pipeline, not by the algorithm: `ICompressionAlgorithm` implementations
receive no limits and are invoked inside the barrier.

Decompression produces **exactly** the declared uncompressed length. Producing fewer bytes and
producing more are both rejected, so a payload cannot declare a size that hides part of its own
content.

Malformed compressed data maps to `BinaryFormatException`.

Output buffer/phase-limit failures map to `BinaryLimitException` when the configured security limit is the reason for rejection.

No compression mode still honors configured phase limits.

Compression is the one phase where output can legitimately exceed input, so the ceiling on a
decompression bomb is `MaxPayloadBytes`. The attacker must still deliver `CompressedLength` real
bytes, which are metered and physically present before the uncompressed buffer is allocated.

---

# 13. Encryption contract

The encryption layer enforces:

```text
plaintext input ≤ MaxCompressedBytes
ciphertext output ≤ MaxEncryptedBytes
ciphertext input ≤ MaxEncryptedBytes
expected plaintext length ≤ MaxCompressedBytes
```

The declared plaintext length may never exceed the ciphertext actually delivered, so a short frame
cannot force a large allocation by claiming one.

## 13.1 Authenticated metadata

The V1 header is bound to authenticated encryption as associated data. The canonical image covers the
format version, the algorithm kinds and custom names, the key id, `PreserveReferences`,
`UncompressedLength`, `CompressedLength` and the checksum bytes.

`OnDiskLength` is excluded because it is only known after encryption; it is self-verifying, since a
wrong value either truncates the read or fails the authentication tag.

Altering any authenticated header byte fails with `BinaryIntegrityException`.

`IEncryptionAlgorithm` exposes AAD-aware overloads with default implementations that ignore the
associated data, together with `AuthenticatesAssociatedData`. The pipeline always builds the image
and always hands it to the AAD-aware overload, whatever the flag says, so the flag is a declaration
about the algorithm rather than a switch over the pipeline. Reporting `true` is the implementer's
undertaking that the associated data takes part in the authentication tag; the engine cannot verify
it. An algorithm that reports `false` — including one that simply does not implement the AAD-aware
overloads — leaves the V1 header as unauthenticated metadata.

`RequireEncryption` refuses such an algorithm from both sides, and the two sides are different
failures:

- configured locally, it is refused by `Build()` with `BinaryConfigurationException` (§4.1): the
  configuration would write metadata the policy claims to protect, and nothing is wrong with the
  data, because there is none yet;
- named by a payload, it is refused while that payload is read, with `BinaryIntegrityException`.
  Nothing is wrong with the reader's configuration there. The message failed the policy, exactly as a
  payload carrying `Encryption = None` does (§21.1) — substituting a cipher that cannot authenticate
  the header is the same downgrade as substituting no cipher at all. The diagnostic names the
  algorithm the payload named, its custom name included.

The second check belongs to the read rather than to registration, because the instance that decrypts
a payload is the one the registered factory produces at that resolution (§4.1). A build-time check
could not run the factory without making registration side-effecting, and could not bind the
instances the same factory returns later.

## 13.2 Key ownership

Key material is modelled by `SecretKey`, which always holds its own copy, and by `IKeyProvider`,
which hands out owned copies.

- The serializer never mutates or zeroes memory owned by the caller.
- `Resolve` receives the header-selected key id; a mismatch with a provider's configured id is
  `BinaryEncryptionKeyException`.
- Each resolved key is disposed by the code that requested it, at the end of that phase.
- Disposing a provider clears only its own copy; the caller's array is untouched.
- Using a disposed provider throws `ObjectDisposedException`.
- Temporary cryptographic buffers owned by the serializer are cleared when their lifetime ends.
- Key material the configured algorithm cannot use — a wrong length, or none at all — is
  `BinaryEncryptionKeyException` (§8.7), raised where the algorithm is used rather than where the key
  was supplied: a key size belongs to the algorithm, so no entry point validates it on the way in.

---

# 14. Contracts and members

## 14.1 Positional/default mode

Eligible members include public fields/properties according to the accessor rules.

Compiler-generated fields and indexers are not members. A **delegate-typed** member is eligible and
is therefore rejected with `BinaryTypeException` naming it: a delegate carries behaviour rather than
data, and silently dropping it would lose state the inclusion rules said was included. Marking it
`[BinaryIgnore]` states that it is not part of the serialized value. The rejection is decided when the
contract is built, so it never depends on whether the member happens to be null. An event is
unaffected, because its backing field is private and was never eligible.

`[BinaryIgnore]` excludes a member.

`[BinaryInclude]` enables otherwise non-public members where positional mode permits it.

`[BinaryOrder(n)]` controls explicit positional order.

`[BinaryKey]` without `[BinaryContract]` is invalid.

Duplicate explicit order values are invalid.

Deterministic fallback ordering is required where explicit ordering is absent.

## 14.2 Keyed contract mode

With `[BinaryContract]`, every eligible member must have exactly one explicit schema decision:

```text
[BinaryKey(n)]
or
[BinaryIgnore]
```

The following are invalid in contract mode:

```text
[BinaryInclude]
[BinaryOrder]
```

Duplicate keys are invalid.

Keys are ordered numerically and encoded with 7-bit variable-length integers.

Unknown keyed fields are skipped according to their declared payload length and do not invoke a formatter for an unavailable/unknown member type.

Keyed mode is independent of the wire format version: the encoding lives in the payload, so it
applies under V0 and V1 alike. It does require a seekable payload stream, because each field's length
is patched after the field is written. V1 buffers the payload, so the requirement never reaches the
caller; under V0 it falls on the caller's destination stream, and one that cannot seek is
`NotSupportedException` (§10.2, §8.10).

---

# 15. Polymorphism

Polymorphism is declared using `[BinaryUnion(tag, typeof(DerivedType))]`.

Required invariants:

- tag fits the supported byte range;
- tags are unique;
- derived type is assignable to the declared base/interface;
- runtime types must be registered;
- unknown discriminators are rejected;
- implicit positional polymorphism must never silently reinterpret a derived layout as a base layout.

A runtime type mismatch that cannot be resolved by the declared polymorphic map is `BinaryTypeException`.

This is enforced on the **write** path: when a declared type has no `[BinaryUnion]` map and the value's
runtime type differs from it, writing fails. Nothing that used to round-trip is lost — without a map
the reader always instantiated the declared type, so such a payload was already unreadable; the
failure simply moved from silent corruption at read time to a clear exception at write time. The
degenerate case is a value written through `object`.

Only registered tags appear on the wire. Type names never do, so a payload cannot name a type to
construct.

---

# 16. References and cycles

`PreserveReferences` changes wire interpretation through explicit reference markers/IDs.

Rules:

- identity covers every structural **reference** type: member-encoded objects, arrays, collections,
  dictionaries and other containers. Value types are never framed, because a boxed struct cannot be
  shared; scalars, including strings, are not framed either;
- first occurrence of a tracked object establishes identity;
- later references point to the existing ID;
- invalid marker values are `BinaryFormatException`;
- unknown reference IDs are rejected deterministically;
- cycles without permitted reference preservation are rejected as graph/type errors;
- the reader and writer use reference identity, not overridden `Equals`.

## 16.1 Registration order

A container that exists before its children are read — a member-encoded object, a mutable collection
or dictionary — is registered **before** them, so a cycle through it closes.

A container that cannot exist until its children are known — an array, an immutable or frozen
collection, a tuple — is registered after completion. A reference that resolves to such an object
while it is still being built is rejected with a deterministic `BinaryFormatException`, never
silently resolved to a half-built instance.

## 16.2 Reference scopes

Reference ids are unique across the payload, but they are **visible only along the ancestor chain**.
Entering a keyed field opens a scope; leaving it closes one.

Consequences:

- a back reference is never emitted between two sibling keyed fields, so a reader that skips an
  unknown field can never meet a dangling reference. `PreserveReferences` and schema evolution are
  therefore compatible;
- a cycle back to an ancestor still resolves, because ancestors stay visible;
- an object shared between two sibling keyed fields is written twice and read as two instances. This
  is the documented cost of skip tolerance.

Current contract deliberately distinguishes **semantic reference preservation** from ordinary value equality.

---

# 17. Arrays and safe materialization

Attacker-controlled counts must be validated before any allocation driven by that count. This is
structural: a count exists only as an `ElementCount`, and the sole way to create one validates it
against its limit and charges the element budget.

Every declared byte length is additionally compared with the bytes that can still arrive before it is
used to allocate — the read source reports its remaining bytes, and a source that cannot satisfy a
declaration is rejected first. Exceeding a configured ceiling is `BinaryLimitException`; exceeding
what the payload physically contains is `BinaryFormatException`.

For deserialization, variable-size arrays are built incrementally through a bounded-capacity growth path so a declared count alone does not force immediate maximum-size array allocation.

On the write path, a sequence with no O(1) count is materialized lazily and abandoned as soon as it
crosses its limit, so an oversized or infinite `IEnumerable<T>` is rejected instead of enumerated.

Multidimensional arrays require:

- every dimension non-negative;
- every dimension within `MaxArrayLength` on its own, independently of the product: a shape such as
  `[0, int.MaxValue]` has no elements at all and yet describes an array the runtime cannot create;
- overflow-safe product calculation;
- product within `MaxArrayLength`;
- zero-dimension behavior explicitly covered.

`ImmutableArray<T>` serialization must obtain its backing array through `ImmutableCollectionsMarshal.AsArray<T>` rather than reflective invocation of an unavailable instance `ToArray` member.

The final writer path validates the resulting element count before encoding it.

---

# 18. Naming and ownership model

```text
SerializationLimits      = immutable configuration
SerializationBudget      = per-operation mutable accounting
PhaseBudget              = per-phase size policy
SerializationOperation   = everything one public call may consume

ValueReader              = read-side checked primitives   (the only byte access)
ValueWriter              = write-side checked primitives  (the only byte access)
ElementCount             = a count that has been validated and charged

GraphReader / GraphWriter = graph traversal, depth, nodes, identity, keyed layout
TypeContract              = members, keys, layout mode for one concrete type
UnionMap                  = tag ↔ type map for one declared type
```

There is exactly one read-side and one write-side primitive surface, and exactly one traversal
owner. A formatter never has to decide which validation helper applies: the primitive it is given
has already applied it.

`Serialization*` names refer to the operation as a whole and are used for both directions;
direction-specific types say `Read` or `Write` in the name.

---

# 19. Format inspection and diagnostics

`BinaryFormatInspector.Peek(Stream)` requires a seekable stream and must restore the original stream position.

Underlying stream I/O failure becomes `BinaryStreamException`.

Returning `null` is reserved for data that is simply not recognized as a supported inspectable format; malformed recognized data is represented by the documented format exception.

`BinaryFormatDumper.DumpHeader` renders the envelope of a payload as text. It is diagnostic tooling:
it reports a failure as output instead of propagating it, which production serializer code must never
do. It catches only `BinarySerializerException`; nothing in `src/` uses a broad catch as generic
exception normalization.

---

# 20. Stream ownership

Serializer-created readers/writers use `leaveOpen: true` where caller-owned stream preservation is promised.

`BinarySerializer` never disposes the caller's stream.

A failed operation may leave the stream position at the point where the failure occurred unless a specific inspection API promises position restoration.

Inspection APIs that promise non-consuming behavior must restore position even on failure.

---

# 21. Semantic clarifications

## 21.1 Encryption capability vs encryption requirement

Configuring an encryption algorithm does **not** mean that every input must be encrypted. A message
whose V1 header says `Encryption = None` may be read by a serializer that also has an encryption
capability configured: the header describes one message, the configuration describes a capability.

Requiring encrypted input is an explicit policy:

```csharp
BinarySerializerOptions.Configure()
    .WithEncryption(new Aes256Gcm(), key)
    .RequireEncryption()
    .Build();
```

With the policy set, an unencrypted payload is `BinaryIntegrityException`, and so is one encrypted
by an algorithm that does not authenticate the header (§13.1) — both are the same downgrade. The
same algorithm configured locally is refused earlier, at configuration time. Without the policy,
authenticated metadata still prevents tampering with an encrypted message, but not substitution of a
plaintext one — which is exactly what the policy exists for.

`RequireChecksum` is the analogous policy for integrity metadata.

Both policies are statements about a payload's *metadata*, so both are confined to a format that has
metadata. A headerless V0 payload can carry neither, which is why `Build()` refuses either policy
together with `WithVersion(0)` or `AllowV0Fallback` (§4.1, §10.2) instead of letting the operation
produce or accept unprotected data. Within V1 the rule is unchanged: with the policy set, an
unencrypted payload is `BinaryIntegrityException`.

## 21.2 Keyed-field count vs total-element budget

`MaxKeyedFields` is a structural per-object field-count limit.

`MaxTotalElements` is the cumulative data-element budget.

Unknown keyed fields do not consume `MaxTotalElements` merely because they are fields: field counts
are schema metadata, element counts are data. The cumulative metadata ceiling is the separately named
`MaxTotalKeyedFields`, which counts every keyed field of the operation, including skipped ones.

## 21.3 Audit findings

The hostile-audit campaign recorded in `audit/Problems.cs` and the architecture audit in
`Architecture-Audit.md` are closed by the layered design described in §2. Each finding is pinned by a
test in `tests/.../Security` and `tests/.../Correctness`:

```text
S01 encryption capability vs policy          → RequireEncryption / RequireChecksum (§21.1)
S02 authenticated V1 header metadata         → header AAD (§13.1)
S03 graph-wide depth coverage                → engine-owned traversal (§2.4, §5.1)
S04 graph-wide container-node accounting     → engine-owned traversal (§5.8)
S05 V0 payload read boundary                 → metered payload on both directions (§7.1)
S06 allocation ordering vs wire budget       → remaining-byte check before allocation (§17)
S07 key-resolver buffer ownership            → SecretKey / IKeyProvider (§13.2)
S08 disposal and ownership                   → SecretKey / IKeyProvider (§13.2)
S09 exact decompression output               → exact-output contract (§12)
S10 trailing typed-payload bytes             → root canonicity (§10.1)
S11 eager IEnumerable materialization        → bounded materialization (§17)
S12 cumulative keyed-field budget            → MaxTotalKeyedFields (§5.9a)
C01 ref-struct value restoration             → ref overload reads the root (§3)
C02 collection reference identity            → identity covers containers (§16)
C03 unknown keyed fields + reference table   → ancestor-visible scopes (§16.2)
C04 implicit positional polymorphism         → write-side rejection (§15)
C05 BinaryKey/BinaryIgnore contradiction     → contract validation (§14.2)
C06 operation-relative write budget          → MeteredWriteStream origin (§7.2)
C07 primitive truncation exceptions          → checked fixed-size reads (§2.3)
A01 populate-in-place on non-member types    → explicit rejection (§3)
A02 ref-struct framing under references      → ref overload reads the root (§3)
A03 object-declared values                   → write-side rejection (§15)
```

Deferred by design, and **not** claimed by this contract: a public formatter contract, an async API,
streaming (non-buffered) payloads, a V2 codec, constant-time checksum comparison, and source
generators in place of expression-tree accessors.

# 22. Wire format

This section is normative and complete: a conforming reader can be written from it alone. All
multi-byte integers are little-endian. "7-bit int" is the LEB128-style encoding used by
`BinaryWriter.Write7BitEncodedInt`: seven bits per byte, high bit set while more bytes follow,
restricted to non-negative `Int32`.

## 22.1 Primitives

| Element | Encoding |
|---|---|
| `bool` | 1 byte: `0` false, `1` true |
| `byte`, `sbyte` | 1 byte |
| `short`, `ushort`, `char` | 2 bytes |
| `int`, `uint`, `float` | 4 bytes |
| `long`, `ulong`, `double` | 8 bytes |
| `decimal` | 16 bytes: four `int32` in `decimal.GetBits` order (lo, mid, hi, flags) |
| string | 7-bit int UTF-8 byte length, then the bytes |
| blob | 7-bit int byte length, then the bytes |
| count | `int32` |
| optional string | `bool` present flag, then the string when present |

A count is read as an `int32` and is immediately validated against its limit and charged to the
element budget; a negative count is `BinaryFormatException`.

A boolean admits exactly the two encodings above. A reader rejects any other byte with
`BinaryFormatException` rather than treating it as a second spelling of `true`, so the encoding is
canonical. This is what keeps every header flag unforgeable: authenticated encryption binds the
header's decoded fields (§13.1), so a non-canonical flag byte would otherwise be an edit the tag
does not cover.

A string is canonical for the same reason. Its bytes must be valid UTF-8, and a reader that meets a
sequence which is not rejects it with `BinaryFormatException` instead of substituting U+FFFD. Lenient
decoding would map an unbounded set of byte sequences onto one string — `C3 28`, `E0 80 28` and
`F0 80 80 28` all become `�(` — and since the tag is computed over the decoded field, every one
of those sequences would carry the same tag. The rule applies to every string read off the wire,
payload and header alike, and costs no valid payload anything: no writer has ever produced a byte
sequence that is not valid UTF-8.

## 22.2 Value framing

Every value is written as:

```text
[null flag]  [reference frame]  [shape payload]
```

- **Null flag** — one `bool`, present only when the declared type can be null: any reference type, or
  `Nullable<T>`. `false` ends the value. A non-nullable value type has no flag.
- **Reference frame** — present only when the payload header says `PreserveReferences` and the
  declared type is a structural **reference** type. One marker byte followed by an `int32` id:
  marker `0` means first occurrence and the shape payload follows; marker `1` means back reference
  and the value ends there. Any other marker is `BinaryFormatException`. Scalars, including strings,
  and all value types are never framed.
- **Shape payload** — per §22.3.

## 22.3 Shapes

| Shape | Encoding |
|---|---|
| scalar | per §22.4 |
| sequence | count, then each element as a framed value |
| map | entry count, then each entry as key value, both framed |
| object, positional | each member as a framed value, in plan order |
| object, keyed | 7-bit int field count, then each field: 7-bit int key, `int32` payload length, payload |
| union | one tag byte, then the member layout of the tagged type |

Member plan order for the positional layout: members carrying `[BinaryOrder]` first, ascending by
order, then the rest in ordinal name order.

Keyed fields are written in ascending key order. A field payload is exactly as long as its declared
length; reading one consumes it exactly, and trailing bytes inside a field are
`BinaryFormatException`. A reader skips a key it does not know by its declared length.

## 22.4 Scalar encodings

| Type | Encoding |
|---|---|
| `bool`, `byte`, `sbyte`, `short`, `ushort`, `char`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal` | §22.1 |
| `string` | string |
| enum | the underlying primitive |
| `Half` | `int16` of `BitConverter.HalfToInt16Bits` |
| `Int128`, `UInt128` | 16 bytes |
| `IntPtr`, `UIntPtr` | `int64` / `uint64` |
| `Rune` | `int32` scalar value; an invalid scalar is `BinaryFormatException` |
| `BigInteger` | blob, as produced by `BigInteger.TryWriteBytes` |
| `DateTime` | `int64` of `ToBinary()`, which carries the kind |
| `DateTimeOffset` | `int64` ticks, then `int64` offset ticks |
| `TimeSpan` | `int64` ticks |
| `DateOnly` | `int32` day number |
| `TimeOnly` | `int64` ticks |
| `TimeZoneInfo` | string of `ToSerializedString()` |
| `Guid` | 16 bytes, `Guid.TryWriteBytes` layout |
| `Uri` | string of `OriginalString` |
| `Version` | string of `ToString()` |
| `StringBuilder` | string |
| `CultureInfo` | string of `Name` |
| `BitArray` | `int32` bit count, then a blob of `ceil(bits / 8)` bytes |
| `Complex` | 2 × `double`: real, imaginary |
| `Vector2`, `Vector3`, `Vector4` | 2 / 3 / 4 × `float` |
| `Quaternion` | 4 × `float`: X, Y, Z, W |
| `Plane` | 4 × `float`: normal X, Y, Z, then D |
| `Matrix3x2` | 6 × `float`: M11, M12, M21, M22, M31, M32 |
| `Matrix4x4` | 16 × `float`, row-major |

## 22.5 Composite encodings

| Type | Encoding |
|---|---|
| `KeyValuePair<K,V>` | key, then value |
| `Tuple<…>`, `ValueTuple<…>` | items in declaration order |
| `Lazy<T>` | the materialized value |
| `ImmutableArray<T>` | `bool` present flag; when true, count then elements. A default instance writes `false`, which is how it stays distinct from empty |
| array, rank > 1 | `int32` rank, then one `int32` per dimension, then elements in row-major order |

## 22.6 V1 envelope

```text
magic            int32   0x52455342
version          int32   1
compression      byte    CompressionAlgorithm
customCompression        optional string, <= 256 UTF-8 bytes
checksum         byte    ChecksumAlgorithm
customChecksum           optional string, <= 256 UTF-8 bytes
encryption       byte    EncryptionAlgorithm
customEncryption         optional string, <= 256 UTF-8 bytes
keyId                    optional string, <= 256 UTF-8 bytes
preserveReferences bool
uncompressedLength int32
compressedLength   int32
onDiskLength       int32
checksumLength     byte
checksum           checksumLength bytes
payload            onDiskLength bytes
```

Phase order when writing: serialize the payload, checksum the raw payload, compress, build the
associated data, encrypt, then write the header followed by the ciphertext. Reading reverses it.

Header invariants, each `BinaryFormatException` unless noted:

- the magic must match, otherwise the stream is not a Viper payload;
- an unknown version is `BinaryFormatNotSupportedException`;
- an undefined algorithm identifier is `BinaryFormatNotSupportedException`;
- every optional header string is at most 256 UTF-8 bytes;
- all three lengths are non-negative and within their phase limits, otherwise `BinaryLimitException`;
- `Compression = None` implies `compressedLength = uncompressedLength`;
- `Encryption = None` implies `onDiskLength = compressedLength`;
- the declared plaintext length never exceeds the ciphertext actually present;
- the payload is consumed exactly: trailing bytes after the root value are an error.

## 22.7 Associated data

When the encryption algorithm authenticates associated data, the following image is bound to the
ciphertext. It is never stored — both sides recompute it — and every field in it is therefore
unforgeable:

```text
version            int32
compression        byte
customCompression  string   (empty when absent)
checksum           byte
customChecksum     string
encryption         byte
customEncryption   string
keyId              string
preserveReferences bool
uncompressedLength int32
compressedLength   int32
checksumLength     byte
checksum           bytes
```

`onDiskLength` is excluded: it is only known after encryption, and it is self-verifying, because a
wrong value either truncates the read or fails the tag.

## 22.8 V0 envelope

No envelope at all: the payload of §22.1–22.5 is written as-is, with no magic number and no leading
or trailing bytes of any kind. A V0 payload is therefore byte-identical to the payload a V1 frame
carries for the same value under the same member layout, positional or keyed, when that frame uses
no compression, checksum, encryption or reference framing — the four things V0 does not have.
Nothing identifies those bytes, so reading them as V0 requires the caller to opt in (§10.2).

Having no header, V0 encodes no reference frames and no compression, checksum or encryption phase.
The keyed object layout of §22.3 is payload-level and appears under V0 unchanged.

Because the payload is not length-delimited by an envelope, a V0 payload may be embedded in a larger
stream: the reader stops when the root value is complete and does not require the source to end
there. Root canonicity (§10.1) is a V1 rule and does not apply. A keyed field's declared length is
still checked against the bytes that can physically arrive before anything is allocated (§17), but
for an embedded payload those bytes are the remainder of the containing stream rather than of a
declared payload, so the check is weaker under V0 than under V1. The field window (§7.3) bounds what
the field's decoder may actually consume in both cases.

---

# 23. Supported types

A type is supported when a formatter claims it, or when it is member-encoded. Anything else is
`BinaryTypeException` at the first attempt to use it.

| Family | Types |
|---|---|
| Primitives | `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `char`, `string`, enums, `Half`, `Int128`, `UInt128`, `IntPtr`, `UIntPtr`, `Rune`, `BigInteger` |
| Time | `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, `TimeZoneInfo` |
| Numerics | `Complex`, `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Plane`, `Matrix3x2`, `Matrix4x4` |
| System | `Guid`, `Uri`, `Version`, `StringBuilder`, `CultureInfo`, `BitArray` |
| Composite | `KeyValuePair<,>`, `Tuple<…>`, `ValueTuple<…>`, `Lazy<>`, `ImmutableArray<>`, arrays of rank > 1 |
| Arrays and memory | `T[]`, `Memory<>`, `ReadOnlyMemory<>`, `ArraySegment<>`, `ReadOnlySequence<>` |
| Collections | `List<>`, `IList<>`, `ICollection<>`, `IEnumerable<>`, `IReadOnlyList<>`, `IReadOnlyCollection<>`, `HashSet<>`, `ISet<>`, `SortedSet<>`, `LinkedList<>`, `ObservableCollection<>`, `Stack<>`, `Queue<>`, `ReadOnlyCollection<>`, `ReadOnlyObservableCollection<>` |
| Concurrent | `ConcurrentBag<>`, `ConcurrentQueue<>`, `ConcurrentStack<>`, `ConcurrentDictionary<,>` |
| Dictionaries | `Dictionary<,>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, `ReadOnlyDictionary<,>`, `SortedDictionary<,>`, `SortedList<,>`, `PriorityQueue<,>` |
| Immutable | `ImmutableList<>`, `IImmutableList<>`, `ImmutableHashSet<>`, `IImmutableSet<>`, `ImmutableSortedSet<>`, `ImmutableQueue<>`, `IImmutableQueue<>`, `ImmutableStack<>`, `IImmutableStack<>`, `ImmutableDictionary<,>`, `IImmutableDictionary<,>`, `ImmutableSortedDictionary<,>` |
| Frozen | `FrozenSet<>`, `FrozenDictionary<,>` |
| Custom collections | any concrete non-abstract type implementing `ICollection<T>` with a public parameterless constructor and an `Add` method |
| Objects | any other type with a parameterless constructor, encoded member by member |

Notes that belong to the contract:

- **Interfaces** resolve to a concrete implementation on read, which is part of the contract:
  `IList<T>`, `ICollection<T>`, `IEnumerable<T>`, `IReadOnlyList<T>` and `IReadOnlyCollection<T>`
  produce `List<T>`; `ISet<T>` produces `HashSet<T>`; `IDictionary<K,V>` produces `Dictionary<K,V>`;
  `IReadOnlyDictionary<K,V>` produces `ReadOnlyDictionary<K,V>`; the immutable interfaces produce the
  corresponding immutable type. Reference identity across such a member is preserved, the concrete
  type is not.
- **Ordering.** `Stack<>`, `ConcurrentStack<>` and `ImmutableStack<>` round trip so that iteration
  order is preserved; the elements are written bottom-up. Unordered containers — `HashSet<>`,
  `ConcurrentBag<>`, `FrozenSet<>`, dictionaries other than sorted ones — round trip their contents,
  not their iteration order.
- **`PriorityQueue<TElement,TPriority>`** round trips its unordered element/priority pairs, so
  dequeue order is reconstructed from the priorities rather than copied.
- **`Lazy<T>`** travels as its value, so writing one **materializes** it: an unevaluated instance has
  its factory run, and an exception from that factory is the caller's own and propagates unchanged
  rather than being wrapped. Reading produces a `Lazy<T>` that already holds the value, so it never
  fails and never runs a factory, though `IsValueCreated` is `false` until the value is first asked
  for.
- **Delegates** are rejected with `BinaryTypeException` as a root value, a member, or an element. Use
  `[BinaryIgnore]` on the member that holds one.
- **Types without a parameterless constructor**, including interfaces and abstract classes without a
  `[BinaryUnion]` map, are `BinaryTypeException` on read.
- **Memory-like values** — `Memory<T>`, `ReadOnlyMemory<T>`, `ArraySegment<T>` and
  `ReadOnlySequence<T>` — travel as their elements alone (§22.3), so their backing storage is not
  part of the value. A read builds a fresh array and wraps the whole of it: an `ArraySegment<T>`
  comes back at offset zero over an array exactly as long as the segment, and a multi-segment
  `ReadOnlySequence<T>` comes back as a single segment. A default `ArraySegment<T>`, which has no
  backing array at all, is written as an empty segment.
- **`ImmutableArray<T>`** distinguishes default from empty; every other container does not.

# 24. Release checklist

A box is checked only when source and a test prove it.

## Contract and API

- [x] Public `BinarySerializer` overloads match this contract.
- [x] Default/options construction paths are stable; `Configure()...Build()` is the only path.
- [x] Stream ownership behavior is verified.
- [x] `FromHeader` / `FromStream` semantics are verified.
- [x] Populate-in-place rejects non-member-encoded types.
- [x] No hidden required API exists outside this document (§3 lists the whole surface).
- [x] Every public member carries XML documentation, and it ships with the package.

## Exceptions

- [x] Exception hierarchy matches exactly.
- [x] Limit violations are `BinaryLimitException`.
- [x] Negative wire counts/lengths are `BinaryFormatException`.
- [x] Configuration errors are `BinaryConfigurationException`.
- [x] Unsupported recognized versions/algorithms are `BinaryFormatNotSupportedException`.
- [x] Integrity failures are `BinaryIntegrityException`.
- [x] Key-selection failures are `BinaryEncryptionKeyException`.
- [x] Underlying stream I/O is `BinaryStreamException`.
- [x] CLR/contract/reference semantics use `BinaryTypeException`.
- [x] Raw parser exceptions (`EndOfStreamException`, `ArgumentException`) do not escape, and neither
  does an exception raised by a registered algorithm factory that a payload selected.
- [x] No generic exception normalization exists in production.

## Limits and resources

- [x] All default limit values match this document.
- [x] All limits are validated once, as configuration.
- [x] Budget state is fresh per operation and never shared between calls.
- [x] Element budget is cumulative and monotonic.
- [x] Object-node budget is cumulative and monotonic, and covers containers.
- [x] Keyed-field budget is cumulative and monotonic.
- [x] Depth is scoped, exception-safe, and covers every structural shape.
- [x] Wire/payload/compressed/encrypted boundaries are enforced in both directions.
- [x] The write budget is relative to the operation's starting position.

## Formats

- [x] V0 positional contract remains stable.
- [x] V0 carries the same payload encoding as V1, keyed contracts included.
- [x] V1 header validation is deterministic.
- [x] Header lengths are validated before phase allocation.
- [x] V0/V1 routing is deterministic.
- [x] Inspection preserves stream position.
- [x] A V1 payload is consumed exactly; trailing bytes are rejected.
- [x] Decompression output matches the declared length exactly.

## Security

- [x] Attacker-controlled counts/lengths are validated before allocation.
- [x] Declared lengths are compared with physically available bytes before allocation.
- [x] Unknown keyed payloads are skipped without whole-payload allocation.
- [x] Reference markers/IDs are validated; references are ancestor-scoped.
- [x] The V1 header is authenticated when an AEAD algorithm is used.
- [x] Every field the tag covers has one encoding only: a boolean admits two bytes and a string
  admits valid UTF-8, so no field can be rewritten into a second spelling of itself.
- [x] `RequireEncryption` / `RequireChecksum` reject protection downgrades, and are refused at
  configuration time against a format that cannot carry protection.
- [x] Temporary crypto buffers are cleared.
- [x] Caller-owned key buffers are never destroyed by serializer-owned cleanup.
- [x] Disposed crypto components cannot continue using invalid internal state.
- [x] No global mutable state can substitute a built-in algorithm.
- [x] A hostile deeply nested payload fails as a limit violation, not a stack overflow.

## Architecture invariants

- [x] No type below the pipeline references `SerializationLimits`.
- [x] Payload bytes are reachable only through `ValueReader`/`ValueWriter`.
- [x] A loop bound over wire data exists only as a validated `ElementCount`.
- [x] Recursion, depth, node and identity accounting live only in the payload engine.
- [x] No public contract participates in enforcing a limit.

## Quality gate

- [x] Audit findings S01–S12, C01–C07 and A01–A03 are each pinned by a test.
- [x] Round-trip corpus covers every supported type family in V0 and V1.
- [x] The byte-level wire format of §22 is pinned by tests.
- [x] `QA-Plan.md` mandatory cases pass — M0 through M8 are closed and every checkpoint in the plan is proven.
- [x] Performance is deliberately outside this gate. `Benchmark-Plan.md` is a post-release baseline,
  captured against the `v1.0.0` tag rather than before it, and re-run per v1.x release; until its
  cells exist, the project states nothing about its own performance.
- [x] Release artifact includes reproducible environment/version metadata — both packages that
  carry code build deterministically, publish a `.snupkg` of their symbols, and record the
  repository and the exact commit through Source Link, so a published package can be traced
  back to the source it was built from and stepped into.
