# Options and limits

`BinarySerializerOptions` is the immutable configuration one `BinarySerializer` runs with. It holds
the algorithms, the key source, the policies and the limits. Options have no public setters: a builder
is the only way to make them, and it validates the result once.

```csharp
using ViShap.Viper;

var defaults = BinarySerializerOptions.Default;

BinarySerializerOptions custom = BinarySerializerOptions.Configure()
    .PreserveReferences()
    .Build();

Console.WriteLine(defaults.WriteVersion + " " + defaults.PreserveReferences);
Console.WriteLine(custom.WriteVersion + " " + custom.PreserveReferences);
```

`new BinarySerializer()` uses `BinarySerializerOptions.Default`.

## Defaults

| Setting | Default |
|---|---|
| format written | V1 |
| compression | none |
| checksum | none |
| encryption | none |
| reference preservation | off |
| reading headerless (V0) input | off |
| `RequireEncryption`, `RequireChecksum` | off |
| limits | `SerializationLimits.Default` |

## The builder

| Method | What it does |
|---|---|
| `WithCompression(ICompressionAlgorithm)` | compresses the payloads this serializer writes |
| `WithChecksum(IChecksumAlgorithm)` | stores a checksum of the raw payload with each frame written |
| `WithEncryption(algorithm, key, keyId)` | encrypts what is written with a fixed key; the key is also used for reading |
| `WithEncryption(algorithm, keyResolver, keyId)` | the same, with the key looked up by id |
| `WithEncryption(algorithm, IKeyProvider, keyId)` | the same, with keys from a provider of your own |
| `WithKeys(key, keyId)`, `WithKeys(keyResolver)`, `WithKeys(IKeyProvider)` | supplies keys for **reading** encrypted frames, without encrypting what is written |
| `WithVersion(0 or 1)` | selects the format used for writing; see [Formats](formats.md) |
| `PreserveReferences(bool)` | writes frames that keep shared and cyclic references; see [References](references.md) |
| `WithLimits(SerializationLimits)` | replaces the limits |
| `AllowV0Fallback(bool)` | allows reading headerless input as V0; it does not change what is written |
| `RequireEncryption(bool)` | rejects a frame that is not encrypted with an algorithm that authenticates the header |
| `RequireChecksum(bool)` | rejects a frame that carries no checksum |
| `RegisterCustomCompression`, `RegisterCustomChecksum`, `RegisterCustomEncryption` | registers an algorithm of your own under a name, so a frame that names it can be read |
| `Build()` | validates and returns the options |

A null argument is `ArgumentNullException`. Compression, checksum and encryption algorithms, keys, and
custom registrations are covered in [Algorithms and keys](algorithms-and-keys.md).

### What `Build()` rejects

Invalid configuration is a `BinaryConfigurationException`, raised by `Build()` and not at the first
call:

- a write version that is not a supported format;
- keys supplied both through `WithEncryption` and through `WithKeys`;
- `RequireEncryption` without an encryption algorithm, or with an algorithm that does not
  authenticate associated data;
- `RequireChecksum` without a checksum algorithm;
- `RequireEncryption` or `RequireChecksum` together with `WithVersion(0)` or with `AllowV0Fallback`,
  because a headerless payload has no header to protect;
- a fixed key given to `WithEncryption` whose length is not the algorithm's `KeySizeInBytes`;
- a limit that is zero or negative.

`Build()` also refuses options that encrypt with `ChaCha20Poly1305Encryption` on a platform whose
cryptography library does not provide it, with `BinaryFormatNotSupportedException`.

A key from a resolver or a provider does not exist until it is resolved, so its length is checked then,
with `BinaryEncryptionKeyException`.

## Limits

`SerializationLimits` is the resource policy of one serializer: how much a single call may consume.
Reading turns bytes you may not control into objects, so every quantity a payload can declare is capped
here, and a payload that exceeds a cap is rejected with `BinaryLimitException` before the work it asked
for is done. Limits are immutable, positive, validated when the options are built, and no payload can
raise them.

Derive a policy from the defaults with `with`:

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;
using ViShap.Viper.Security;

var producer = new BinarySerializer();
byte[] bytes = producer.Serialize(new Batch { Ids = Enumerable.Range(0, 100).ToList() });

var strict = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithLimits(SerializationLimits.Default with { MaxCollectionLength = 10 })
    .Build());

try
{
    strict.Deserialize<Batch>(bytes);
}
catch (BinaryLimitException)
{
    Console.WriteLine("the batch is larger than this reader accepts");
}

public sealed class Batch
{
    public List<int> Ids { get; set; } = [];
}
```

Each public call gets its own budget of what it has consumed, and a budget is never shared between
calls. For a stream of frames every frame is a call, so limits apply per frame and never per connection.

### The values

| Limit | Default | Bounds |
|---|---:|---|
| `MaxDepth` | 512 | structural nesting, counting objects and containers alike; it keeps a hostile payload from exhausting the stack, and applies to writing as well as reading |
| `MaxArrayLength` | 1,000,000 | one array; for a rank above one, every dimension and their product. It bounds every element type alike, `byte[]` included |
| `MaxCollectionLength` | 1,000,000 | the elements of one collection |
| `MaxDictionaryEntries` | 1,000,000 | the entries of one dictionary |
| `MaxStringBytes` | 4,000,000 | one string, in UTF-8 bytes rather than characters |
| `MaxByteBlobBytes` | 16,000,000 | a value written as a length followed by raw bytes: the body of a `BigInteger` and the data of a `BitArray` (whose bit count is bounded by this times eight). It does not bound `byte[]` |
| `MaxTotalElements` | 10,000,000 | the elements of every array, collection and dictionary of the whole call, one per element |
| `MaxObjectGraphNodes` | 1,000,000 | the objects and containers created by the whole call; a back reference to an existing object does not count |
| `MaxKeyedFields` | 1,000,000 | the fields of one [keyed](contracts.md#keyed-layout) object; checked before the total below |
| `MaxTotalKeyedFields` | 10,000,000 | the keyed fields of the whole call, the unknown ones that are skipped included |
| `MaxPayloadBytes` | 64 MiB | the logical, uncompressed payload; it is also the ceiling on decompression |
| `MaxCompressedBytes` | 64 MiB | the compressed representation — the payload itself when there is no compression |
| `MaxDecompressionRatio` | 10,000 | how far a payload may declare that it expands: the declared uncompressed length over the compressed length |
| `MaxEncryptedBytes` | 64 MiB + 64 KiB | the encrypted representation, nonce and tag included |
| `MaxWireBytes` | 80 MiB | the bytes one call takes from its source or emits to its destination, counted from where the call starts |

The defaults suit general use. Tighten them when the input is untrusted — the useful ones are usually
`MaxWireBytes`, `MaxPayloadBytes` and `MaxDepth` — and loosen them only for data you produced.

Limits are a resource policy, not an authorization: they bound what parsing and materialization may
cost, and they do not tell a legitimate sender from a hostile one.

### What a breach looks like

| Situation | Exception |
|---|---|
| a configured limit is zero or negative | `BinaryConfigurationException`, at `Build()` |
| a length, count or id in the payload is not a non-negative `Int32` | `BinaryFormatException` |
| a length or count is above its limit | `BinaryLimitException`, a subtype of `BinaryFormatException` |
| a length or count is within its limit but the bytes to back it are not there | `BinaryFormatException` |

A declared length the limits admit is therefore never reported as a limit breach just because the
payload is shorter than it claims.

### Carrying binary data

A `byte[]` is an ordinary array: it is bounded by `MaxArrayLength`, and each of its bytes is one element
of `MaxTotalElements`. The same holds for `Memory<byte>`, `ReadOnlyMemory<byte>`, `ArraySegment<byte>`
and `ReadOnlySequence<byte>`. To carry more than the defaults allow, raise both limits together — either
one alone still refuses the value — and keep `MaxPayloadBytes` in step.

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;
using ViShap.Viper.Security;

byte[] file = new byte[2_000_000];

try
{
    new BinarySerializer().Serialize(file);
}
catch (BinaryLimitException)
{
    Console.WriteLine("refused with the default limits");
}

var relaxed = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithLimits(SerializationLimits.Default with
    {
        MaxArrayLength = 4_000_000,
        MaxTotalElements = 4_000_000
    })
    .Build());

byte[] frame = relaxed.Serialize(file);
Console.WriteLine(relaxed.Deserialize<byte[]>(frame)!.Length);
```

### Phase limits

For a [V1 frame](formats.md) the size limits are checked in the order the bytes are handled, each check
before the buffer it protects is allocated:

1. **The header.** The declared stored length is checked against `MaxEncryptedBytes` for an encrypted
   frame, `MaxCompressedBytes` for a compressed one, `MaxPayloadBytes` otherwise, and against the bytes
   that can still arrive. With compression, the declared uncompressed length is checked against
   `MaxPayloadBytes` and against the stored length times `MaxDecompressionRatio`.
2. **Decryption.** It works in a buffer no longer than the ciphertext actually delivered, so a short frame
   cannot force a large allocation by claiming one.
3. **After decryption.** The plaintext is checked against `MaxCompressedBytes`, or `MaxPayloadBytes`
   without compression, and the declared uncompressed length against the plaintext length times
   `MaxDecompressionRatio`.
4. **Decompression.** The one allocation the ratio protects, made only after the checks above. The output
   buffer starts small and grows with the bytes actually produced, up to the declared length, so a frame
   that declares a large expansion and produces nothing costs almost nothing.
5. **The checksum** is verified over the raw payload before the engine reads a byte of it.

`MaxDecompressionRatio` matters for highly repetitive data: a large zero-filled buffer compresses well
beyond the default ratio, and raising the ratio is what lets it through.

When writing, `MaxPayloadBytes` bounds the payload as it is produced, so a graph that would exceed it
fails while being written; the finished frame is checked against `MaxWireBytes` before any byte reaches
the destination. The write budget counts only the bytes the call produces, so appending to a stream that
already holds data costs nothing.
