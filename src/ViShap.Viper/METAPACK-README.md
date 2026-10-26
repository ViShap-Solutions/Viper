# ViShap.Viper

A binary serializer for .NET 10. Viper writes objects as self-describing frames, reads them from memory,
streams and pipes, and can compress, checksum and encrypt a frame. It is built to read bytes it does not
trust: every declared length, count and nesting level is bounded before anything is allocated for it, and
every failure is an exception from one hierarchy.

This is the meta-package. It ships no code of its own; it references the three packages that do:

| Package | Contains |
|---|---|
| [`ViShap.Viper.Core`](https://www.nuget.org/packages/ViShap.Viper.Core) | the attributes, the algorithm and key contracts, the exception hierarchy |
| [`ViShap.Viper.Serialization`](https://www.nuget.org/packages/ViShap.Viper.Serialization) | the serializer, its options and limits, the built-in algorithms and key providers, the diagnostics |
| [`ViShap.Viper.Generator`](https://www.nuget.org/packages/ViShap.Viper.Generator) | the build-time source generator of type contracts; it runs in the compiler and ships nothing with your application |

```bash
dotnet add package ViShap.Viper
```

## Getting started

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

var order = new Order
{
    Id = 1001,
    Customer = "Ada Lovelace",
    Lines = [new OrderLine { Sku = "ANL-1", Quantity = 2 }]
};

byte[] bytes = serializer.Serialize(order);
Order? copy = serializer.Deserialize<Order>(bytes);

Console.WriteLine(copy!.Lines[0].Sku);

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}
```

`Serialize` returns one frame: a header that records the format version and any compression, checksum and
encryption, followed by the payload. `Deserialize` reads all of that from the bytes.

## What it offers

- **Buffers, streams and pipes.** `byte[]`, `IBufferWriter<byte>`, pooled payloads, `ReadOnlySpan<byte>`,
  `ReadOnlySequence<byte>`, `Stream` and `System.IO.Pipelines`, with asynchronous methods, `Populate` into
  an existing object, and a stream of frames.
- **Explicit layouts.** Positional by default; keyed contracts with `[BinaryContract]` and
  `[BinaryKey(n)]` so a type can evolve without breaking stored data.
- **Declared polymorphism.** `[BinaryUnion(tag, typeof(Derived))]`; only tags travel, never type names.
- **Shared and cyclic references** with `PreserveReferences()`.
- **Data protection.** Deflate and Brotli compression, CRC-32 and XXH3 checksums, AES-256-GCM and
  ChaCha20-Poly1305 encryption with an authenticated header, key providers with key ids, and
  `RequireEncryption` and `RequireChecksum` policies.
- **Two formats.** V1, the self-describing frame, and V0, the headerless payload for protocols that
  already frame their messages.
- **Limits.** Bounds on depth, sizes, cumulative budgets and the size of every phase, checked before the
  memory they protect is allocated.
- **Diagnostics.** `BinaryFormatInspector` reads a header; `BinaryFormatDumper` renders a frame as a tree
  and points at the value that failed.

## Documentation

- [Getting started](https://github.com/ViShap-Solutions/Viper/blob/main/docs/getting-started.md)
- [Entry points](https://github.com/ViShap-Solutions/Viper/blob/main/docs/entry-points.md)
- [Options and limits](https://github.com/ViShap-Solutions/Viper/blob/main/docs/options-and-limits.md)
- [Formats: V1 and V0](https://github.com/ViShap-Solutions/Viper/blob/main/docs/formats.md)
- [Contracts and schema evolution](https://github.com/ViShap-Solutions/Viper/blob/main/docs/contracts.md)
- [Algorithms and keys](https://github.com/ViShap-Solutions/Viper/blob/main/docs/algorithms-and-keys.md)
- [Exceptions](https://github.com/ViShap-Solutions/Viper/blob/main/docs/exceptions.md)
- [All pages](https://github.com/ViShap-Solutions/Viper/tree/main/docs)

The public API is documented in XML that ships beside the assemblies. Viper is released under the MIT
License.
