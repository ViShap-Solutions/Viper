# Formats: V1 and V0

Viper writes two formats. They are peers with different jobs, not a current format and a deprecated
one: neither is derived from the other, neither is scheduled for removal, and a reader never guesses
which one it is holding.

| | V1 | V0 |
|---|---|---|
| what it is | a self-describing frame: a header, then the payload | the payload alone — no header, no magic number, no length |
| written by | default | `WithVersion(0)` |
| read by | any serializer | a serializer built with `AllowV0Fallback` |
| compression, checksum, encryption | yes | no; configured algorithms are not applied to a V0 write |
| reference preservation | yes | no |
| authenticated | when an authenticated cipher is used | never |
| a stream of it | read from any stream, synchronously or asynchronously | read from a seekable stream, synchronously only |
| use it for | stored data, data crossing a trust boundary, data whose schema will move, data read by a party the writer did not configure | private or tightly coordinated channels whose protocol already frames each message |

Everything below the envelope is the same in both: the [supported types](supported-types.md),
[polymorphism](polymorphism.md), [keyed contracts](contracts.md), the [limits](options-and-limits.md)
and their accounting. V0 is not a reduced serializer; it is the same one without a header.

## V1

A V1 frame is a header followed by the payload:

```text
magic          4 bytes   42 53 45 52
version        varint    1
payload mode   varint    bit 0: the payload carries reference frames
service count  varint    the number of service records that follow
services       records   compression, checksum, encryption — each present only when applied
onDiskLength   varint    the number of bytes after the header
payload        onDiskLength bytes
```

An absent phase is an absent record. The header is at most 4,096 bytes. The header names the algorithms,
so a reader takes them from the frame and needs no algorithm of its own — only a key, for an encrypted
frame.

```csharp
using ViShap.Viper;

var v1 = new BinarySerializer();
var v0 = new BinarySerializer(BinarySerializerOptions.Configure().WithVersion(0).Build());

byte[] framed = v1.Serialize(new Ping { Id = 42 });
byte[] headerless = v0.Serialize(new Ping { Id = 42 });

Console.WriteLine(Convert.ToHexString(framed));
Console.WriteLine(Convert.ToHexString(headerless));

public sealed class Ping
{
    public int Id { get; set; }
}
```

The first line is the whole V1 frame, and the second is what V1 carries as its payload:

```text
42 53 45 52    magic
01             version
00             payload mode: no reference frames
00             no service records
05             onDiskLength: five bytes follow
01             the object is present
2A 00 00 00    Id = 42
```

Writing applies the phases in a fixed order: serialize the payload, checksum the raw payload, compress,
write the header, encrypt. Reading reverses it. When the frame is encrypted, the header's exact bytes
are the associated data of the cipher, so no header byte can be altered without the decryption failing
with a `BinaryIntegrityException`. [Algorithms and keys](algorithms-and-keys.md) covers the phases, and
[Diagnostics](diagnostics.md) shows how to read a header.

Other properties of the format:

- **A new capability is a new header record.** A reader refuses a record it does not know when the record
  is marked critical, with `BinaryFormatNotSupportedException`, and skips it by its length when it is
  not. An unknown format version is `BinaryFormatNotSupportedException` too.
- **A frame is canonical.** After the root value is read the payload must be consumed exactly, and
  trailing bytes are a `BinaryFormatException`. Every number and field has exactly one encoding, so a
  frame cannot be rewritten into a second spelling of the same value.
- **The frame decides how references are read.** `PreserveReferences()` decides what a writer produces;
  a reader follows the frame's payload mode whatever it is configured with.

## V0

V0 is the payload and nothing else. It is the deliberate choice for a caller who wants the value
without an envelope around it and who already knows, out of band, what the bytes are.

It fits when the transport is private or tightly coordinated so both ends are configured together, when
framing and context already exist outside the payload — a message type, a length prefix, a channel — and
when an encoding that carries no metadata is worth more than the envelope. IPC and other private
channels are the usual case.

It is the wrong choice for data at rest, for anything that must be compressed, checksummed or encrypted,
and for anything read by a party the writer did not configure. Schema evolution is not on that list: a
keyed contract is a property of the payload and works under V0.

What V0 does not have:

- a header of any kind — no magic number, no version, no algorithm names, no lengths;
- a compression, checksum or encryption phase. Algorithms configured on the options are not applied to a
  V0 write, because a V0 reader has nowhere to learn that they were;
- reference preservation. `PreserveReferences` does not apply, and a cycle in the graph is a
  `BinaryTypeException`.

For one value under one member layout, the V0 payload is byte-for-byte the payload a V1 frame carries
when that frame has no service records and no reference frames — the second line printed above.

### Reading V0 is an explicit decision

Nothing in a V0 payload identifies it. Reading one is therefore something the caller states with
`AllowV0Fallback`, and without it bytes that do not begin with the V1 magic number are rejected with a
`BinaryFormatException` rather than parsed.

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;

var v0 = new BinarySerializer(BinarySerializerOptions.Configure().WithVersion(0).Build());
byte[] headerless = v0.Serialize(new Ping { Id = 42 });
byte[] framed = new BinarySerializer().Serialize(new Ping { Id = 42 });

var reader = new BinarySerializer(BinarySerializerOptions.Configure().AllowV0Fallback().Build());
Console.WriteLine(reader.Deserialize<Ping>(headerless)!.Id);
Console.WriteLine(reader.Deserialize<Ping>(framed)!.Id);

try
{
    new BinarySerializer().Deserialize<Ping>(headerless);
}
catch (BinaryFormatException)
{
    Console.WriteLine("headerless bytes are not read without AllowV0Fallback");
}

public sealed class Ping
{
    public int Id { get; set; }
}
```

`WithVersion(0)` alone selects V0 for writing, and `AllowV0Fallback` alone selects nothing about
writing: the two choices are independent in both directions. A serializer with `AllowV0Fallback` still
reads V1 frames as V1 — a frame is identified by its magic number followed by a complete version, and
only bytes that are not identified that way are read as V0.

A V0 payload is **unauthenticated by construction**: there is no header, so there is no tag and no
checksum, and nothing can be verified before the bytes are decoded. Use it only on a channel that
authenticates itself — a local IPC endpoint, a mutually authenticated session, a file the process alone
controls. For that reason `Build()` refuses `RequireEncryption` or `RequireChecksum` together with
`WithVersion(0)` or `AllowV0Fallback`.

### Where a V0 payload ends

A V0 payload declares no length, so a reader finds the end by decoding the root value.

| Source | Behaviour |
|---|---|
| span or sequence | exactly one payload; bytes after it are a `BinaryFormatException` |
| span or sequence, with a bytes-consumed form | reading stops at the end of the root value and reports where, so payloads placed back to back are read one after another |
| seekable `Stream` | read ahead within the limits, then the position is put back at the end of the root value, so a payload may be embedded in a larger stream |
| non-seekable `Stream` | `NotSupportedException`: there is no length to read to |
| any asynchronous read | `NotSupportedException` |

```csharp
using ViShap.Viper;

var v0 = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithVersion(0).AllowV0Fallback().Build());

byte[] both = [.. v0.Serialize(new Ping { Id = 1 }), .. v0.Serialize(new Ping { Id = 2 })];

ReadOnlySpan<byte> rest = both;
while (!rest.IsEmpty)
{
    Ping? ping = v0.Deserialize<Ping>(rest, out int consumed);
    Console.WriteLine(ping!.Id);
    rest = rest[consumed..];
}

public sealed class Ping
{
    public int Id { get; set; }
}
```

Asynchronous V0 *writes* are allowed; only reading is synchronous. A keyed contract writes under V0 to
any destination, seekable or not, and a write that fails leaves nothing in the destination, exactly as
under V1.

A keyed field's declared length is checked against the bytes that can still arrive before anything is
allocated. For a V0 payload embedded in a larger stream, those bytes are the remainder of the stream
rather than of a declared payload, so that check is weaker than under V1; the limits still bound what
a field may consume.

### Reading V0 from a pipe

> V0 carries neither a magic number nor a length: it is a codec for protocols that already frame their
> messages — a length prefix, a message type, a channel. The protocol knows where a message ends, so
> the caller already holds one message's bytes and reads them synchronously. Waiting asynchronously
> is for a reader that does not know where the message ends; with V0 the protocol knows, not Viper.

```csharp
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using ViShap.Viper;

var compact = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithVersion(0).AllowV0Fallback().Build());

// the sending side of the protocol: a 4-byte little-endian length, then the V0 payload
var pipe = new Pipe();
foreach (int id in new[] { 1, 2, 3 })
{
    byte[] payload = compact.Serialize(new Order { Id = id });
    BinaryPrimitives.WriteInt32LittleEndian(pipe.Writer.GetSpan(4), payload.Length);
    pipe.Writer.Advance(4);
    pipe.Writer.Write(payload);
}
await pipe.Writer.CompleteAsync();

// the receiving side: the protocol cuts one message out of the pipe, Viper decodes it
while (true)
{
    ReadResult read = await pipe.Reader.ReadAsync();
    ReadOnlySequence<byte> buffer = read.Buffer;

    while (TryReadFrame(ref buffer, out ReadOnlySequence<byte> frame))
    {
        Order? message = compact.Deserialize<Order>(frame);   // synchronous: the frame is in memory
        Console.WriteLine(message!.Id);
    }

    pipe.Reader.AdvanceTo(buffer.Start, buffer.End);
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

public sealed class Order
{
    public int Id { get; set; }
}
```

A V1 frame, by contrast, carries its own length, so Viper can await it from a socket:

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();
var networkStream = new MemoryStream();
serializer.Serialize(networkStream, new Order { Id = 7 });
networkStream.Position = 0;

// V1 from a socket: Viper knows the frame boundary — the header carries the length
Order? order = await serializer.DeserializeAsync<Order>(networkStream);
Console.WriteLine(order!.Id);

public sealed class Order
{
    public int Id { get; set; }
}
```

## How a reader chooses

Routing looks at the first bytes the source has delivered. The magic number followed by a complete
version selects V1, and nothing is read twice, so no source is asked to rewind. Bytes that are not
identified that way are read as V0 only when `AllowV0Fallback` is set and are otherwise a
`BinaryFormatException`. A recognized version this build does not support is a
`BinaryFormatNotSupportedException`.
