# Algorithms and keys

A [V1 frame](formats.md) can be compressed, carry a checksum, and be encrypted. Each is optional and
independent, and each is a header record that names the algorithm, so a reader learns from the
frame what to undo. A [V0 payload](formats.md#v0) has no header and none of the three.

Writing applies the phases in a fixed order — serialize, checksum the raw payload, compress, write the
header, encrypt — and reading reverses it. Nothing is written to the destination until the whole frame
exists, and the checksum is verified before the engine reads a single byte of the payload.

## Built-in algorithms

| Family | Class | Enum value | Notes |
|---|---|---|---|
| compression | `DeflateCompression` | `Deflate` | raw DEFLATE; takes a `CompressionLevel` |
| compression | `BrotliCompression` | `Brotli` | Brotli; takes a `CompressionLevel` |
| checksum | `Crc32Checksum` | `Crc32` | CRC-32, 4 bytes |
| checksum | `XxHash3Checksum` | `XxHash3` | XXH3, 64 bits, 8 bytes |
| checksum | `XxHash128Checksum` | `XxHash128` | XXH3, 128 bits, 16 bytes |
| encryption | `Aes256GcmEncryption` | `Aes256Gcm` | AES-256-GCM; 32-byte key; adds 28 bytes (12-byte nonce, 16-byte tag) |
| encryption | `ChaCha20Poly1305Encryption` | `ChaCha20Poly1305` | ChaCha20-Poly1305; 32-byte key; adds 28 bytes; not available on every platform |

Each class carries its family as a suffix, so none shares a simple name with a .NET type such as
`System.IO.Hashing.Crc32`, and importing both namespaces never produces an ambiguity. The enum values
keep the short names.

`NoCompression`, `NoChecksum` and `NoEncryption` stand for the absence of a phase. Built-in algorithms
cannot be substituted: what encrypts a payload is decided by the options that were built, and no
process-wide registry exists that another component could change.

## Compression

```csharp
using System.IO.Compression;
using ViShap.Viper;
using ViShap.Viper.Compression;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithCompression(new BrotliCompression(CompressionLevel.SmallestSize))
    .Build());

byte[] frame = serializer.Serialize(Enumerable.Repeat("viper", 1000).ToList());
List<string>? copy = serializer.Deserialize<List<string>>(frame);

Console.WriteLine(copy!.Count);
Console.WriteLine(frame.Length < 5000);
```

Compression is the one phase whose output can legitimately be larger than its input, so it is the one
phase where a declared size is not backed by bytes that must arrive. Viper bounds it twice: the
declared uncompressed length may not exceed the compressed length by more than `MaxDecompressionRatio`,
and the buffer that receives the output starts small and grows only as output is produced. A frame that
declares a large expansion and delivers nothing costs a probe and no more. Decompression must produce
exactly the declared length: fewer bytes and more bytes are both rejected, and malformed compressed data
is a `BinaryFormatException`. See [Options and limits](options-and-limits.md#phase-limits).

## Checksums

A checksum is computed over the raw payload and stored in the header. It detects accidental corruption
anywhere in the pipeline.

```csharp
using ViShap.Viper;
using ViShap.Viper.Checksum;
using ViShap.Viper.Exceptions;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithChecksum(new Crc32Checksum())
    .Build());

byte[] frame = serializer.Serialize("hello, viper");
frame[^1] ^= 0xFF;

try
{
    serializer.Deserialize<string>(frame);
}
catch (BinaryIntegrityException)
{
    Console.WriteLine("the payload does not match its checksum");
}
```

A checksum is not authentication. It proves nothing against someone who modifies the data on purpose,
because they can recompute it. For that, encrypt with an authenticated cipher, which also binds the
header.

## Encryption

Both built-in ciphers are authenticated (AEAD): a modified payload, a modified header and a wrong key
all fail verification, and a failure surfaces as `BinaryIntegrityException` — authentication cannot tell
a wrong key from modified data. A fresh nonce is generated for every message, so a key can be reused
across messages without tracking anything.

```csharp
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Crypto;
using ViShap.Viper.Exceptions;

byte[] key = RandomNumberGenerator.GetBytes(32);

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithEncryption(new Aes256GcmEncryption(), key, keyId: "2026-q3")
    .RequireEncryption()
    .Build());

byte[] frame = serializer.Serialize(new Secret { Token = "s3cret" });
Console.WriteLine(serializer.Deserialize<Secret>(frame)!.Token);

frame[^1] ^= 0xFF;
try
{
    serializer.Deserialize<Secret>(frame);
}
catch (BinaryIntegrityException)
{
    Console.WriteLine("the frame was modified");
}

public sealed class Secret
{
    public string Token { get; set; } = "";
}
```

The header is authenticated too: its exact bytes are the associated data of the cipher, so changing any
of them — the version, an algorithm, the key id, a length — either breaks the header's own rules or
fails authentication with `BinaryIntegrityException`. The frame is sized, checked against the limits and
given its key before the destination is touched, and the cipher then writes straight into the
destination, so a failure leaves nothing committed.

`ChaCha20Poly1305Encryption` comes from the platform's cryptography library. Where the platform lacks
it, `Build()` refuses options that encrypt with it and a frame that names it is refused when read, both
with `BinaryFormatNotSupportedException`.

### Requiring protection

Configuring an encryption algorithm only means the serializer *can* decrypt. A message whose header says
"not encrypted" is still read, because the header describes one message and the configuration describes
a capability. To refuse such a message, require protection:

| Policy | Rejects, with `BinaryIntegrityException` |
|---|---|
| `RequireEncryption()` | a frame that is not encrypted, or that is encrypted with an algorithm that does not authenticate the header |
| `RequireChecksum()` | a frame with no checksum |

```csharp
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Crypto;
using ViShap.Viper.Exceptions;

byte[] key = RandomNumberGenerator.GetBytes(32);

var strict = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithEncryption(new Aes256GcmEncryption(), key)
    .RequireEncryption()
    .Build());

byte[] plaintextFrame = new BinarySerializer().Serialize("not encrypted");

try
{
    strict.Deserialize<string>(plaintextFrame);
}
catch (BinaryIntegrityException)
{
    Console.WriteLine("an unencrypted frame is a downgrade");
}
```

Both policies talk about a frame's header, so both are incompatible with V0: `Build()` refuses them
together with `WithVersion(0)` or `AllowV0Fallback`. `RequireEncryption` also requires an algorithm that
reports `AuthenticatesAssociatedData`, and `Build()` refuses one that does not.

## Keys

Key material is a `SecretKey`, which always owns a copy of its bytes, obtained from an `IKeyProvider`.

- The serializer never modifies or clears memory it does not own. The array you pass to the builder, and
  the arrays a key resolver returns, are copied and left as they are.
- Each key a provider resolves is disposed by the code that asked for it when the phase ends, and
  temporary buffers that held plaintext or key bytes are cleared.
- A fixed key handed to `WithEncryption` is checked against the algorithm's `KeySizeInBytes` by `Build()`,
  with `BinaryConfigurationException`. A key a provider resolves — for writing, or for reading a frame
  whose algorithm the reader learns only from the header — is checked when it is resolved, with
  `BinaryEncryptionKeyException`. A missing key is `BinaryEncryptionKeyException` as well.
- The **key id** is a selector, not a secret: it is written in the header, at most 256 UTF-8 bytes, and
  it tells a reader which key to look up. Supply one as soon as more than one key is in circulation.

### Providers

| Provider | Use it when |
|---|---|
| `StaticKeyProvider` | one key, optionally bound to an id; a payload naming a different id is refused rather than decrypted with the wrong key. `WithEncryption(algorithm, key, keyId)` and `WithKeys(key, keyId)` build one for you |
| `DelegateKeyProvider` | keys come from a function of the key id, such as a vault lookup. `WithEncryption(algorithm, resolver, keyId)` and `WithKeys(resolver)` build one for you |
| `HkdfKeyProvider` | one root key, and one derived key per key id: HKDF-SHA-256 with the id as info, with an optional salt and a key size that is 32 by default. The root key is copied and never exposed. A key id holding a lone surrogate is refused with `BinaryEncryptionKeyException`, so two ids never derive the same key |
| your own `IKeyProvider` | anything else; return an owned `SecretKey` on every call |

```csharp
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Crypto;

using var provider = new HkdfKeyProvider(RandomNumberGenerator.GetBytes(32));

var writer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithEncryption(new Aes256GcmEncryption(), provider, keyId: "tenant-42")
    .Build());

byte[] frame = writer.Serialize("for tenant 42");

// a reader needs no algorithm of its own, only the keys
var reader = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithKeys(provider)
    .Build());

Console.WriteLine(reader.Deserialize<string>(frame));
```

The same id always derives the same key and different ids derive independent ones. A frame that names no
key id has nothing to derive from, and asking the provider for it is a `BinaryEncryptionKeyException`.
Disposing a provider clears only its own copy; using one afterwards is `ObjectDisposedException`.

### Reading with keys only

A V1 frame names its own algorithms, so a reader needs no algorithm of its own — only keys. `WithKeys`
supplies them without choosing encryption for writing. Keys have one place: supplying them through both
`WithEncryption` and `WithKeys` is a `BinaryConfigurationException` at `Build()`.

### Rotating keys

A writer records the id of the key it used, and a reader looks the key up by the id it finds. A resolver
therefore lets one reader follow rotation: it reads frames encrypted under any key it can still produce.

```csharp
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Crypto;
using ViShap.Viper.Exceptions;

var keys = new Dictionary<string, byte[]>
{
    ["2026-q2"] = RandomNumberGenerator.GetBytes(32),
    ["2026-q3"] = RandomNumberGenerator.GetBytes(32)
};

byte[]? Lookup(string? id) => id is not null && keys.TryGetValue(id, out byte[]? key) ? key : null;

BinarySerializer WriterFor(string id) => new(BinarySerializerOptions.Configure()
    .WithEncryption(new Aes256GcmEncryption(), Lookup, keyId: id)
    .Build());

var reader = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithKeys(Lookup)
    .Build());

byte[] old = WriterFor("2026-q2").Serialize("written last quarter");
byte[] current = WriterFor("2026-q3").Serialize("written this quarter");

Console.WriteLine(reader.Deserialize<string>(old));
Console.WriteLine(reader.Deserialize<string>(current));

keys.Remove("2026-q2");
try
{
    reader.Deserialize<string>(old);
}
catch (BinaryEncryptionKeyException)
{
    Console.WriteLine("the retired key is gone");
}
```

## Your own algorithms

The three algorithm interfaces are public so an algorithm of your own can be plugged in. They live in
`ViShap.Viper.Core`, so an algorithm can be written without referencing the serializer.

An implementation is pure mechanics: it transforms bytes and never sees a limit, framing or buffer
lifetime. Each interface has one method per direction and no default members, and must be safe for
concurrent use or be supplied through a factory that returns a fresh instance each time.

| Interface | Members to implement |
|---|---|
| `ICompressionAlgorithm` | `Kind`, `CustomName`, `Compress(source, IBufferWriter<byte>)`, `Decompress(source, IBufferWriter<byte>, expectedLength)` |
| `IChecksumAlgorithm` | `Kind`, `CustomName`, `HashSizeInBytes` (1 to 255), `Compute(source, destination)` |
| `IEncryptionAlgorithm` | `Kind`, `CustomName`, `AuthenticatesAssociatedData`, `KeySizeInBytes`, `GetCiphertextLength`, `Encrypt`, `Decrypt` |
| `IKeyProvider` | `Resolve(keyId)`, returning an owned `SecretKey` |

To use one, set `Kind` to `Custom`, give it a stable `CustomName`, use it for writing with the matching
`With…` method, and register the same name on every reading side. The name is what a frame carries; a
frame that names an algorithm the reader has not registered is a `BinaryFormatNotSupportedException`.

```csharp
using ViShap.Viper;
using ViShap.Viper.Checksum;
using ViShap.Viper.Exceptions;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithChecksum(new Fletcher16Checksum())
    .RegisterCustomChecksum("fletcher16", () => new Fletcher16Checksum())
    .Build());

byte[] frame = serializer.Serialize("custom checksum");
Console.WriteLine(serializer.Deserialize<string>(frame));

try
{
    new BinarySerializer().Deserialize<string>(frame);
}
catch (BinaryFormatNotSupportedException)
{
    Console.WriteLine("this reader has not registered fletcher16");
}

public sealed class Fletcher16Checksum : IChecksumAlgorithm
{
    public ChecksumAlgorithm Kind => ChecksumAlgorithm.Custom;
    public string? CustomName => "fletcher16";
    public int HashSizeInBytes => 2;

    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int a = 0, b = 0;
        foreach (byte value in source)
        {
            a = (a + value) % 255;
            b = (b + a) % 255;
        }

        destination[0] = (byte)a;
        destination[1] = (byte)b;
    }
}
```

A registration is a factory, and it runs once each time a frame that names the algorithm is read. What
runs is therefore decided by the frame, so a factory may not take the operation outside the exception
hierarchy: a factory that throws, and one that returns `null`, are both a
`BinaryConfigurationException` naming the registration, with the cause as its inner exception.

The rules each interface holds an implementation to, and the exception a breach raises:

- **Compression.** `Compress` writes into the `IBufferWriter<byte>` it is given, which refuses space past
  `MaxCompressedBytes`. `Decompress` writes into a writer that grows as output arrives and refuses space
  past `expectedLength`, and must produce exactly `expectedLength` bytes; producing fewer, more, or
  leaving the stream unterminated is a `BinaryFormatException`. A framework `InvalidDataException` is
  reported as a `BinaryFormatException` with the cause preserved.
- **Checksums.** The hash size is constant and between 1 and 255 bytes; any other value is a
  `BinaryConfigurationException`.
- **Encryption.** `GetCiphertextLength` is exact — a function of the plaintext length alone — and never
  below it, so an algorithm that adds random padding cannot be plugged in. `Encrypt` fills the whole
  destination and returns its length. `Decrypt` returns a plaintext length within the ciphertext length.
  Breaking any of these, or refusing a destination with `ArgumentException`, is a
  `BinaryConfigurationException`. Report `AuthenticatesAssociatedData` as `true` only if the associated
  data takes part in the authentication tag; nothing can verify it, and `RequireEncryption` relies on
  it. Never repeat a nonce for a key. A `CryptographicException` becomes `BinaryEncryptionException`
  when writing and `BinaryIntegrityException` when reading.

A key provider of your own returns a fresh `SecretKey` per call, and reports a missing or unusable key
with `BinaryEncryptionKeyException`:

```csharp
using ViShap.Viper;
using ViShap.Viper.Crypto;
using ViShap.Viper.Exceptions;

var vault = new Dictionary<string, byte[]> { ["k1"] = new byte[32] };

var options = BinarySerializerOptions.Configure()
    .WithEncryption(new Aes256GcmEncryption(), new VaultKeyProvider(vault), keyId: "k1")
    .Build();

byte[] frame = new BinarySerializer(options).Serialize("from the vault");
Console.WriteLine(new BinarySerializer(options).Deserialize<string>(frame));

public sealed class VaultKeyProvider(Dictionary<string, byte[]> vault) : IKeyProvider
{
    public SecretKey Resolve(string? keyId) =>
        keyId is not null && vault.TryGetValue(keyId, out byte[]? material)
            ? SecretKey.CopyFrom(material)
            : throw new BinaryEncryptionKeyException($"The vault holds no key '{keyId}'.");
}
```
