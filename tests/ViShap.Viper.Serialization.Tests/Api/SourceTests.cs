using System.IO.Pipelines;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins SRC-01…SRC-10: every kind of source a caller can hand the serializer — a span, a sequence in
/// one segment or many, a stream that can seek and one that cannot, a stream or a pipe awaited — reads
/// the same value from the same frame, and a source that delivers bytes over time is asked for exactly
/// one frame and nothing after it.
/// </summary>
public class SourceTests
{
    private static readonly byte[] Key = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    private static Person Value => new() { Name = "Ada", Age = 36 };

    public static TheoryData<string> Profiles => ["P0", "P6", "P7"];

    /// <summary>The profiles that write version 1 frames, which declare their length.</summary>
    public static TheoryData<string> FramedProfiles => ["P0", "P6"];

    private static BinarySerializer Profile(string name) => name switch
    {
        "P0" => new BinarySerializer(),
        "P6" => new BinarySerializer(BinarySerializerOptions.Configure()
            .WithCompression(new BrotliCompression())
            .WithChecksum(new Crc32Checksum())
            .WithEncryption(new Aes256GcmEncryption(), Key, keyId: "primary")
            .Build()),
        "P7" => new BinarySerializer(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static void AssertIsTheValue(Person? restored)
    {
        Assert.NotNull(restored);
        Assert.Equal("Ada", restored.Name);
        Assert.Equal(36, restored.Age);
    }

    // --- SRC-01…SRC-07: one value from every kind of source ---------------------------------------

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Span_ReadsTheValue(string profile)
    {
        var serializer = Profile(profile);

        AssertIsTheValue(serializer.Deserialize<Person>(serializer.Serialize(Value).AsSpan()));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void SingleSegmentSequence_ReadsTheValue(string profile)
    {
        var serializer = Profile(profile);

        AssertIsTheValue(serializer.Deserialize<Person>(Sequences.Of(serializer.Serialize(Value))));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void MultiSegmentSequence_ReadsTheValue_WhereverTheSegmentsAreCut(string profile)
    {
        var serializer = Profile(profile);
        byte[] frame = serializer.Serialize(Value);

        for (int cut = 1; cut < frame.Length; cut++)
            AssertIsTheValue(serializer.Deserialize<Person>(Sequences.Of(frame[..cut], frame[cut..])));

        AssertIsTheValue(serializer.Deserialize<Person>(Sequences.Of([.. frame.Select(b => new[] { b })])));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void SeekableStream_ReadsTheValue(string profile)
    {
        var serializer = Profile(profile);

        AssertIsTheValue(serializer.Deserialize<Person>(new MemoryStream(serializer.Serialize(Value), writable: false)));
    }

    [Theory]
    [MemberData(nameof(FramedProfiles))]
    public void NonSeekableStream_ReadsTheValue(string profile)
    {
        var serializer = Profile(profile);

        AssertIsTheValue(serializer.Deserialize<Person>(new NonSeekableStream(serializer.Serialize(Value))));
    }

    [Theory]
    [MemberData(nameof(FramedProfiles))]
    public async Task PipeReader_ReadsTheValue(string profile)
    {
        var serializer = Profile(profile);

        AssertIsTheValue(await serializer.DeserializeAsync<Person>(new ChunkedPipeReader(serializer.Serialize(Value), chunkSize: 16)));
    }

    [Theory]
    [MemberData(nameof(FramedProfiles))]
    public async Task StreamAwaited_ReadsTheValue(string profile)
    {
        var serializer = Profile(profile);

        AssertIsTheValue(await serializer.DeserializeAsync<Person>(new NonSeekableStream(serializer.Serialize(Value))));
    }

    [Theory]
    [MemberData(nameof(FramedProfiles))]
    public async Task EverySource_ReadsTheFrameWrittenThroughARealPipe(string profile)
    {
        var serializer = Profile(profile);
        var pipe = new Pipe();

        await serializer.SerializeAsync(pipe.Writer, Value);
        var restored = await serializer.DeserializeAsync<Person>(pipe.Reader);

        AssertIsTheValue(restored);
    }

    // --- SRC-08: exactly one version 1 frame is taken, and nothing after it ------------------------

    /// <summary>
    /// Frames whose headers exercise every variable part: no service record, a key id, custom
    /// algorithm names, and a checksum.
    /// </summary>
    public static TheoryData<string> Headers => ["plain", "protected", "custom names", "long key id"];

    private static BinarySerializer WithHeader(string name) => name switch
    {
        "plain" => new BinarySerializer(),
        "protected" => Profile("P6"),
        "custom names" => new BinarySerializer(BinarySerializerOptions.Configure()
            .WithCompression(new IdentityCompression())
            .WithChecksum(new Sum8())
            .WithEncryption(new UnauthenticatedCipher(), Key, keyId: "k")
            .RegisterCustomCompression(IdentityCompression.RegisteredName, static () => new IdentityCompression())
            .RegisterCustomChecksum(Sum8.RegisteredName, static () => new Sum8())
            .RegisterCustomEncryption(UnauthenticatedCipher.RegisteredName, static () => new UnauthenticatedCipher())
            .Build()),
        "long key id" => new BinarySerializer(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256GcmEncryption(), Key, keyId: new string('k', 200))
            .Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void NonSeekableStream_TakesExactlyOneFrame(string header)
    {
        var serializer = WithHeader(header);
        byte[] frame = serializer.Serialize(Value);
        byte[] next = serializer.Serialize(new Person { Name = "Bob", Age = 41 });
        var source = new FrameBoundStream([.. frame, .. next], boundary: frame.Length);

        AssertIsTheValue(serializer.Deserialize<Person>(source));
        Assert.Equal(frame.Length, source.Taken);
    }

    [Theory]
    [MemberData(nameof(Headers))]
    public async Task StreamAwaited_TakesExactlyOneFrame(string header)
    {
        var serializer = WithHeader(header);
        byte[] frame = serializer.Serialize(Value);
        byte[] next = serializer.Serialize(new Person { Name = "Bob", Age = 41 });
        var source = new FrameBoundStream([.. frame, .. next], boundary: frame.Length);

        AssertIsTheValue(await serializer.DeserializeAsync<Person>(source));
        Assert.Equal(frame.Length, source.Taken);
    }

    [Theory]
    [MemberData(nameof(Headers))]
    public async Task PipeReader_ConsumesExactlyOneFrame(string header)
    {
        var serializer = WithHeader(header);
        byte[] frame = serializer.Serialize(Value);
        byte[] next = serializer.Serialize(new Person { Name = "Bob", Age = 41 });
        var pipe = new ChunkedPipeReader([.. frame, .. next], chunkSize: 3);

        AssertIsTheValue(await serializer.DeserializeAsync<Person>(pipe));
        Assert.Equal(frame.Length, pipe.Consumed);
    }

    [Fact]
    public void NonSeekableStream_TakesExactlyOneFrame_OfEveryPayloadLength()
    {
        var serializer = new BinarySerializer();

        foreach (int length in new[] { 0, 1, 7, 255, 4_096, 70_000 })
        {
            byte[] frame = serializer.Serialize(new string('x', length));
            var source = new FrameBoundStream([.. frame, 0xEE, 0xEE], boundary: frame.Length);

            Assert.Equal(length, serializer.Deserialize<string>(source)!.Length);
            Assert.Equal(frame.Length, source.Taken);
        }
    }

    // --- SRC-09: a frame arriving a byte at a time -----------------------------------------------

    [Theory]
    [MemberData(nameof(FramedProfiles))]
    public async Task AFrameArrivingOneByteAtATime_IsRead(string profile)
    {
        var serializer = Profile(profile);
        byte[] frame = serializer.Serialize(Value);

        AssertIsTheValue(serializer.Deserialize<Person>(new PartialReadStream(frame, chunkSize: 1)));
        AssertIsTheValue(await serializer.DeserializeAsync<Person>(new ChunkedPipeReader(frame, chunkSize: 1)));
    }

    // --- SRC-10: what a source that cannot tell its length costs follows what it delivers ---------

    [Fact]
    public void FrameBuffer_ForADeclaredLengthNotKnownToArrive_GrowsAsBytesArrive()
    {
        var buffer = new Pipeline.FrameBuffer();
        try
        {
            const int declared = 64 * 1024 * 1024;

            var first = buffer.Free(declared, backed: false);
            Assert.True(first.Length <= 512, $"The first read offered {first.Length} bytes.");

            buffer.Advance(first.Length);
            var second = buffer.Free(declared, backed: false);
            Assert.True(buffer.Length + second.Length <= 4 * first.Length);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void FrameBuffer_ForALengthTheSourceHolds_OffersItAtOnce()
    {
        var buffer = new Pipeline.FrameBuffer();
        try
        {
            Assert.Equal(100_000, buffer.Free(100_000, backed: true).Length);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void NonSeekableStream_DeclaringMoreThanItDelivers_IsMalformed()
    {
        byte[] frame = new BinarySerializer().Serialize(Value);
        byte[] declaringMore = Wire.WithLengths(frame, onDiskLength: 32 * 1024 * 1024);

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(new NonSeekableStream(declaringMore)));
    }
}
