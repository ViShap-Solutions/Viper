# Getting started

Viper turns .NET objects into binary frames and back. A frame describes itself, so a reader needs no
out-of-band agreement about how it was written, and every quantity a frame declares is bounded before
the memory it asks for is allocated.

Viper targets .NET 10.

## Install

```bash
dotnet add package ViShap.Viper
```

`ViShap.Viper` is a meta-package: it ships no code and references the two packages that do.

| Package | Contains | Reference it directly when |
|---|---|---|
| `ViShap.Viper` | nothing of its own; references both packages below | you serialize objects — this is the usual choice |
| `ViShap.Viper.Core` | the attributes, the algorithm and key contracts, the exception hierarchy; no dependencies | a library annotates its types, or implements a compression, checksum or encryption algorithm or a key provider, without serializing anything itself |
| `ViShap.Viper.Serialization` | the serializer, its options and limits, the built-in algorithms and key providers, the diagnostics | you want the engine without the meta-package |

The public types live in a handful of namespaces. `BinarySerializer`, `BinarySerializerOptions`, its
builder, `PooledPayload` and the attributes are in `ViShap.Viper`, so one `using` is enough for the
first steps. The rest are named where they are introduced: `ViShap.Viper.Security` (limits),
`ViShap.Viper.Compression`, `ViShap.Viper.Checksum`, `ViShap.Viper.Crypto`, `ViShap.Viper.Metadata`
(header inspection), `ViShap.Viper.Diagnostics` and `ViShap.Viper.Exceptions`. Every example in this
documentation shows the `using` directives it needs.

## The first round trip

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

var order = new Order
{
    Id = 1001,
    Customer = "Ada Lovelace",
    Lines =
    [
        new OrderLine { Sku = "ANL-1", Quantity = 2 },
        new OrderLine { Sku = "DIF-7", Quantity = 1 }
    ]
};

byte[] bytes = serializer.Serialize(order);
Order? copy = serializer.Deserialize<Order>(bytes);

Console.WriteLine(copy!.Customer);
Console.WriteLine(copy.Lines.Count);

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

`Serialize` returns one *frame*: a header followed by the payload. The header records the format
version, whether the payload keeps object identity, and which compression, checksum and encryption were
applied, so `Deserialize` finds all of that out from the bytes. [Formats](formats.md) describes the
frame.

## What a type needs

Any type made of the [supported types](supported-types.md) can be serialized without annotation. A
class or struct is written member by member, and it must be constructible by the reader: it needs a
parameterless constructor, which may be non-public, or be a struct.

Without annotations the layout is *positional*: the public read/write properties and the public
non-`readonly` fields are written in the order of their names, and that order is the format. Renaming
or adding a member changes what is written. When a type has to outlive the code that wrote it,
declare a keyed contract instead — [Contracts and schema evolution](contracts.md).

## One serializer per configuration

A `BinarySerializer` is immutable and safe to use from many threads at once, and each call is accounted
separately. Create one for each configuration and keep it.

```csharp
using ViShap.Viper;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;

BinarySerializerOptions options = BinarySerializerOptions.Configure()
    .WithCompression(new BrotliCompression())
    .WithChecksum(new XxHash3Checksum())
    .Build();

var serializer = new BinarySerializer(options);
```

`Configure()` starts a builder and `Build()` validates the result once; an invalid combination is a
`BinaryConfigurationException` at that point rather than at the first call. [Options and
limits](options-and-limits.md) lists every setting, and [Algorithms and keys](algorithms-and-keys.md)
covers compression, checksums and encryption.

## Reading input you do not control

Reading turns bytes into objects, so a reader is exposed to whatever the bytes declare. Viper bounds
every declared length, count and nesting level with `SerializationLimits`, whose defaults are finite,
and it rejects a declaration the remaining bytes cannot back before allocating for it. Tighten the limits
to what your data legitimately needs: [Options and limits](options-and-limits.md).

## When something fails

Every failure that comes from the data, the configuration or the destination is a
`BinarySerializerException`, and its subtype says what kind of failure it was.

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;

var serializer = new BinarySerializer();

try
{
    serializer.Deserialize<int>(new byte[] { 0xFF, 0xFF });
}
catch (BinaryLimitException)
{
    Console.WriteLine("a limit was exceeded");
}
catch (BinaryFormatException e)
{
    Console.WriteLine("malformed input: " + e.GetType().Name);
}
```

`BinaryLimitException` derives from `BinaryFormatException`, so it is caught first.
[Exceptions](exceptions.md) lists the whole hierarchy.

## Where next

| You want to | Read |
|---|---|
| write to and read from buffers, streams and pipes | [Entry points](entry-points.md) |
| set limits, algorithms and policies | [Options and limits](options-and-limits.md) |
| choose between the two formats | [Formats: V1 and V0](formats.md) |
| evolve a type without breaking stored data | [Contracts and schema evolution](contracts.md) |
| keep shared and cyclic references | [References](references.md) |
| serialize a base type and its derived types | [Polymorphism](polymorphism.md) |
| compress, checksum or encrypt | [Algorithms and keys](algorithms-and-keys.md) |
| look inside a frame | [Diagnostics](diagnostics.md) |
| build with trimming or native AOT | [Trimming and native AOT](aot.md) |
