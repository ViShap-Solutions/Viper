# Diagnostics

A binary frame cannot be read by eye the way JSON can. Two tools in Viper close that gap:
`BinaryFormatInspector` reads a header without decoding the payload, and `BinaryFormatDumper` turns a
frame into something a person can read — what was written, where, and why it does not read back. When a
payload you expected to read does not, a dump of it is the first thing to look at.

The examples on this page use this type:

```csharp
public sealed class Order
{
    public long Id { get; set; }
    public string Customer { get; set; } = "";
    public List<Line> Lines { get; set; } = [];
}

public sealed class Line
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
```

## Reading a header

`BinaryFormatInspector.Peek` reads the header of a V1 frame — from a span, a sequence or a stream — and
returns a `BinaryHeaderInfo` without decoding the payload. It is how to choose a key before committing to
a read, or to route a frame by the algorithms it uses.

```csharp
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Metadata;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithCompression(new BrotliCompression())
    .WithChecksum(new XxHash3Checksum())
    .WithEncryption(new Aes256GcmEncryption(), RandomNumberGenerator.GetBytes(32), keyId: "2026-q3")
    .Build());

byte[] frame = serializer.Serialize("a frame to inspect");

BinaryHeaderInfo? header = BinaryFormatInspector.Peek(frame);

Console.WriteLine(header!.Value.Encryption + " with key " + header.Value.KeyId);
Console.WriteLine(header.Value.Compression + ", " + header.Value.UncompressedLength + " bytes uncompressed");
Console.WriteLine(header.Value.ChecksumAlgorithm);
Console.WriteLine(header.Value.HeaderLength + " header bytes, " + header.Value.OnDiskLength + " stored");
```

`BinaryHeaderInfo` reports the format version, whether the payload carries reference frames, each
algorithm with its custom name when it has one, the uncompressed length of a compressed frame, the
checksum the header records, the key id, and the length of the header and of the bytes after it. It never
contains key material: the key id names a key, it is not one.

`Peek` returns `null` for bytes that are simply not a recognized frame, and raises a
`BinaryFormatException` for a frame it recognizes and finds malformed. The stream overload needs a
seekable stream, restores the position on success and on failure, and reports an `IOException` as
`BinaryStreamException`.

## Dumping a frame

`BinaryFormatDumper` reads under the options' limits — `SerializationLimits.Default` when none are given —
through the ordinary read path, so a hostile frame costs a dump what it costs a read.

| Method | What it gives you |
|---|---|
| `DumpHeader(span \| sequence \| stream)` | the header as text: version, payload mode, each record, the declared lengths, the checksum in hex |
| `Dump(span, options)` | with no type: the header, each phase undone with the options' keys and verified, then the payload as annotated hex |
| `Dump<T>(span \| sequence, options)` | with `T` as the schema: the payload as a tree of `BinaryDumpNode` |
| `DumpValue<T>(value, options)` | writes the value with the options and dumps what was written |
| `Compare<T>(expected, actual, options)` | the first node at which two frames of `T` differ, or `null` |

```csharp
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Diagnostics;

byte[] key = RandomNumberGenerator.GetBytes(32);
var options = BinarySerializerOptions.Configure()
    .WithCompression(new BrotliCompression())
    .WithChecksum(new XxHash3Checksum())
    .WithEncryption(new Aes256GcmEncryption(), key, keyId: "2026-q3")
    .Build();

var order = new Order
{
    Id = 1001,
    Customer = "Ada Lovelace",
    Lines =
    [
        new Line { Sku = "ANL-1", Quantity = 2, Note = "gift" },
        new Line { Sku = "DIF-7", Quantity = 1, Note = null },
        new Line { Sku = "ENG-3", Quantity = 5, Note = "fragile" }
    ]
};

byte[] frame = new BinarySerializer(options).Serialize(order);

Console.WriteLine(BinaryFormatDumper.DumpHeader(frame));
Console.WriteLine(BinaryFormatDumper.Dump<Order>(frame, options));

public sealed class Order
{
    public long Id { get; set; }
    public string Customer { get; set; } = "";
    public List<Line> Lines { get; set; } = [];
}

public sealed class Line
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
```

The header comes first, then the tree. The checksum and the sizes depend on the algorithms and the
runtime, so the numbers below are what one run printed:

```text
Format version : 1
Header length  : 34
References     : none
Compression    : Brotli, 70 bytes uncompressed
Checksum       : XxHash3, F99E18257B275944
Encryption     : Aes256Gcm
Key id         : 2026-q3
On-disk length : 100
```

```text
Viper V1 frame · 134 bytes · header 34 bytes
  compression  Brotli         70 → 72 bytes (×1.0)
  checksum     XxHash3        F9 9E 18 25 7B 27 59 44   verified
  encryption   Aes256Gcm      key id "2026-q3"   decrypted, header authenticated
payload 70 bytes as Order · depth 4 · 16 nodes
@0000  Order                                           object · 3 members
@0001  ├─ Customer                    String           "Ada Lovelace"
@000E  ├─ Id                          Int64            1001
@0016  └─ Lines                       List<Line>       3 items
@0017     ├─ [0]                      Line             object · 3 members
@0018     │  ├─ Note                  String           "gift"
@001D     │  ├─ Quantity              Int32            2
@0021     │  └─ Sku                   String           "ANL-1"
@0027     ├─ [1]                      Line             object · 3 members
@0028     │  ├─ Note                  String           null
@0029     │  ├─ Quantity              Int32            1
@002D     │  └─ Sku                   String           "DIF-7"
@0033     └─ [2]                      Line             object · 3 members
@0034        ├─ Note                  String           "fragile"
@003C        ├─ Quantity              Int32            5
@0040        └─ Sku                   String           "ENG-3"
```

Each node shows its offset in the payload, its name — the member, `[index]`, `{key}` for a map value, or
the type for the root — its declared type (the runtime type for a union), its kind and its value when it
is a scalar. The offsets are in hexadecimal, and the members appear in the order the layout writes them,
which for a [positional type](contracts.md#positional-layout) is not the order they are declared in.

`BinaryDump` also reports the format version, the header and payload lengths, whether the checksum was
verified and whether the frame was decrypted (each `null` when it does not apply), the depth and the number
of nodes. A node carries its offset and length in the payload — every byte of it, its framing and its
children included — a keyed field's key, a union tag, a reference id and, for a back reference, the path
it points to.

`ToString()` renders the report above. `ToJson()` renders the same as JSON, `ToXml()` as XML with one
element per node, and `ToHex()` every byte of the payload sixteen to a line, each line labelled with its
node:

```text
@0000  01 0D 41 64 61 20 4C 6F 76 65 6C 61 63 65 E9 03  Order
@0010  00 00 00 00 00 00 04 01 05 67 69 66 74 02 00 00  Order.Id
@0020  00 06 41 4E 4C 2D 31 01 00 01 00 00 00 06 44 49  Order.Lines[0].Quantity
@0030  46 2D 37 01 08 66 72 61 67 69 6C 65 05 00 00 00  Order.Lines[1].Sku
@0040  06 45 4E 47 2D 33                                Order.Lines[2].Sku
```

## When a frame does not read

The dumper never throws for a malformed or hostile frame. It catches only `BinarySerializerException`
and keeps it in `BinaryDump.Failure`, with `FailureOffset` and `FailurePath` pointing at the value that
failed and the tree read up to it.

```csharp
using System.Text;
using ViShap.Viper;
using ViShap.Viper.Diagnostics;

var order = new Order
{
    Id = 1001,
    Customer = "Ada Lovelace",
    Lines =
    [
        new Line { Sku = "ANL-1", Quantity = 2, Note = "gift" },
        new Line { Sku = "ENG-3", Quantity = 5, Note = "fragile" }
    ]
};

byte[] bytes = new BinarySerializer().Serialize(order);

// damage the first byte of the last note so it is no longer valid UTF-8
int at = bytes.AsSpan().IndexOf("fragile"u8);
bytes[at] = 0xFF;

BinaryDump dump = BinaryFormatDumper.Dump<Order>(bytes);

Console.WriteLine(dump.Failure!.GetType().Name);
Console.WriteLine(dump.FailurePath);
Console.WriteLine(dump);

public sealed class Order
{
    public long Id { get; set; }
    public string Customer { get; set; } = "";
    public List<Line> Lines { get; set; } = [];
}

public sealed class Line
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
```

The last lines of the report say where the read stopped:

```text
failure: BinaryFormatException: String payload is not valid UTF-8.
failure at @0028 (40) in Order.Lines[1].Note
```

The offset is in hexadecimal with the decimal value in parentheses, and the path names the member —
`Order.Lines[1].Note` — that could not be read.

## Comparing two frames

`Compare<T>` walks the trees of two frames in order and returns the first node whose kind, type, value,
key, tag or number of children differs, or `null` when they are equal. Offsets are not compared, so two
encryptions of one value are equal.

```csharp
using ViShap.Viper;
using ViShap.Viper.Diagnostics;

var serializer = new BinarySerializer();

byte[] before = serializer.Serialize(new Line { Sku = "ENG-3", Quantity = 5, Note = "fragile" });
byte[] after = serializer.Serialize(new Line { Sku = "ENG-3", Quantity = 6, Note = "fragile" });

BinaryDumpDifference? difference = BinaryFormatDumper.Compare<Line>(before, after);

Console.WriteLine(difference!.Path);
Console.WriteLine(difference.Expected!.Value + " -> " + difference.Actual!.Value);

public sealed class Line
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
```

## Rules

- **Nothing is rendered that should not be.** A dump decrypts only with the keys of the options you give
  it. Without a key the phases stop at the ciphertext, `Decrypted` is `false`, `Failure` is the key
  failure and the report says so. Key material is never rendered. A decrypted dump does show the
  plaintext, so treat it as you would the data itself.
- **A dump reads the same everywhere.** Values render with the invariant culture and times in UTC, so a
  dump on one machine reads like the same dump on another. String values are cut at 256 characters and
  bit arrays at 64 bytes in the tree.
- **It is diagnostic tooling.** Production serializer code never reports a failure as output. What the
  dumper does raise: `ArgumentNullException` for a `null` stream, `NotSupportedException` for a stream
  that cannot seek, and, from `DumpValue`, whatever `Serialize` raises for a value that cannot be written.
- **Bytes without the magic number** are shown by `Dump` as a version 0 payload.

Every method that reads with `T` builds a codec for it by reflection; see
[Trimming and native AOT](aot.md). `DumpHeader`, the untyped `Dump` and `BinaryFormatInspector` do not.
