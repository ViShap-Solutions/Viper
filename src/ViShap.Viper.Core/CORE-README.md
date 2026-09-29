# ViShap.Viper.Core

The contracts of Viper for .NET 10, with no dependencies and no policy: the attributes that describe a
type, the interfaces that compression, checksum and encryption algorithms and key providers implement,
`SecretKey`, and the exception hierarchy.

Reference this package when a component needs to talk about Viper without owning the serializer:

- a library that **annotates its types** with `[BinaryContract]`, `[BinaryKey]`, `[BinaryUnion]` and the
  other attributes, and leaves serializing them to the application;
- a library that **implements an algorithm** — `ICompressionAlgorithm`, `IChecksumAlgorithm`,
  `IEncryptionAlgorithm` — or a key provider — `IKeyProvider` — for an application to plug in;
- code that only **catches** `BinarySerializerException` and its subtypes.

To serialize, install [`ViShap.Viper`](https://www.nuget.org/packages/ViShap.Viper) or
[`ViShap.Viper.Serialization`](https://www.nuget.org/packages/ViShap.Viper.Serialization), which contain
the serializer, its options and the built-in algorithms.

```bash
dotnet add package ViShap.Viper.Core
```

## Attributes

| Attribute | Meaning |
|---|---|
| `[BinaryContract]` | the type is a keyed contract: every member carries a key or is ignored, and readers skip keys they do not know |
| `[BinaryKey(n)]` | the wire key of a member of a contract type |
| `[BinaryIgnore]` | leaves a member out |
| `[BinaryInclude]` | includes a non-public member of a positional type |
| `[BinaryOrder(n)]` | pins the position of a member of a positional type |
| `[BinaryUnion(tag, typeof(Derived))]` | declares a derived type a base class or interface may hold, and its one-byte tag |

```csharp
using ViShap.Viper;

[BinaryContract]
public sealed class Customer
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public int Age { get; set; }
    [BinaryIgnore] public string? CachedDisplayName { get; set; }
}
```

[Contracts and schema evolution](https://github.com/ViShap-Solutions/Viper/blob/main/docs/contracts.md) and
[Polymorphism](https://github.com/ViShap-Solutions/Viper/blob/main/docs/polymorphism.md) describe the rules.

## Writing an algorithm

An algorithm is pure mechanics. It transforms bytes and never sees a limit, the framing around the
payload or the lifetime of a buffer: the serializer calls it inside those checks, so an implementation can
neither weaken a limit nor be asked to enforce one. Each interface has one method per direction and no
default members, and an implementation must be safe for concurrent use, or be supplied through a factory
that returns a fresh instance for each use.

| Interface | Members |
|---|---|
| `ICompressionAlgorithm` | `Kind`, `CustomName`, `Compress(source, IBufferWriter<byte>)`, `Decompress(source, IBufferWriter<byte>, expectedLength)` |
| `IChecksumAlgorithm` | `Kind`, `CustomName`, `HashSizeInBytes` (1 to 255), `Compute(source, destination)` |
| `IEncryptionAlgorithm` | `Kind`, `CustomName`, `AuthenticatesAssociatedData`, `KeySizeInBytes`, `GetCiphertextLength`, `Encrypt`, `Decrypt` |
| `IKeyProvider` | `Resolve(keyId)`, returning an owned `SecretKey` |

Set `Kind` to `Custom` and give the algorithm a stable `CustomName`: the name is what a frame carries. The
application registers the same name on the side that reads, with the options builder of the serializer
package:

```csharp
using ViShap.Viper.Checksum;

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

The rules an implementation is held to:

- **Compression.** `Compress` writes into the buffer writer it is given, which refuses space beyond the
  configured maximum. `Decompress` writes into a writer that grows as output arrives, and must produce
  exactly `expectedLength` bytes; malformed or unterminated data, or any other length, is a
  `BinaryFormatException`.
- **Checksums.** The hash size is constant and between 1 and 255 bytes.
- **Encryption.** The ciphertext length is a function of the plaintext length alone, exact, and never below
  it, so an algorithm that adds random padding cannot be plugged in. `Encrypt` fills the whole
  destination; `Decrypt` returns a length within the ciphertext length. The serializer passes the frame
  header as associated data: report `AuthenticatesAssociatedData` as `true` only if it takes part in the
  authentication tag, because `RequireEncryption` relies on it and nothing can verify it. Never repeat a
  nonce for a key.
- **Breaking a statement** — a wrong ciphertext length, a hash size outside 1 to 255, a destination
  refused with `ArgumentException` — is reported as `BinaryConfigurationException`.

### Keys

`SecretKey` always holds its own copy of the bytes, and only the copy it owns is ever cleared. A key
provider returns a fresh `SecretKey` on every call, and the code that asked for it disposes it. A missing
or unusable key is a `BinaryEncryptionKeyException`.

```csharp
using ViShap.Viper.Crypto;
using ViShap.Viper.Exceptions;

public sealed class VaultKeyProvider(Dictionary<string, byte[]> vault) : IKeyProvider
{
    public SecretKey Resolve(string? keyId) =>
        keyId is not null && vault.TryGetValue(keyId, out byte[]? material)
            ? SecretKey.CopyFrom(material)
            : throw new BinaryEncryptionKeyException($"The vault holds no key '{keyId}'.");
}
```

See [Algorithms and keys](https://github.com/ViShap-Solutions/Viper/blob/main/docs/algorithms-and-keys.md).

## Exceptions

Every exception derives from `BinarySerializerException`:

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

[Exceptions](https://github.com/ViShap-Solutions/Viper/blob/main/docs/exceptions.md) says what each one
means and when it is raised.

## Documentation

The full documentation is in the
[`docs` folder](https://github.com/ViShap-Solutions/Viper/tree/main/docs) of the repository. The public API
is documented in XML that ships beside the assembly.

Viper is released under the MIT License.
