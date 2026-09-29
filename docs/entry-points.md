# Entry points

`BinarySerializer` writes to and reads from every common place bytes live. The overloads come in
families by where the bytes are; within a family the rules are the same.

| The bytes are in | Write | Read |
|---|---|---|
| memory you own | `Serialize<T>(T)` returns a `byte[]`; `SerializePooled<T>(T)` returns a pooled `PooledPayload`; `Serialize<T>(IBufferWriter<byte>, T)` appends to a buffer writer | `Deserialize<T>(ReadOnlySpan<byte>)`; `Deserialize<T>(ReadOnlySequence<byte>)` |
| a `Stream` | `Serialize<T>(Stream, T)`; `SerializeAsync<T>(Stream, T, CancellationToken)` | `Deserialize<T>(Stream)`; `DeserializeAsync<T>(Stream, CancellationToken)` |
| a pipe | `SerializeAsync<T>(PipeWriter, T, CancellationToken)` | `DeserializeAsync<T>(PipeReader, CancellationToken)` |
| an object you already hold | — | `Populate<T>` over a span, a sequence, a stream or a pipe |
| a sequence of frames | — | `DeserializeAsyncEnumerable<T>` over a stream or a pipe |

Every generic entry point that encodes or decodes your type builds its codec by reflection; see
[Trimming and native AOT](aot.md).

## Writing to memory

```csharp
using System.Buffers;
using ViShap.Viper;

var serializer = new BinarySerializer();
var message = new Message { Id = 7, Text = "hello" };

byte[] array = serializer.Serialize(message);

var buffer = new ArrayBufferWriter<byte>();
serializer.Serialize(buffer, message);

using PooledPayload pooled = serializer.SerializePooled(message);

Console.WriteLine(array.AsSpan().SequenceEqual(buffer.WrittenSpan));
Console.WriteLine(array.AsSpan().SequenceEqual(pooled.Span));

public sealed class Message
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}
```

`Serialize` into an `IBufferWriter<byte>` advances the writer and does not flush it. The frame is copied
in as many spans as the writer hands out. A writer that hands out an empty span, which the
`IBufferWriter<T>` contract does not allow, makes the write fail with `BinaryStreamException` instead
of looping.

`SerializePooled` returns a `PooledPayload` that holds the bytes in an array rented from
`ArrayPool<byte>.Shared`. `Memory` and `Span` are valid until `Dispose`, which clears the bytes and
returns the array to the pool. `Dispose` may be called more than once, and from several threads; reading
`Memory` or `Span` afterwards throws `ObjectDisposedException`. It is a class, not a struct, so a copy
can never be a second owner of the same array.

## Reading from memory

```csharp
using System.Buffers;
using ViShap.Viper;

var serializer = new BinarySerializer();
byte[] bytes = serializer.Serialize(new Message { Id = 7, Text = "hello" });

Message? fromSpan = serializer.Deserialize<Message>(bytes);
Message? fromSequence = serializer.Deserialize<Message>(new ReadOnlySequence<byte>(bytes));

Console.WriteLine(fromSpan!.Text);
Console.WriteLine(fromSequence!.Id);

public sealed class Message
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}
```

There is no `byte[]` overload: an array converts to `ReadOnlySpan<byte>`, so `Deserialize<T>(bytes)`
compiles against the span form. A `null` array is an empty span.

Three rules hold for every read and `Populate`:

- **An empty input is not a payload.** No format encodes a value in zero bytes, not even a null root,
  which takes one byte. An empty span, an empty sequence, a stream that ends before its first byte and a
  pipe that completes empty are all `BinaryFormatException`; nothing is read as `default(T)`.
- **Without a bytes-consumed form, a span or a sequence is exactly one frame.** Bytes after it are a
  `BinaryFormatException`.
- **A stream or a pipe is read exactly as far as one frame extends.** A V1 frame declares its length,
  so it is read to the end and no byte past it is taken; the stream can be non-seekable. Reading a V0
  payload from a stream is different and is described in [Formats](formats.md).

## Frames placed back to back

The bytes-consumed forms read one frame from the front of a larger input and report where it ended:
`out int bytesConsumed` for a span, `out SequencePosition consumed` for a sequence — the position that
`PipeReader.AdvanceTo` takes.

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

byte[] both =
[
    .. serializer.Serialize(new Message { Id = 1, Text = "one" }),
    .. serializer.Serialize(new Message { Id = 2, Text = "two" })
];

ReadOnlySpan<byte> rest = both;
while (!rest.IsEmpty)
{
    Message? message = serializer.Deserialize<Message>(rest, out int consumed);
    Console.WriteLine(message!.Text);
    rest = rest[consumed..];
}

public sealed class Message
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}
```

## Streams

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

using var stream = new MemoryStream();
serializer.Serialize(stream, new Message { Id = 1, Text = "one" });
serializer.Serialize(stream, new Message { Id = 2, Text = "two" });

stream.Position = 0;
Message? first = serializer.Deserialize<Message>(stream);
Message? second = serializer.Deserialize<Message>(stream);

Console.WriteLine(first!.Id + " " + second!.Id);

public sealed class Message
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}
```

- A stream is never disposed and never rewound by the serializer. A write appends where the stream is
  positioned, and a successful read leaves the stream positioned at the end of the frame, so frames
  written one after another are read one after another.
- Writing builds the whole frame in the serializer's own buffers before the first byte reaches the
  stream, so no stream is asked to seek, report its length or position, and a failure of the value —
  an unsupported type, a limit, an algorithm failure — leaves the stream untouched. Only a failure of
  the stream itself, while the finished bytes are being copied, can leave part of a frame behind.
- An `IOException` from the stream is a `BinaryStreamException` with the original as its inner
  exception. Anything else the stream raises, such as `NotSupportedException` for a stream that cannot
  read, is not wrapped.
- After a failed or cancelled read the stream position is undefined.

## Asynchronous reads and writes

```csharp
using System.IO.Pipelines;
using ViShap.Viper;

var serializer = new BinarySerializer();
var pipe = new Pipe();

await serializer.SerializeAsync(pipe.Writer, new Message { Id = 1, Text = "one" });
await serializer.SerializeAsync(pipe.Writer, new Message { Id = 2, Text = "two" });
await pipe.Writer.CompleteAsync();

Message? first = await serializer.DeserializeAsync<Message>(pipe.Reader);
Message? second = await serializer.DeserializeAsync<Message>(pipe.Reader);

Console.WriteLine(first!.Text + " " + second!.Text);

public sealed class Message
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}
```

The asynchronous methods wait only for bytes. An asynchronous read awaits one whole frame — the header
says how long it is — and then decodes it synchronously; an asynchronous write builds the frame
synchronously and awaits only the output. Decoding a frame already in memory is bounded by the limits and
is not interrupted by cancellation.

| | Behaviour |
|---|---|
| cancellation token | observed while bytes are awaited |
| `PipeReader` read | exactly the frame is consumed; on cancellation or failure nothing is consumed |
| `Stream` read | exactly the frame is taken; taken bytes are not given back, so after a cancellation or a failure the stream is unusable for further framing |
| write | a cancellation before the output starts leaves nothing; one during the write or the flush may leave part of a frame, or a frame written but not flushed — that is a property of the destination |
| cancellation | `OperationCanceledException`, which is outside the `BinarySerializerException` hierarchy |

A stream is flushed after a write; a `PipeWriter` is flushed and never completed. A pending pipe read
cancelled with `CancelPendingRead` is reported as `OperationCanceledException` and consumes nothing.

**Asynchronous reads take V1 frames only.** A V0 payload — the headerless format — has no length for an
asynchronous read to await, and reading one asynchronously is `NotSupportedException`. The reason and
the way to read V0 from a pipe are in [Formats](formats.md#reading-v0-from-a-pipe).

## Streams of frames

`DeserializeAsyncEnumerable<T>` reads V1 frames from a stream or a pipe until the source ends.

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

using var stream = new MemoryStream();
for (int id = 1; id <= 3; id++)
    serializer.Serialize(stream, new Message { Id = id, Text = "message " + id });
stream.Position = 0;

await foreach (Message? message in serializer.DeserializeAsyncEnumerable<Message>(stream))
    Console.WriteLine(message!.Text);

public sealed class Message
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}
```

Each frame is its own operation with its own limits, so a stream of frames has no length bound while
every frame stays bounded: limits apply per frame, never per connection. The source ending exactly
between two frames completes the enumeration; ending inside a frame is a `BinaryFormatException`, raised
after the complete frames before it have been yielded. A message above 2 GiB, or one value that never
ends, is out of scope: send large or endless data as many frames.

## Populating an existing object

`Populate` reads a payload into an instance you already hold instead of creating one.

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();
byte[] bytes = serializer.Serialize(new Settings { Theme = "dark", FontSize = 14 });

var target = new Settings { Theme = "light", FontSize = 10 };
serializer.Populate(bytes, target);

Console.WriteLine(target.Theme + " " + target.FontSize);

public sealed class Settings
{
    public string Theme { get; set; } = "";
    public int FontSize { get; set; }
}
```

- It is defined for classes that are written member by member. A type with a dedicated encoding — a
  collection, a dictionary, an array, a string — is a `BinaryTypeException`. For a struct, assign the
  result of `Deserialize<T>`.
- Only the root object is populated: its members are overwritten with the payload's values, and every
  object below it is created afresh. For a [keyed contract](contracts.md#keyed-layout), a member whose key
  the payload does not carry keeps its current value.
- A null root, or a root that is a back reference, is a `BinaryFormatException`: neither is an
  instance's member layout.
- With a [`[BinaryUnion]`](polymorphism.md) payload, a runtime type that differs from the target's is a
  `BinaryTypeException`; an instance cannot be reused as another type.
- An empty input is a `BinaryFormatException` and leaves the target untouched. A `null` target is
  `ArgumentNullException`.
