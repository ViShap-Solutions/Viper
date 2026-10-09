# ViShap.Viper.Serialization

The Viper serializer for .NET 10: it writes objects as self-describing binary frames, reads them from
memory, streams and pipes, and can compress, checksum and encrypt a frame. It is built to read bytes it
does not trust: every declared length, count and nesting level is bounded before anything is allocated
for it, and every failure is an exception from one hierarchy.

This package holds the engine, the built-in algorithms and key providers, and the diagnostics. It
depends on [`ViShap.Viper.Core`](https://www.nuget.org/packages/ViShap.Viper.Core) and
`System.IO.Hashing`. Most applications install the [`ViShap.Viper`](https://www.nuget.org/packages/ViShap.Viper)
meta-package instead, which references both.

```bash
dotnet add package ViShap.Viper.Serialization
```

## A round trip

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

byte[] bytes = serializer.Serialize(new Order { Id = 1001, Customer = "Ada Lovelace" });
Order? copy = serializer.Deserialize<Order>(bytes);

Console.WriteLine(copy!.Customer);

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
}
```

A `BinarySerializer` is immutable and safe to share between threads: create one per configuration.
`Serialize` and `Deserialize` also work with `IBufferWriter<byte>`, `ReadOnlySequence<byte>`, `Stream` and
`System.IO.Pipelines`, asynchronously where it makes sense, and `Populate` reads into an object you
already hold. See [Entry points](https://github.com/ViShap-Solutions/Viper/blob/main/docs/entry-points.md).

## Compression, a checksum and encryption

Each is optional and recorded in the frame's header, so a reader learns from the frame what to undo and
needs only a key.

```csharp
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;

byte[] key = RandomNumberGenerator.GetBytes(32);

var options = BinarySerializerOptions.Configure()
    .WithCompression(new BrotliCompression())
    .WithChecksum(new XxHash3Checksum())
    .WithEncryption(new Aes256GcmEncryption(), key, keyId: "2026-q3")
    .RequireEncryption()
    .Build();

var serializer = new BinarySerializer(options);
byte[] frame = serializer.Serialize("protected");

Console.WriteLine(serializer.Deserialize<string>(frame));
```

The built-in algorithms are `DeflateCompression`, `BrotliCompression`, `Crc32Checksum`,
`XxHash3Checksum`, `XxHash128Checksum`, `Aes256GcmEncryption` and `ChaCha20Poly1305Encryption`. With an
authenticated cipher the header is authenticated too. Key providers — `StaticKeyProvider`,
`DelegateKeyProvider`, `HkdfKeyProvider` — supply keys, and `RequireEncryption` and `RequireChecksum`
reject a frame that was downgraded. See
[Algorithms and keys](https://github.com/ViShap-Solutions/Viper/blob/main/docs/algorithms-and-keys.md).

## Types that can evolve

An unannotated type is written positionally: its layout is the order of its members. A keyed contract
gives each member a number, so readers skip keys they do not know and writers can add members without
breaking stored data.

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

byte[] bytes = serializer.Serialize(new Customer { Name = "Ada", Email = "ada@example.org" });
Console.WriteLine(serializer.Deserialize<Customer>(bytes)!.Email);

[BinaryContract]
public sealed class Customer
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public string? Email { get; set; }
    [BinaryIgnore] public string? CachedDisplayName { get; set; }
}
```

`[BinaryUnion(tag, typeof(Derived))]` declares the derived types a base class or interface may hold, and
`PreserveReferences()` keeps shared and cyclic references. A type's members can also be described by a
`TypeContract<T>` supplied through a `BinarySerializerContext` and `WithContracts(…)` instead of by
reflection; one that describes the type as reflection does writes the same bytes. See
[Contracts and schema evolution](https://github.com/ViShap-Solutions/Viper/blob/main/docs/contracts.md),
[Polymorphism](https://github.com/ViShap-Solutions/Viper/blob/main/docs/polymorphism.md) and
[References](https://github.com/ViShap-Solutions/Viper/blob/main/docs/references.md).

## Reading input you do not control

`SerializationLimits` bounds depth, array, collection, dictionary, string and blob sizes, cumulative
element, node and keyed-field budgets, and the size of each phase of a frame. The defaults are finite;
tighten them to what your data needs.

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;
using ViShap.Viper.Security;

var reader = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithLimits(SerializationLimits.Default with { MaxWireBytes = 1024 * 1024, MaxDepth = 32 })
    .Build());

try
{
    reader.Deserialize<string>(new BinarySerializer().Serialize(new string('x', 2_000_000)));
}
catch (BinaryLimitException)
{
    Console.WriteLine("larger than this reader accepts");
}
```

Every failure that comes from the data, the configuration or the destination is a
`BinarySerializerException`: `BinaryFormatException` (with the subtype `BinaryLimitException`),
`BinaryIntegrityException`, `BinaryEncryptionKeyException`, `BinaryTypeException` and the others listed in
[Exceptions](https://github.com/ViShap-Solutions/Viper/blob/main/docs/exceptions.md). See also
[Options and limits](https://github.com/ViShap-Solutions/Viper/blob/main/docs/options-and-limits.md).

## Two formats

V1, the default, is a self-describing frame: use it for stored data, data that crosses a trust boundary
and data whose schema will move. V0 is the headerless payload for private channels whose protocol
already frames each message; it is selected with `WithVersion(0)`, read only with `AllowV0Fallback`, and
is never authenticated. See [Formats](https://github.com/ViShap-Solutions/Viper/blob/main/docs/formats.md).

## Diagnostics

`BinaryFormatInspector.Peek` reads a header without decoding the payload, and `BinaryFormatDumper` renders
a frame as a tree with the offset and value of every node — and, when a frame does not read, the path of
the value that failed. See
[Diagnostics](https://github.com/ViShap-Solutions/Viper/blob/main/docs/diagnostics.md).

## Trimming and native AOT

The encoding of a type is built by reflection on first use, so every generic entry point that encodes or
decodes your type is marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Nothing else is. See
[Trimming and native AOT](https://github.com/ViShap-Solutions/Viper/blob/main/docs/aot.md).

## Documentation

The full documentation is in the
[`docs` folder](https://github.com/ViShap-Solutions/Viper/tree/main/docs) of the repository. The public API
is documented in XML that ships beside the assembly.

Viper is released under the MIT License.
