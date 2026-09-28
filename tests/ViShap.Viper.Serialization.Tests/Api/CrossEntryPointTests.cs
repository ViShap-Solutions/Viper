using System.Buffers;
using System.IO.Pipelines;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins XEP-01, XEP-02 and XEP-04…XEP-08: for one logical value under one configuration every entry
/// point of section 3.1 must agree — the same bytes from every write, the same value from every read,
/// and a payload one entry point wrote read by any other. An entry point that quietly framed a value
/// differently would be a second wire format hiding behind a convenience overload.
/// </summary>
/// <remarks>
/// The suite names no profile. It is run under the default V1 frame, under the full V1 envelope, and
/// under the headerless format, by the three subclasses at the end of this file.
/// </remarks>
public abstract class CrossEntryPoint
{
    /// <summary>The configuration of the profile under test.</summary>
    protected abstract BinarySerializerOptions Options { get; }

    /// <summary>
    /// Whether the profile writes version 1 frames, which carry their length and so can be awaited
    /// and read from a stream that cannot seek.
    /// </summary>
    protected virtual bool DeclaresLength => true;

    /// <summary>
    /// Whether two writes of one value produce identical bytes. Authenticated encryption draws a
    /// fresh nonce per message, so an encrypted profile compares semantics instead.
    /// </summary>
    protected virtual bool BytesAreDeterministic => true;

    private BinarySerializer Serializer => new(Options);

    private static Person Value => new() { Name = "Alice", Age = 30 };

    private static void AssertIsTheValue(Person? restored)
    {
        Assert.NotNull(restored);
        Assert.Equal("Alice", restored.Name);
        Assert.Equal(30, restored.Age);
    }

    /// <summary>What every write entry point produced for <see cref="Value"/>.</summary>
    private async Task<byte[][]> EveryWrite()
    {
        var serializer = Serializer;

        using var stream = new MemoryStream();
        serializer.Serialize(stream, Value);

        var bufferWriter = new ArrayBufferWriter<byte>(initialCapacity: 1);
        serializer.Serialize(bufferWriter, Value);

        using var pooled = serializer.SerializePooled(Value);

        using var asyncStream = new MemoryStream();
        await serializer.SerializeAsync(asyncStream, Value);

        var pipe = new Pipe();
        await serializer.SerializeAsync(pipe.Writer, Value);
        await pipe.Writer.CompleteAsync();
        var piped = await pipe.Reader.ReadAsync();

        return
        [
            serializer.Serialize(Value),
            stream.ToArray(),
            bufferWriter.WrittenSpan.ToArray(),
            pooled.Span.ToArray(),
            asyncStream.ToArray(),
            piped.Buffer.ToArray()
        ];
    }

    // --- XEP-01, XEP-02, XEP-08: every write entry point ---------------------------------------------

    [Fact]
    public async Task EveryWriteEntryPoint_WritesTheSamePayload()
    {
        byte[][] writes = await EveryWrite();

        foreach (byte[] written in writes)
        {
            if (BytesAreDeterministic)
                Assert.Equal(writes[0], written);

            AssertIsTheValue(Serializer.Deserialize<Person>(written));
        }
    }

    // --- XEP-01, XEP-02, XEP-08: every read entry point ----------------------------------------------

    [Fact]
    public async Task EveryReadEntryPoint_ReadsTheSameValue()
    {
        var serializer = Serializer;
        byte[] payload = serializer.Serialize(Value);

        AssertIsTheValue(serializer.Deserialize<Person>(payload));
        AssertIsTheValue(serializer.Deserialize<Person>(payload, out int bytesConsumed));
        Assert.Equal(payload.Length, bytesConsumed);
        AssertIsTheValue(serializer.Deserialize<Person>(Sequences.Of(payload)));
        AssertIsTheValue(serializer.Deserialize<Person>(Sequences.Of(payload[..1], payload[1..9], payload[9..])));
        AssertIsTheValue(serializer.Deserialize<Person>(Sequences.Of(payload[..5], payload[5..]), out SequencePosition _));
        AssertIsTheValue(serializer.Deserialize<Person>(new MemoryStream(payload, writable: false)));
        AssertIsTheValue(serializer.Deserialize<Person>(new PartialReadStream(payload, chunkSize: 3)));

        if (DeclaresLength)
        {
            AssertIsTheValue(serializer.Deserialize<Person>(new NonSeekableStream(payload)));
            AssertIsTheValue(await serializer.DeserializeAsync<Person>(new NonSeekableStream(payload)));
            AssertIsTheValue(await serializer.DeserializeAsync<Person>(new ChunkedPipeReader(payload, chunkSize: 4)));

            await foreach (var value in serializer.DeserializeAsyncEnumerable<Person>(new MemoryStream(payload)))
                AssertIsTheValue(value);
        }
        else
        {
            // A headerless payload carries no length, so only a synchronous read of bytes the
            // caller has delimited, or of a stream that can seek, can find where it ends.
            Assert.Throws<NotSupportedException>(() => serializer.Deserialize<Person>(new NonSeekableStream(payload)));
            await Assert.ThrowsAsync<NotSupportedException>(
                () => serializer.DeserializeAsync<Person>(new MemoryStream(payload)).AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(
                () => serializer.DeserializeAsync<Person>(new ChunkedPipeReader(payload, chunkSize: 4)).AsTask());
        }
    }

    [Fact]
    public async Task EveryReadEntryPoint_ReadsWhatEveryWriteEntryPointWrote()
    {
        var serializer = Serializer;

        foreach (byte[] written in await EveryWrite())
        {
            AssertIsTheValue(serializer.Deserialize<Person>(written));
            AssertIsTheValue(serializer.Deserialize<Person>(Sequences.Of(written[..3], written[3..])));
            AssertIsTheValue(serializer.Deserialize<Person>(new MemoryStream(written, writable: false)));

            if (DeclaresLength)
                AssertIsTheValue(await serializer.DeserializeAsync<Person>(new ChunkedPipeReader(written, chunkSize: 7)));
        }
    }

    // --- XEP-04, XEP-08: every populate entry point ----------------------------------------------

    [Fact]
    public async Task EveryPopulateEntryPoint_FillsTheTargetItWasGiven()
    {
        var serializer = Serializer;
        byte[] payload = serializer.Serialize(Value);
        var targets = new List<Person>();

        Person Target()
        {
            var target = new Person();
            targets.Add(target);
            return target;
        }

        serializer.Populate(payload, Target());
        serializer.Populate(payload, Target(), out int _);
        serializer.Populate(Sequences.Of(payload[..6], payload[6..]), Target());
        serializer.Populate(Sequences.Of(payload), Target(), out SequencePosition _);
        serializer.Populate(new MemoryStream(payload, writable: false), Target());

        if (DeclaresLength)
        {
            await serializer.PopulateAsync(new NonSeekableStream(payload), Target());
            await serializer.PopulateAsync(new ChunkedPipeReader(payload, chunkSize: 2), Target());
        }
        else
        {
            await Assert.ThrowsAsync<NotSupportedException>(
                () => serializer.PopulateAsync(new MemoryStream(payload), new Person()).AsTask());
        }

        Assert.All(targets, AssertIsTheValue);
    }

    // --- XEP-05: a value type read through the ordinary overload is the populate form --------------

    [Fact]
    public void ValueType_ReadThroughEveryEntryPoint_Agrees()
    {
        var serializer = Serializer;
        byte[] payload = serializer.Serialize(new PointStruct { X = 3, Y = 4 });

        var fromSpan = serializer.Deserialize<PointStruct>(payload);
        var fromSequence = serializer.Deserialize<PointStruct>(Sequences.Of(payload[..2], payload[2..]));
        var fromStream = serializer.Deserialize<PointStruct>(new MemoryStream(payload, writable: false));

        Assert.Equal(new PointStruct { X = 3, Y = 4 }, fromSpan);
        Assert.Equal(fromSpan, fromSequence);
        Assert.Equal(fromSpan, fromStream);
    }

    // --- every entry point leaves the caller's stream open --------------------------------------

    [Fact]
    public async Task EveryStreamEntryPoint_LeavesTheCallersStreamOpen()
    {
        var serializer = Serializer;

        using var destination = new TrackingStream();
        serializer.Serialize(destination, Value);
        await serializer.SerializeAsync(destination, Value);
        Assert.False(destination.Disposed);

        using var source = new TrackingStream(serializer.Serialize(Value));
        serializer.Deserialize<Person>(source);
        Assert.False(source.Disposed);

        using var populated = new TrackingStream(serializer.Serialize(Value));
        serializer.Populate(populated, new Person());
        Assert.False(populated.Disposed);

        if (DeclaresLength)
        {
            using var awaited = new TrackingStream(serializer.Serialize(Value));
            await serializer.DeserializeAsync<Person>(awaited);
            Assert.False(awaited.Disposed);
        }
    }
}

/// <summary>XEP-01, XEP-02, XEP-04, XEP-05 and XEP-08 under profile P0, the default V1 frame.</summary>
public sealed class DefaultCrossEntryPointTests : CrossEntryPoint
{
    protected override BinarySerializerOptions Options { get; } = BinarySerializerOptions.Default;
}

/// <summary>
/// XEP-06 and XEP-08, profile P6: the same agreement under the full V1 envelope. The nonce makes two
/// writes of one value differ, so parity here is parity of what comes back, never of ciphertext bytes
/// (section 22.6).
/// </summary>
public sealed class ProtectedCrossEntryPointTests : CrossEntryPoint
{
    private static readonly byte[] Material =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    protected override BinarySerializerOptions Options { get; } = BinarySerializerOptions.Configure()
        .WithCompression(new BrotliCompression())
        .WithChecksum(new Crc32Checksum())
        .WithEncryption(new Aes256GcmEncryption(), Material, keyId: "primary")
        .Build();

    protected override bool BytesAreDeterministic => false;
}

/// <summary>
/// XEP-07 and XEP-08, profile P7: the same agreement under the headerless format, for every entry
/// point V0 supports. The entry points that need a declared length are the exception, and the suite
/// asserts what they do instead of skipping them (section 10.2).
/// </summary>
public sealed class HeaderlessCrossEntryPointTests : CrossEntryPoint
{
    protected override BinarySerializerOptions Options { get; } = BinarySerializerOptions.Configure()
        .WithVersion(0)
        .AllowV0Fallback()
        .Build();

    protected override bool DeclaresLength => false;
}
