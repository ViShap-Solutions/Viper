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
Public API            BinarySerializer · PooledPayload · attributes · exceptions
                      SerializationLimits · I*Algorithm · IKeyProvider · SecretKey
                      write: IBufferWriter<byte> · byte[] · PooledPayload · Stream · PipeWriter
                      read:  ReadOnlySpan<byte> · ReadOnlySequence<byte> · Stream · PipeReader
      │ creates exactly one OperationState per public call, passed by reference
OperationState            Limits snapshot · Budget · PhaseBudget · Keys · policies · the graph traversal
      │
FormatPipeline (V0|V1)    framing · phase order · header + AAD · phase sizes
      │  a stream or pipe buffered exactly as far as the frame extends · phases as transforms
      │  over pooled buffers · the whole frame built in PayloadBuffer, then copied out once
Engine                    typed codecs, one per declared type: null · references · depth · graph nodes ·
      │                   cycles · keyed framing · every loop over wire data
      │  WireReader / WireWriter — ref structs over memory, the only access to payload bytes
Formatters                IScalarFormatter<T> · sequence, map and array shapes · typed composites
Type contracts            TypeContract<T> — member order, access, construction; ReflectedContract<T>
      │                   in v1.0, reached only through MemberWriter / MemberReader
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

`OperationState` is internal state created **exactly once per public call** by
`BinarySerializer` and passed down **by reference** (INV-1). It is a `struct`: a copy would account for
the call twice, so every member below the public edge takes it by `ref`, and an asynchronous method,
which cannot take a reference, takes the one copy it then owns. `WireReader` and `WireWriter` hold a
reference to it, so every count, depth scope and node charged through them lands in the one budget of
the call. No layer below constructs limits, a budget or a key provider of its own.

It carries:

- `Limits` — the validated configuration snapshot;
- `Budget` — cumulative `TotalElements`, `ObjectGraphNodes`, `KeyedFields` and current `Depth`;
- `Phases` — payload/compressed/encrypted size policy;
- `Keys` — the key provider, if any;
- policies — `PreserveReferences`, `RequireEncryption`, `RequireChecksum`;
- `Graph` — the traversal of the payload being read or written: its reference framing, its pooled
  reference table and the ancestor stack. The pipeline opens it for one payload and closes it after,
  returning every pooled part cleared.

A budget is never shared between independent public calls. When a V1 header declares a different
reference mode than the local configuration, the pipeline opens the payload's traversal with the
header's mode on the same state, so accounting stays cumulative for the whole call.

`SerializationLimits` answers **"what is allowed?"**.
`SerializationBudget` answers **"what has this operation already consumed?"**.

## 2.3 The byte boundary

`WireReader` and `WireWriter` are the only types that touch payload bytes. Both are `ref struct`s
over memory, never over a stream: `WireReader` reads a span or a `ReadOnlySequence<byte>` and always
knows exactly how many bytes remain; `WireWriter` writes into the serializer's own `PayloadBuffer`.
They expose checked primitives: fixed-size reads that fail with `BinaryFormatException` instead of a
framework exception, length-prefixed strings and blobs bounded by their limits, every declared length
compared with the bytes that remain before it drives an allocation, and counts that can only be
obtained as a validated `ElementCount`.

There is no raw escape hatch and no stream under the engine. A scalar formatter is handed a
`WireReader`/`WireWriter` by reference and nothing else, so "read a length and allocate it" is not
expressible, and a reader or writer cannot outlive the call it was handed to. A type contract is
handed less: a `MemberWriter` or a `MemberReader`, which expose no bytes, no counts and no position —
only one member's value per call (INV-2).

## 2.4 The traversal boundary

The engine's **codecs** are the only recursion over an object graph. There is one per declared type,
built once and found through `FormatterCache<T>` — a static field read — and together they own:

- the null flag and the reference frame;
- depth accounting and unwinding;
- object-graph node accounting;
- reference identity and its scopes;
- cycle detection;
- the union tag;
- the keyed-contract layout: the field count, keys and field lengths, the field window, the loop over
  the fields on the wire, skipping unknown keys and requiring a field to be read exactly;
- the element loop of every sequence, map and array.

A formatter describes a **shape** (INV-5). A scalar formatter encodes one value through the checked
primitives. A sequence, map or array shape counts, enumerates, builds and completes; it receives no
count read from the wire and no primitive. A composite formatter reads and writes its fixed children
through a surface that offers no raw integer, and the elements behind an array shape are read by the
engine's loop. No formatter owns a loop over attacker-controlled data, so none can omit an
accounting step.

A member-encoded type is described by its **type contract**, `TypeContract<T>`: member order, member
access, instance creation and the response to a known key, and nothing else. The engine checks every
call a contract makes against the contract's own description — the member each call names, its type,
its key, and the number of calls; under a keyed layout, that an accepted field was read exactly once
and a declined one not at all. A mismatch is `BinaryTypeException` naming the type and the member,
never a distorted wire. The engine creates an instance and registers its identity **before** its
members are read, so a cycle back to it resolves; populate-in-place supplies the instance instead.

**Boxing happens only in a polymorphic slot** (INV-17): a value whose runtime type differs from its
declared one — a `[BinaryUnion]` arm, an interface, `object` — is written and read through the
contract of its runtime type, and that is the one place a value type is boxed. Every other value
travels as its own type from the caller to the wire and back, enums included.

**The engine never awaits** (INV-16). Waiting for bytes happens only at the frame edge, in the
pipeline's source readers: an asynchronous read awaits a whole frame and then decodes it
synchronously, and an asynchronous write builds the frame and then awaits the output (§3.5). No
method below the pipeline — in the engine or the formatters — is asynchronous, so a traversal never
holds a budget, a depth scope or a reference table across a suspension.

## 2.5 Phase-specific components

`PhaseBudget` inside the pipeline is the single place that checks payload, compressed and encrypted
sizes. `CompressionService`, `ChecksumService` and `EncryptionService` are internal and are called
**inside** the barrier, never instead of it; the one ceiling that reaches a service is the size of the
writer compression fills, which refuses space past `MaxCompressedBytes`. The services also hold each
algorithm to what it states — its checksum size, its ciphertext length, its key size, and output of
exactly the declared length (§12, §13).

Public algorithm contracts (`ICompressionAlgorithm`, `IChecksumAlgorithm`, `IEncryptionAlgorithm`)
contain no serializer policy, so an external implementation cannot weaken a limit.

Each phase reads one pooled buffer and writes the next; the input of a phase is cleared and returned
to its pool as soon as its output exists. A V1 payload with no phase goes out as the engine wrote it.
Under any phase the payload is made contiguous once, because each phase works on a single span.
Encryption is the last phase and the one exception: its output is not a pooled buffer but the
destination itself (§13).

## 2.6 Atomic writes

A data or graph error leaves no byte in the destination (INV-15). A pipeline builds the whole frame
— header and body — in the serializer's own pooled buffers, checks it against `MaxWireBytes`, and
only then copies it to the destination. A type error, a limit breach or an algorithm failure
therefore raises before the destination is touched, whatever the destination is: a stream, seekable
or not, or a buffer writer. Only a failure of the destination itself, while the finished bytes are
being copied, can leave part of a frame behind (§20). Encryption starts only after the whole payload
is in the serializer's buffer. An encrypted frame is sized, checked against `MaxWireBytes` and given
its key before the destination is touched, and its ciphertext is then produced in the destination
itself; a buffer writer is advanced only once the whole frame is in it, so a cipher that fails leaves
nothing committed there either (§13).

---

# 3. Public API surface

This is the whole public surface. Anything not listed is internal, and adding to this list is an API
change that belongs in a release note.

**`ViShap.Viper`** — `BinarySerializer`, `BinarySerializerOptions`, `BinarySerializerOptionsBuilder`,
`PooledPayload`, and the attributes `[BinaryContract]`, `[BinaryKey]`, `[BinaryIgnore]`,
`[BinaryInclude]`, `[BinaryOrder]`, `[BinaryUnion]`.

**`ViShap.Viper.Security`** — `SerializationLimits`.

**`ViShap.Viper.Compression`** — `CompressionAlgorithm`, `ICompressionAlgorithm`, `NoCompression`,
`DeflateCompression`, `BrotliCompression`.

**`ViShap.Viper.Checksum`** — `ChecksumAlgorithm`, `IChecksumAlgorithm`, `NoChecksum`,
`Crc32Checksum`, `XxHash3Checksum`, `XxHash128Checksum`.

**`ViShap.Viper.Crypto`** — `EncryptionAlgorithm`, `IEncryptionAlgorithm`, `NoEncryption`,
`Aes256GcmEncryption`, `ChaCha20Poly1305Encryption`, `SecretKey`, `IKeyProvider`,
`StaticKeyProvider`, `DelegateKeyProvider`, `HkdfKeyProvider`.

Every built-in algorithm carries its family as a suffix — `…Compression`, `…Checksum`,
`…Encryption` — so that no public type shares its simple name with a type of the .NET libraries the
packages build on (`System.IO.Hashing.Crc32`, `System.Security.Cryptography.ChaCha20Poly1305`, …): a
consumer who imports both namespaces never meets an ambiguous name. The algorithm interfaces declare
no default members; a member added after the release comes with a default implementation, which is
the only additive path.

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

The public `BinarySerializer` surface is exactly:

```csharp
BinarySerializer(BinarySerializerOptions? options = null);

// write
void          Serialize<T>(IBufferWriter<byte> destination, T value);
byte[]        Serialize<T>(T value);
PooledPayload SerializePooled<T>(T value);
void          Serialize<T>(Stream destination, T value);
ValueTask     SerializeAsync<T>(Stream destination, T value, CancellationToken cancellationToken = default);
ValueTask     SerializeAsync<T>(PipeWriter destination, T value, CancellationToken cancellationToken = default);

// read
T?            Deserialize<T>(ReadOnlySpan<byte> source);
T?            Deserialize<T>(ReadOnlySpan<byte> source, out int bytesConsumed);
T?            Deserialize<T>(ReadOnlySequence<byte> source);
T?            Deserialize<T>(ReadOnlySequence<byte> source, out SequencePosition consumed);
T?            Deserialize<T>(Stream source);
ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken cancellationToken = default);
ValueTask<T?> DeserializeAsync<T>(PipeReader source, CancellationToken cancellationToken = default);
IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(Stream source, CancellationToken cancellationToken = default);
IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(PipeReader source, CancellationToken cancellationToken = default);

// populate an existing instance
void          Populate<T>(ReadOnlySpan<byte> source, T target) where T : class;
void          Populate<T>(ReadOnlySpan<byte> source, T target, out int bytesConsumed) where T : class;
void          Populate<T>(ReadOnlySequence<byte> source, T target) where T : class;
void          Populate<T>(ReadOnlySequence<byte> source, T target, out SequencePosition consumed) where T : class;
void          Populate<T>(Stream source, T target) where T : class;
ValueTask     PopulateAsync<T>(Stream source, T target, CancellationToken cancellationToken = default) where T : class;
ValueTask     PopulateAsync<T>(PipeReader source, T target, CancellationToken cancellationToken = default) where T : class;
```

`Api/PublicSurfaceTests` compares this list with the compiled assembly member by member, and the
builder's list of §4.1 likewise.

Every write builds the whole frame in the serializer's own pooled buffers before the first byte
reaches the destination (§2.6), so no destination is asked to seek. A buffer writer is advanced but
not flushed; a stream is flushed; a pipe is flushed and not completed. The frame is copied into a
buffer writer in as many spans as it hands out, shorter ones included; a writer that hands out an
empty span, which the `IBufferWriter<T>` contract does not allow, is `BinaryStreamException` rather
than a copy that never ends (§8.8). Caller-provided streams and
pipes are never disposed or completed, and a stream is never rewound.

Rules for every read and populate entry point:

- **An empty input is not a payload.** No wire version encodes a value in zero bytes, not even a
  null root, which costs one byte. An empty span, an empty sequence, a stream that ends before its
  first byte and a pipe that completes empty are `BinaryFormatException`, and a populate target is
  left untouched. A zero-length input is never silently read as `default(T)`.
- **There is no `byte[]` read overload.** An array converts to `ReadOnlySpan<byte>`, so
  `Deserialize<T>(bytes)` and `Populate(bytes, target)` compile against the span forms. A `null`
  array becomes an empty span and is rejected as an empty payload — `BinaryFormatException`, not
  `ArgumentNullException`.
- **Without a bytes-consumed form, a span or a sequence is exactly one frame**: one V1 frame, or one
  V0 payload up to the end of its root value. Bytes after it are `BinaryFormatException` (§3.4).
- **A stream or a pipe is read exactly as far as one frame extends** (§20): a V1 frame declares its
  length, so it is read without seeking and nothing past it is taken. A V0 payload declares none,
  so a synchronous read of a stream reads ahead and moves back, which needs a seekable stream (§10.2).
- **Asynchronous reads accept V1 only** (§3.5).

## 3.2 `PooledPayload`

`SerializePooled` returns a `PooledPayload`, a sealed class implementing `IDisposable` that owns an
array rented from `ArrayPool<byte>.Shared` holding exactly the bytes `Serialize<T>(T)` would return.
`Memory` (`ReadOnlyMemory<byte>`) and `Span` (`ReadOnlySpan<byte>`) are valid until `Dispose`.
`Dispose` clears the bytes and returns the array to the pool; it is idempotent, and safe to call
concurrently, so the array is returned exactly once. Reading `Memory` or `Span` after `Dispose` is
`ObjectDisposedException`.

The owner is a class, not a struct: a copy of a struct would be a second owner, a double `Dispose`
would return the array to the pool twice, and two later operations would share it.

```csharp
using PooledPayload payload = serializer.SerializePooled(order);
await socket.SendAsync(payload.Memory, cancellationToken);
```

## 3.3 Populate-in-place

`Populate` reads a payload into a class the caller already holds.

- It is defined only for **member-encoded** classes. A type claimed by a dedicated formatter
  (collection, dictionary, array, string, …) is `BinaryTypeException`; its payload is not
  reinterpreted through a member plan. A struct is read with `value = serializer.Deserialize<T>(…)`,
  which is observationally the same, so there is no struct form.
- Only the **root** is populated: its members are overwritten with the payload's values, and every
  object below it is created afresh. Under a keyed contract (§14.2) a member whose key the payload
  does not carry keeps its current value.
- A **null root** or a root that is a **back reference** is `BinaryFormatException`: neither is an
  instance's member layout.
- A `[BinaryUnion]` payload whose runtime type differs from the target's is `BinaryTypeException`; an
  instance cannot be reused for another type. Without a union map the target's type must be the
  declared type.
- An empty input leaves the target untouched (§3.1). A null target is `ArgumentNullException`.

## 3.4 Bytes consumed

Each source reports where a frame ended in its native position type: `out int bytesConsumed` for a
span, `out SequencePosition consumed` for a sequence, which goes straight to
`PipeReader.AdvanceTo`. With the form, reading stops at the end of the V1 frame or of the V0 root
and reports where, so frames and payloads placed back to back are read one after another; bytes
after the frame are not read. Without it, trailing bytes are `BinaryFormatException`. The rule is the
same for V0 and V1.

## 3.5 Asynchrony

**The engine never awaits** (§2.4). An asynchronous read awaits one whole V1 frame — the header gives
its length — and then decodes it synchronously; an asynchronous write builds the frame synchronously
in the serializer's buffer and awaits only the output. The asynchronous methods use
`PoolingAsyncValueTaskMethodBuilder`.

```text
cancellation token   observed while bytes are awaited; decoding a frame already in memory is not
                     interrupted — it is bounded by the limits
PipeReader           exactly the frame is consumed; on cancellation or failure nothing is consumed:
                     AdvanceTo(frame start, examined end)
Stream (read)        exactly the frame is taken; bytes taken are not given back, so after a cancellation
                     or a failure the position is undefined and the stream is unusable for further
                     framing
write                a cancellation before the output starts leaves nothing (the buffer is atomic); one
                     during WriteAsync, FlushAsync or the pipe's flush may leave part of a frame, or a
                     frame written but not flushed — a property of the destination
OperationCanceledException   standard .NET, outside the taxonomy of §8
```

A pending pipe read cancelled with `CancelPendingRead` is reported as `OperationCanceledException` as
well, and consumes nothing.

**V0 is read synchronously, from the caller's frame.** An asynchronous read or populate that meets V0
(`AllowV0Fallback` on, no magic) is `NotSupportedException` naming the rule. Asynchronous V0 *writes*
are allowed.

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

**Reading a stream of frames.** `DeserializeAsyncEnumerable<T>` reads V1 frames until the source
ends. Each frame is its own operation — its own limits and budgets — so a stream of frames is
unbounded while every frame stays bounded; limits apply per frame, never per connection. The source
ending exactly between frames completes the enumeration; ending inside a frame is
`BinaryFormatException`, raised once the complete frames before it have been yielded. V0 is
`NotSupportedException`, for the reason above. Cancellation behaves as for `DeserializeAsync`; a
`PipeReader` does not consume a frame it has started. A single message above 2 GiB, or a single value
that never ends, is out of scope by design: messages are materialised object graphs, and large or
endless data is sent as many frames.

```csharp
await foreach (Order? order in serializer.DeserializeAsyncEnumerable<Order>(networkStream, cancellationToken))
    Handle(order);
```

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
WithKeys(ReadOnlySpan<byte> key, string? keyId)
WithKeys(Func<string?, byte[]?> keyResolver)
WithKeys(IKeyProvider)
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

**Keys for reading.** Reading V1 takes its algorithms from the header, so a reader needs no
algorithm of its own — only a key. `WithKeys` supplies one — a fixed key, a resolver by key id, or a
provider — without choosing encryption for writing. `WithEncryption` still supplies keys too, for
reading as well as writing, so keys have one place: supplying them through both is rejected at
`Build()`.

```csharp
var reader = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithKeys(keyId => vault.Get(keyId))
    .Build());
Order? order = reader.Deserialize<Order>(stream);   // any V1 frame, encrypted or not
```

`Build()` rejects contradictory configuration with `BinaryConfigurationException`:

- a write version that is not a supported wire format;
- keys supplied both through `WithEncryption` and through `WithKeys`;
- `RequireEncryption` without an encryption algorithm;
- `RequireEncryption` with an algorithm that does not authenticate associated data;
- `RequireChecksum` without a checksum algorithm;
- `RequireEncryption` or `RequireChecksum` together with `WithVersion(0)`;
- `RequireEncryption` or `RequireChecksum` together with `AllowV0Fallback`;
- a fixed key given to `WithEncryption` whose length is not the algorithm's `KeySizeInBytes` (§13.2).
  The check applies only to a fixed key: a key from a resolver or a provider exists only once it is
  resolved, and is checked then.

`Build()` also refuses options that encrypt with `ChaCha20Poly1305Encryption` on a platform whose
cryptography library does not provide it, with `BinaryFormatNotSupportedException` naming the
algorithm (§13).

The two version rules close both directions of the same contradiction: a headerless payload carries no
protection, so a policy demanding protection could be satisfied neither when writing one nor when
reading one (§10.2, §21.1). A configured algorithm without a policy is a capability, not a demand,
and stays legal under version 0 — it simply does not apply to what version 0 writes.

Missing key material is **not** among them. Every `WithEncryption` overload takes the key source in
the same call as the algorithm and rejects a null one with `ArgumentNullException`, so a builder
holding an algorithm without key material is not a state a caller can produce. `Build()` keeps the
guard as an invariant over the options it constructs, but no configuration reaches it.

A payload may well name an encryption algorithm the reader was given no key for — the header
chooses the algorithm, not the reader. That is a reader missing a key, not a contradictory
configuration: it is diagnosed when the payload is read, as `BinaryEncryptionKeyException` (§8.7),
alongside a key provider that resolves to nothing and a key that does not match the header's
`keyId`. `KeyId` is metadata used to select the key; it is not key material and is never treated as a
secret. A key resolver receives the header's `keyId`.

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

## 4.3 Reading a header without reading the payload

`BinaryFormatInspector.Peek` (§19) reads a V1 header's metadata — algorithms, custom names, key id —
without decoding the payload, for diagnostics and for choosing a key before committing to a read.
Options are never derived from a header: reading V1 already takes its algorithms from it, and keys
come from `WithKeys` (§4.1).

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
| `MaxDecompressionRatio` | `10_000` |
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

It bounds every element type alike, `byte[]` included. An array of bytes is an array, not a blob, so
it is never measured against `MaxByteBlobBytes` (§5.6), and its elements are charged to
`MaxTotalElements` one per byte (§5.7). The same holds for the memory-like values of §23 —
`Memory<byte>`, `ReadOnlyMemory<byte>`, `ArraySegment<byte>` and `ReadOnlySequence<byte>` — which
travel as their elements.

## 5.3 `MaxCollectionLength`

Limits per-collection element count.

## 5.4 `MaxDictionaryEntries`

Limits per-dictionary entry count.

## 5.5 `MaxStringBytes`

Limits UTF-8 encoded byte length, not character count.

## 5.6 `MaxByteBlobBytes`

Limits one value written in the **blob** wire form: a single declared byte length followed by that
many raw bytes (§22.1).

The set of values encoded that way is closed and small — the `BigInteger` body and the `BitArray`
data (§22.4), and nothing else. It also bounds a `BitArray` bit count, at `MaxByteBlobBytes × 8`.

It does **not** bound `byte[]`, which is an ordinary array under §5.2. The element type never changes
the wire form of a container, so no array of any kind reaches this limit. See §21.4.

## 5.7 `MaxTotalElements`

Cumulative per-operation element budget, charged one per element of every array, collection and
dictionary the operation reads or writes. It must never decrease during one operation.

It is deliberately distinct from keyed-field metadata counts.

One element is one charge however many bytes it encodes to, so this budget measures a payload's
**structural size**, not its byte volume; byte volume is bounded by the phase limits of §5.10. A
record therefore spends one element whatever its members weigh, while a `byte[]` of a million bytes
spends a million. Carrying bulk binary data above the defaults consequently means raising
`MaxArrayLength` **and** `MaxTotalElements` together: either one alone still refuses the value.

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

`MaxDecompressionRatio` bounds how far a payload may declare that it expands: the declared
uncompressed length may not exceed the declared compressed length by more than this factor. It is the
only phase bound that relates a declared size to the bytes that carry it, and it exists because
compression is the only phase whose output may legitimately exceed its input — and therefore the only
one where the remaining-bytes rule of §17 cannot apply on its own. It is evaluated while the header is
read, before any buffer exists, and only when compression is not `None`; with `None` the two lengths
are already required to be equal.

`MaxEncryptedBytes` bounds the encrypted/on-disk representation.

`MaxWireBytes` bounds the bytes one operation takes from its source and the bytes it emits to its
destination, each counted from where the operation starts. On read, the pipeline never buffers more
than this from the source (§7.1). On write, a finished frame longer than this is refused with
`BinaryLimitException` before any byte is copied out (§7.2).

---

# 6. Resource accounting rules

`SerializationBudget` lives in the operation's `OperationState` and is cumulative within one
operation and monotonic.

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

A charge is per element, never per byte, and the element type does not change it (§5.7).

A declared count decides how memory is taken for it (INV-3): a collection whose count the bytes that
remain could hold — at the element's fewest bytes on the wire — is created at that capacity, and one
whose count they could not hold starts at a capacity of at most 1 024 and grows as elements arrive.
Arrays follow the same rule (§17).

---

# 7. Metering and windowing over buffers

There are two mechanisms: metering and windowing. Neither is a stream: on the read side both are
properties of `WireReader`, which reads memory; on the write side metering is a property of the
serializer's `PayloadBuffer` and of the finished frame, which is checked before it is copied out.

**Metering** — counting what one operation consumes or produces, relative to where it started.

## 7.1 Metering on read

> Caps the bytes this operation reads from a caller-owned stream.

- the pipeline reads the source ahead into memory, from the caller stream's position, and never more
  than the operation's budget: V1 reads its header (at most the largest header the format admits)
  and then exactly the declared on-disk length; V0, which has no header, reads up to the tighter of
  `MaxWireBytes` and `MaxPayloadBytes`, so `MaxPayloadBytes` applies symmetrically to reading and
  writing. A byte array is already in memory and is decoded where it lies, cut to the same budget;
- a phased V1 payload is decrypted and decompressed into pooled buffers, each cleared when the value
  has been read; an unphased one is decoded straight from the bytes read;
- the `WireReader` over those bytes knows exactly how many remain, so a declared length is rejected
  before it drives an allocation;
- a declaration the bytes cannot satisfy is classified by which bound it broke: beyond the budget is
  `BinaryLimitException`, while within the budget but beyond the bytes the source delivered means the
  payload is shorter than it claims, which is `BinaryFormatException`. A declared length the budget
  admits is therefore never reclassified as a limit violation;
- after a successful read the source's position is where the decoded bytes end — the end of a V1
  frame, or the end of a V0 root value;
- underlying `IOException` is wrapped as `BinaryStreamException`;
- the caller's stream is never disposed.

## 7.2 Metering on write

> Caps the bytes this operation produces.

- the payload is written into the serializer's `PayloadBuffer`, which never hands out space past
  its budget — `MaxPayloadBytes`, and under V0, where the payload is the whole frame, the tighter of
  `MaxPayloadBytes` and `MaxWireBytes` — so a graph that would exceed it fails with
  `BinaryLimitException` while it is being written;
- the budget counts only the bytes this operation produces, so appending to a destination that
  already holds data costs the operation nothing;
- a keyed field's length is patched in place in the buffer, over bytes already counted, so a patch
  is never charged twice: the budget follows the high-water mark;
- the finished frame is checked against `MaxWireBytes` before it leaves; a frame longer than the
  budget is `BinaryLimitException`, and the destination receives nothing (§2.6);
- the frame is then copied to the destination once, in order. A destination is never asked to seek,
  to report its position or its length, so any writable stream and any buffer writer will do;
- an `IOException` from a stream destination, while writing or flushing, is wrapped as
  `BinaryStreamException` with the original preserved;
- the caller's stream is never disposed.

**Windowing** — exposing exactly one declared subrange.

## 7.3 The field window

> `WireReader.Slice(length)`: a reader over exactly one declared subrange of the payload.

Used for known keyed-field payloads. If a field declares `N` bytes, its decoder may consume at most
`N` bytes through the window and cannot read into the next field. The window knows exactly how many
bytes remain and classifies an over-read as `BinaryFormatException`, because running past a declared
window means the payload is shorter than it claims.

A window never copies the field payload merely to enforce the boundary, and a known field is decoded
through it while sharing the parent operation's budget and reference state.

Unknown keyed fields are skipped by moving past their declared length, never copied into an
attacker-sized byte array.
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
- invalid configuration combinations, keys supplied both through `WithEncryption` and `WithKeys`
  among them;
- a fixed key of a length the configured algorithm does not take (§13.2);
- unusable configured provider state;
- an algorithm that breaks its own statement (§12, §13): a ciphertext length below the plaintext
  length, an encryption that writes other than the length it stated, a decryption reporting a
  plaintext outside `0…ciphertext.Length`, a destination refused with `ArgumentException`, a checksum
  size outside `1…255`.

Not for null public arguments.

## 8.2 `BinaryFormatException`

Malformed or structurally invalid binary input.

Examples:

- truncated header/payload;
- malformed 7-bit integers;
- negative wire counts/lengths;
- invalid markers;
- inconsistent header lengths;
- malformed keyed payload structure;
- a duplicate key or element, or a null dictionary key, inside a container that admits neither (§23).

When EOF means the declared binary structure is incomplete, raw `EndOfStreamException` must not escape the parser.

A container refusing a value from the payload does so with the framework's own `ArgumentException`.
That refusal is a statement about the payload rather than about the caller's arguments, so the engine
— which owns the element loop — classifies it here and preserves the original as `InnerException`
(§9). No `ArgumentException` reaches the caller under its own name from a payload path.

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
- missing custom registration;
- `ChaCha20Poly1305Encryption` on a platform that does not provide it — when options that encrypt
  with it are built, and when a payload that names it is read (§13).

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
- key provider cannot supply usable material, a resolved key of a length the algorithm does not take
  among it (§13.2);
- `HkdfKeyProvider` asked for the key of a payload that names no key id.

`KeyId` is a selector; the header never supplies secret key bytes.

## 8.8 `BinaryStreamException`

Underlying caller-stream or caller-pipe I/O failure: an `IOException` from a `Stream` read, write,
flush, position or length, from `PipeReader.ReadAsync` — a pipe whose writer completed with one, a
connection reset — or from `PipeWriter.FlushAsync`, a pipe whose reader completed with one.

A caller's `IBufferWriter<byte>` — a `PipeWriter` among them — that hands out an empty span while the
frame is being copied into it is a failed destination as well, and is `BinaryStreamException`; there
is no inner exception, because the writer raised none. Part of the frame may already be in it (§2.6).

The original `IOException` is preserved as `InnerException`. Anything else a stream or a pipe raises —
`NotSupportedException` from a stream that cannot read or write, `InvalidOperationException` from a
completed pipe — keeps its normal BCL semantics and is not wrapped.

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
- `NotSupportedException` — an unsupported capability: a V0 payload read from a stream that cannot
  seek, since it carries no length and is read ahead and moved back (§10.2); and an asynchronous
  read that meets V0, since only V1 declares the length an asynchronous read awaits (§3.5). No read
  requires seekability otherwise;
- `ObjectDisposedException` — a `PooledPayload` read after it was disposed (§3.2);
- `OperationCanceledException` — a cancelled asynchronous call (§3.5). It is standard .NET and
  outside the taxonomy.

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
identically under both. A keyed field's length is written ahead of the field and patched once the
field's size is known; under both formats that happens in the serializer's own buffer, and the
finished bytes are copied to the destination once. A keyed write therefore works with any
destination, seekable or not, and a write that fails leaves nothing in the destination.

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

Writing V0 is selected by `WithVersion(0)` alone. `AllowV0Fallback` governs only whether bytes
without the magic number may be *read* as V0, so the two choices are independent in both directions.

**Where a V0 payload ends when read.** Nothing in the payload declares its length; the reader finds
the end by decoding the root value:

```text
span / sequence       by default exactly one payload; trailing bytes → BinaryFormatException
bytes-consumed forms  stop at the end of the root and report where (§3.4) — for payloads back to back
seekable Stream       read ahead within the budget, then the position is restored to the end of the root
non-seekable Stream   not readable without a length → NotSupportedException
asynchronous read     not supported for V0 → NotSupportedException (§3.5)
```

V0 is read synchronously, from the caller's frame; asynchronous V0 *writes* are allowed.

> V0 carries neither a magic number nor a length: it is a codec for protocols that already frame their
> messages — a length prefix, a message type, a channel. The protocol knows where a message ends, so
> the caller already holds one message's bytes and reads them synchronously. Waiting asynchronously
> is for a reader that does not know where the message ends; with V0 the protocol knows, not Viper.

The example of §3.5 shows both: a V1 frame awaited from a socket, and V0 payloads cut out of a pipe by
the caller's own length prefix and read synchronously.

## 10.3 Routing

Format routing identifies the version from the first bytes the source has delivered: the magic
number and the version are decoded from the buffered frame, so nothing is read twice and no source is
asked to rewind. Only an exact match of the eight bytes identifies a versioned frame; fewer bytes
identify nothing.

If V1 magic/version is recognized, V1 is selected.

Otherwise the bytes are unidentified, and the serializer does not guess: V0 is selected only when the
caller enabled `AllowV0Fallback` and V0 is registered. Without that opt-in, unidentified input is
`BinaryFormatException`. The opt-in is a statement about the source, not about the format — it says
the caller knows that this channel carries headerless payloads.

Unsupported recognized versions produce `BinaryFormatNotSupportedException`.

An `IOException` while the identifying bytes are read is `BinaryStreamException`, raised where the
source is read (§8.8); routing itself touches no source, and nothing blanket-wraps the codec
operation.

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
Compression != None  → UncompressedLength <= CompressedLength × MaxDecompressionRatio
Encryption == None   → OnDiskLength == CompressedLength
```

The expansion rule is a configured limit rather than a property of the format, so breaking it is
`BinaryLimitException` while the other two are `BinaryFormatException`.

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
expected decompressed output ≤ compressed input × MaxDecompressionRatio
```

Phase sizes are checked by the pipeline, not by the algorithm: `ICompressionAlgorithm` implementations
receive no limits and are invoked inside the barrier.

```csharp
public interface ICompressionAlgorithm
{
    CompressionAlgorithm Kind { get; }
    string? CustomName { get; }

    void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination);

    // Writes exactly expectedLength bytes; more, fewer or an unterminated stream is BinaryFormatException.
    void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength);
}
```

One method per direction, and no default members (§3). Both directions write into an
`IBufferWriter<byte>` the serializer supplies: on the way out it refuses space past
`MaxCompressedBytes`, which is `BinaryLimitException`; on the way in it refuses space past the
declared length, which is `BinaryFormatException`. The built-ins are `DeflateCompression` and
`BrotliCompression`; neither goes through a `MemoryStream`.

Decompression produces **exactly** the declared uncompressed length. Producing fewer bytes and
producing more are both rejected, so a payload cannot declare a size that hides part of its own
content.

Malformed compressed data maps to `BinaryFormatException`.

Output buffer/phase-limit failures map to `BinaryLimitException` when the configured security limit is the reason for rejection.

No compression mode still honors configured phase limits.

Compression is the one phase where output can legitimately exceed input, so it is the one place where
a declared size is not backed by bytes that must physically arrive. Two rules restore that backing,
and both are needed:

- **The declared expansion is bounded against the delivered bytes.** `MaxDecompressionRatio` (§5.10)
  relates `UncompressedLength` to `CompressedLength`, and `CompressedLength` bytes are metered and
  physically present before anything is decompressed. The buffer a payload can ask for is therefore
  proportional to the payload it actually delivered, not to the number it wrote in its header.
- **The buffer follows the output, not the declaration.** Every algorithm, built-in or custom,
  decompresses into a writer that starts at 64 KiB and grows to the declared length only once that
  much output exists. A payload that declares a large expansion and then produces nothing therefore
  costs the probe and nothing more. There is no other mode.

The first rule bounds the declaration; the second keeps the allocation behind the output. Together
the allocation is at most `min(MaxPayloadBytes, delivered × MaxDecompressionRatio)`, and never more
than 64 KiB until the payload has genuinely produced that much.

The service holds every algorithm to the declared length whatever the algorithm reports: output that
stops short is `BinaryFormatException` once the algorithm returns, and output that runs past it is
refused by the writer before a byte lands. A framework `InvalidDataException` from an algorithm is
`BinaryFormatException` with the cause preserved.

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

```csharp
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

One method per direction, always with associated data, and no default members (§3). The ciphertext
length is a function of the plaintext length: `Aes256GcmEncryption` and `ChaCha20Poly1305Encryption`
both state `plaintextLength + 28` (a 12-byte nonce and a 16-byte tag). An algorithm that cannot state
its ciphertext length before encrypting — one that adds random padding, say — cannot be plugged in.

The service holds an algorithm to what it states, and every breach is a fault of the algorithm, not
of the data:

```text
GetCiphertextLength   negative or below the plaintext length   → BinaryConfigurationException
Encrypt               returns other than destination.Length,
                      or refuses the destination with ArgumentException → BinaryConfigurationException
Decrypt               returns outside 0…ciphertext.Length,
                      or refuses the destination with ArgumentException → BinaryConfigurationException
KeySizeInBytes        a fixed key given to WithEncryption      → BinaryConfigurationException at Build()
                      a key obtained from a provider           → BinaryEncryptionKeyException when resolved
```

A `CryptographicException` is `BinaryEncryptionException` on the way out and
`BinaryIntegrityException` on the way in, the cause preserved.

Because the length is exact, an encrypted frame is sized before anything is encrypted: the ciphertext
length is checked against `MaxEncryptedBytes`, the key is resolved and checked, the header is written
with its final `OnDiskLength`, and the whole frame is checked against `MaxWireBytes` — all before the
destination is touched. The algorithm then encrypts straight into the destination: into the span an
`IBufferWriter<byte>` hands out for the whole frame, which is advanced once the frame is complete; into
the new array for `Serialize` and `SerializePooled`; into one pooled buffer written to a stream in one
call. A writer that hands out less than the whole frame receives it through a pooled buffer instead.
A cipher that fails therefore leaves nothing committed in any destination (§2.6). The associated data
image is built only when the payload is encrypted, in memory the serializer owns, and is cleared once
the frame is written or dropped; so is the plaintext on the way in and on the way out.

`ChaCha20Poly1305Encryption` comes from the platform's cryptography library, and not every platform
provides it. Where it is missing, `Build()` refuses options that encrypt with it, and a payload that
names it is refused when read, both with `BinaryFormatNotSupportedException` naming the algorithm.

## 13.1 Authenticated metadata

The V1 header is bound to authenticated encryption as associated data. The canonical image covers the
format version, the algorithm kinds and custom names, the key id, `PreserveReferences`,
`UncompressedLength`, `CompressedLength` and the checksum bytes.

`OnDiskLength` is not part of the image; it is self-verifying, since a wrong value either truncates
the read or fails the authentication tag.

Altering any authenticated header byte fails with `BinaryIntegrityException`.

`IEncryptionAlgorithm` has no method without associated data and no default for
`AuthenticatesAssociatedData`: every algorithm states it. The pipeline always builds the image and
always hands it over, whatever the property says, so the property is a declaration about the
algorithm rather than a switch over the pipeline. Reporting `true` is the implementer's undertaking
that the associated data takes part in the authentication tag; the engine cannot verify it. An
algorithm that reports `false` leaves the V1 header as unauthenticated metadata.

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
- Every algorithm states `KeySizeInBytes`. A fixed key given to `WithEncryption` is checked against
  it by `Build()`, with `BinaryConfigurationException`; a key a provider resolves — for writing, or
  for reading a payload whose algorithm the reader learns only from the header — is checked when it
  is resolved, with `BinaryEncryptionKeyException` (§8.7), and a refused key is disposed at once. A
  missing key is `BinaryEncryptionKeyException` too.
- `HkdfKeyProvider` derives the key for an id as HKDF-SHA-256 over its root key, with the UTF-8 bytes
  of the key id as info and an optional salt; the key size is a constructor argument, 32 by default.
  The same id always yields the same key and different ids independent ones. The root key is copied
  when the provider is built and is never exposed — the provider's only members are `Resolve` and
  `Dispose` — and every derived key is an owned `SecretKey`. A payload that names no key id has
  nothing to derive from: `Resolve(null)` is `BinaryEncryptionKeyException`.

---

# 14. Contracts and members

## 14.1 Positional/default mode

The member plan of a type is its type contract, `TypeContract<T>` (§2.4): in v1.0 it is built by
reflection, as `ReflectedContract<T>`, and it is the one description reader and writer share. Its
member order is the total order below; a contract built any other way must produce the same plan
(INV-12).

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

**The plan covers the whole inheritance chain.** Members are collected level by level, from the
concrete type up to `object`, so a member declared on a base class belongs to the plan whatever its
visibility: a non-public base member carrying `[BinaryInclude]` is part of the value exactly as it is
when the base is serialized on its own. Asking only the most derived type — which is what reflection
does by default — drops such a member silently, and silence is not an option the contract offers.

Each declaration is counted once:

- an **override** is one member, taken at its most derived declaration, so the attributes that apply
  are the ones written on the override;
- a member that **hides** another with `new` is a second member. Both travel, and the base
  declaration is written first.

The plan order is therefore a total order — `[BinaryOrder]`, then ordinal name, then the declaring
level with the base before the type that hides it — and never depends on the order in which
reflection happened to return members. Two declarations sharing a name are the only case the third
key decides; everything else is ordered exactly as it was before it existed.

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

Keys are ordered numerically and encoded with 7-bit variable-length integers. On the wire they
appear in strictly ascending order; a payload that repeats a key or lists one out of order is
malformed (§22.3).

`[BinaryContract]` is **inherited**. A type extending a contract type is itself a contract type, and
every member it adds needs its own `[BinaryKey]` or `[BinaryIgnore]`; an unmarked one is
`BinaryTypeException` naming it. The hierarchy shares one key space, so a key the base claims cannot
be reused below it — a duplicate is rejected wherever the two declarations sit. That shared space is
what makes the inheritance useful: a reader holding only the base skips a derived member by its
declared length, exactly as it skips any other key it does not know.

Unknown keyed fields are skipped according to their declared payload length and do not invoke a formatter for an unavailable/unknown member type.

Keyed mode is independent of the wire format version: the encoding lives in the payload, so it
applies under V0 and V1 alike. Each field's length is patched after the field is written, in the
serializer's own buffer, so keyed mode places no requirement on the caller's destination (§10.2).

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

The polymorphic slot is the only place the engine boxes (INV-17): a value whose runtime type is not
its declared type is written and read through the contract of its runtime type, found by that type.
A value type in such a slot is boxed once, and on read its identity is registered and its members
are read into that same box.

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
- an ID is declared **once**: a second first-occurrence under an ID already visible is
  `BinaryFormatException`, not an overwrite. Allowing it would give one graph a second spelling on
  the wire and would silently move the object that earlier references resolve to;
- unknown reference IDs are rejected deterministically;
- a back reference that resolves to an object the declared type cannot hold is
  `BinaryFormatException`;
- cycles without permitted reference preservation are rejected as graph/type errors;
- the reader and writer use reference identity, not overridden `Equals`.

Without `PreserveReferences`, a cycle is found by searching the path from the root to the value being
written: an ancestor stack, never deeper than the depth budget admits, searched by reference for
every structural reference-typed value. A value already on that path is a cycle and is
`BinaryTypeException`, with the same diagnostic at every depth. A value that appears more than once
without being its own ancestor — shared between siblings, or reachable along two paths — is not a
cycle and is simply written again.

The reference tables of one operation, and its ancestor stack, come from pools and go back to them
cleared when the operation ends, so no object of one call is reachable from the next.

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

For deserialization, an array whose count is backed by bytes — the array itself, at its size in
memory, fits in the bytes that remain — is allocated at its final length and read into. Otherwise its
elements are gathered in a pooled buffer as they arrive and one array of the final length is created
at the end, so a declared count alone never forces an allocation in proportion to it. A collection's
initial capacity follows the same rule, at the element's fewest bytes on the wire (§6).

On the write path, a sequence with no O(1) count is materialized lazily and abandoned as soon as it
crosses its limit, so an oversized or infinite `IEnumerable<T>` is rejected instead of enumerated.

Multidimensional arrays require:

- every dimension non-negative;
- every dimension within `MaxArrayLength` on its own, independently of the product: a shape such as
  `[0, int.MaxValue]` has no elements at all and yet describes an array the runtime cannot create;
- overflow-safe product calculation;
- product within `MaxArrayLength`;
- zero-dimension behavior explicitly covered.

`ImmutableArray<T>` is written from its backing array in place and read into an array that becomes
its backing array through `ImmutableCollectionsMarshal.AsImmutableArray<T>`; neither direction copies
it or reaches it by reflection.

The final writer path validates the resulting element count before encoding it.

---

# 18. Naming and ownership model

```text
SerializationLimits      = immutable configuration
SerializationBudget      = per-operation mutable accounting
PhaseBudget              = per-phase size policy
OperationState           = everything one public call may consume: a struct passed by reference

WireReader               = read-side checked primitives over memory   (the only byte access)
WireWriter               = write-side checked primitives into PayloadBuffer (the only byte access)
PayloadBuffer            = the serializer's pooled write buffer: one byte budget, patching in place
EncodedFrame             = one finished frame in pooled buffers, checked against the wire budget,
                           copied to its destination once
RentedBytes              = the pooled output of one pipeline phase, cleared when it is released
ElementCount             = a count that has been validated and charged

Codec<T>                 = the engine's codec for one declared type: framing, traversal, the loop
FormatterCache<T>        = the codec of T, resolved once by FormatterRegistry and held in a static field
GraphState               = the traversal of one payload: reference framing, tables, ancestor stack
IScalarFormatter<T>      = the encoding of one self-contained value
ISequenceShape<,,,>      = count, enumerate, build and complete a sequence; no count, no primitive
IMapShape<,,,,>          = the same for key/value entries
IArrayShape<,>           = a sequence whose elements lie in one array: exposed as a span, wrapped back
ICompositeFormatter<T>   = a fixed child layout, through CompositeReader / CompositeWriter
TypeContract<T>          = member order, access, construction, the response to a known key
ReflectedContract<T>     = the type contract built by reflection, the one v1.0 ships
MemberWriter / Reader    = what a type contract writes and reads its members through
CompositeReader / Writer = what a composite formatter may do: child values, a validated ArrayShape
                           and the elements behind it, read by the engine — and no raw integer
TypeContract             = the description of a TypeContract<T>: members, keys, layout mode,
                           constructibility; what the engine checks every contract call against
UnionMap                 = tag ↔ type map for one declared type
```

There is exactly one read-side and one write-side primitive surface, and exactly one traversal
owner, the engine's codecs. A formatter never has to decide which validation helper applies: the
primitive it is given has already applied it.

**What is cached.** Three things, each built once, on first use, for the life of the process, and
each safe when several threads use a type for the first time at once:

- the codec of each declared type, in the static field of `FormatterCache<T>`. The shapes of the
  generic definitions it is built from are a fixed table, closed once per type;
- the type contract of each member-encoded type, found by type, which is what the polymorphic slot
  needs;
- the union map of each declared type.

Nothing request-local takes part in building an entry, and an entry that fails to build — a delegate
member, a duplicate key, a duplicate tag — is not kept, so every later use reports the same failure.
There is no cache of reflective accessors: a shape calls a collection's own members, and a contract's
accessors are compiled once per member.

`Serialization*` names refer to the operation as a whole and are used for both directions;
direction-specific types say `Read` or `Write` in the name.

---

# 19. Format inspection and diagnostics

`BinaryFormatInspector.Peek` reads the header of a span, a sequence or a stream. Over a span or a
sequence it reads the bytes in place. The stream overload requires a seekable stream and must restore
the original stream position, on success and on failure.

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

Any readable stream is accepted, seekable or not. A V1 frame declares its length, so exactly one
frame is read and nothing past it: the stream is asked for the identifying bytes, then for as much of
the header as the bytes so far say it holds, then for the rest of the frame — never for a byte
beyond it. A V0 payload declares no length, so it is read ahead and a seekable stream is left at the
end of the root; from a stream that cannot seek it is `NotSupportedException` (§10.2). Bytes a
declared length has not yet delivered are buffered as they arrive, so what a stream that cannot tell
its length costs follows what it delivered, never what a header declared.

A successful read leaves the stream positioned where the frame, or the V0 root, ends. After a failed
or cancelled read the position is undefined — anywhere up to the furthest byte read — unless a
specific inspection API promises position restoration.

A write that fails before its frame is complete writes nothing to the stream (§2.6).

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
    .WithEncryption(new Aes256GcmEncryption(), key)
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
C06 operation-relative write budget          → PayloadBuffer budget and frame check (§7.2)
C07 primitive truncation exceptions          → checked fixed-size reads (§2.3)
A01 populate-in-place on non-member types    → explicit rejection (§3)
A02 ref-struct framing under references      → ref overload reads the root (§3)
A03 object-declared values                   → write-side rejection (§15)
```

Deferred by design, and **not** claimed by this contract: a public formatter contract,
streaming (non-buffered) payloads, a V2 codec, constant-time checksum comparison, and source
generators in place of expression-tree accessors.

## 21.4 Array length vs blob length

Every limit in §5 bounds a **wire form**, not a CLR type, and the two most easily confused are the
array length and the blob length.

`byte[]` is an array. It is written as an element count followed by its elements (§22.3), so it is
bounded by `MaxArrayLength` and charged to `MaxTotalElements` per byte, exactly as `int[]` is. So are
`Memory<byte>`, `ReadOnlyMemory<byte>`, `ArraySegment<byte>` and `ReadOnlySequence<byte>`, which
travel as their elements alone (§23).

`MaxByteBlobBytes` bounds the blob form — one declared byte length followed by raw bytes — which is
reached only from the `BigInteger` body and the `BitArray` data (§22.4). No array reaches it, whatever
its element type.

The element type never changes a container's wire form, which is why there is one rule here rather
than a table of exceptions. Its consequence is that the default ceiling on a `byte[]` is
`MaxArrayLength`, not the larger number `MaxByteBlobBytes` names: an application carrying files,
images or compressed blobs raises `MaxArrayLength` and `MaxTotalElements` deliberately, as a policy
decision about untrusted input.

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

A 7-bit int is minimally encoded: when it spans more than one byte, its last byte is not zero. A
reader rejects any longer spelling of the same value — `85 00` or `85 80 00` for 5 — with
`BinaryFormatException`, so every 7-bit int has exactly one encoding. The rule covers every place
the encoding appears: string and blob lengths, the keyed field count and keys, and the lengths of
the header strings. Without it a header string length could be respelled without changing the
decoded field, and therefore without changing the associated data the authentication tag covers.

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
order, then the rest in ordinal name order, and where two declarations share a name, the one declared
further up the inheritance chain first (§14.1).

Keyed fields appear in strictly ascending key order. A reader rejects a key that is not greater than
the one before it with `BinaryFormatException` — a repeated key and an out-of-order key alike, whether
the reader knows the key or would skip it — so a keyed object has exactly one encoding. A field
payload is exactly as long as its declared length; reading one consumes it exactly, and trailing
bytes inside a field are `BinaryFormatException`. A reader skips a key it does not know by its
declared length.

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
- `uncompressedLength` exceeds `compressedLength` by at most `MaxDecompressionRatio`, otherwise
  `BinaryLimitException`;
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

`onDiskLength` is not part of the image. It is self-verifying, because a wrong value either
truncates the read or fails the tag.

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
there. A span or a sequence read without a bytes-consumed form is the exception: it is exactly one
payload, and bytes after the root are `BinaryFormatException` (§3.4, §10.2). A keyed field's declared length is
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
- **Duplicates.** A key or an element a payload declares twice is malformed input, wherever the
  container would otherwise have decided for itself. Left to them the containers disagree — a
  dictionary raises, a `ConcurrentDictionary` drops the repeat, a set collapses it — so the same
  bytes would be a failure, a silent loss of data, or neither, depending only on which type a member
  happens to be declared as. The engine owns the element loop and therefore owns this rule: a
  container that refuses the value reports `BinaryFormatException`, and one that would have collapsed
  it is caught by comparing the materialized count with the count the payload declared. A sequence
  that admits repeats — a list, an array, a queue — is unaffected: the same value twice is data
  there, not a duplicate. A null dictionary key is refused the same way.
- **`DateTime`** travels as `ToBinary()`, which carries the kind but not the zone. A value whose
  `Kind` is `Local` is encoded as the instant it names and is reconstructed in the **reader's** local
  zone, so the instant survives a machine in another zone and the wall-clock value does not. Use
  `DateTimeOffset`, or `DateTimeKind.Utc`, when the value must compare equal on both ends.
- **`ImmutableArray<T>`** distinguishes default from empty; every other container does not.

# 24. Release checklist

A box is checked only when source and a test prove it.

## Contract and API

- [x] Public `BinarySerializer` overloads match this contract.
- [x] Default/options construction paths are stable; `Configure()...Build()` is the only path.
- [x] Stream ownership behavior is verified.
- [x] Keys for reading are supplied with `WithKeys`, in one place with `WithEncryption`; every entry point reads a non-seekable source it can read (§3.1, §4.1, §20).
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
- [x] A declared decompression expansion is bounded against the compressed bytes delivered.
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
- [x] The member plan is a total order and covers the whole inheritance chain.

## Security

- [x] Attacker-controlled counts/lengths are validated before allocation.
- [x] Declared lengths are compared with physically available bytes before allocation, and the one
  that cannot be — the uncompressed length — is bounded against them by ratio while the buffer that
  receives it grows with the output rather than with the declaration.
- [x] Unknown keyed payloads are skipped without whole-payload allocation.
- [x] Reference markers/IDs are validated, declared once, and ancestor-scoped.
- [x] A duplicate key or element is malformed input, decided by the engine rather than by whichever
  container happens to receive it.
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
- [x] Payload bytes are reachable only through `WireReader`/`WireWriter`, `ref struct`s over memory.
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
