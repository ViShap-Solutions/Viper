using System.Buffers;
using System.IO.Compression;
using ViShap.Viper.Compression;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins CMP-01…CMP-07, CMP-09…CMP-14, CMP-16, CMP-17 and LIM-45: a compressed payload round trips,
/// decompression produces exactly the declared length so a payload cannot hide part of its own content, and every phase
/// size is decided by the pipeline before the algorithm is consulted.
/// </summary>
public class CompressionTests
{
    private static BinarySerializer With(ICompressionAlgorithm compression) =>
        new(BinarySerializerOptions.Configure().WithCompression(compression).Build());

    private static BinarySerializer With(ICompressionAlgorithm compression, SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure()
            .WithCompression(compression)
            .WithLimits(limits)
            .Build());

    private static Person Compressible() => new() { Name = new string('x', 5_000), Age = 7 };

    /// <summary>
    /// A body no codec shrinks to a handful of bytes, so a ceiling of a few bytes is reached by what
    /// compression produced rather than by what it was given.
    /// </summary>
    private static byte[] Incompressible() =>
        [.. Enumerable.Range(0, 512).Select(value => (byte)(value * 37 + 11))];

    [Fact]
    public void Deserialize_DeflatePayload_RoundTrips()
    {
        var serializer = With(new DeflateCompression());
        var source = Compressible();

        Assert.Equivalent(source, serializer.Deserialize<Person>(serializer.Serialize(source)));
    }

    [Fact]
    public void Deserialize_BrotliPayload_RoundTrips()
    {
        var serializer = With(new BrotliCompression());
        var source = Compressible();

        Assert.Equivalent(source, serializer.Deserialize<Person>(serializer.Serialize(source)));
    }

    [Fact]
    public void Deserialize_NoCompressionPayload_RoundTrips()
    {
        var serializer = With(new NoCompression());
        var source = Compressible();

        Assert.Equivalent(source, serializer.Deserialize<Person>(serializer.Serialize(source)));
    }

    [Fact]
    public void Serialize_CompressedPayload_IsSmallerThanThePlainOne()
    {
        var source = Compressible();

        int compressed = With(new DeflateCompression()).Serialize(source).Length;
        int plain = new BinarySerializer().Serialize(source).Length;

        Assert.True(compressed < plain, $"compressed {compressed} is not smaller than plain {plain}");
    }

    [Fact]
    public void Decompress_OutputLongerThanDeclared_ThrowsFormat()
    {
        var deflate = new DeflateCompression();
        byte[] compressed = Compress(deflate, new byte[1024]);

        Assert.Throws<BinaryFormatException>(
            () => deflate.Decompress(compressed, new ArrayBufferWriter<byte>(), 4));
    }

    [Fact]
    public void Deserialize_HeaderDeclaringMoreUncompressedBytesThanTheStreamProduces_ThrowsFormat()
    {
        // The exactness rule lives on the phase boundary, not in the algorithm: the algorithm refuses
        // to overrun the buffer, and the service refuses a stream that underfills it.
        var serializer = With(new DeflateCompression());
        byte[] payload = serializer.Serialize(Compressible());

        int declared = BitConverter.ToInt32(payload, Wire.UncompressedLengthOffset);
        byte[] tampered = Mutate.SetInt32(payload, Wire.UncompressedLengthOffset, declared + 64);

        AssertEx.Throws<BinaryFormatException>(
            "expected", () => serializer.Deserialize<Person>(tampered));
    }

    [Fact]
    public void Deserialize_CorruptedDeflateBody_ThrowsFormat()
    {
        var serializer = With(new DeflateCompression());
        byte[] payload = serializer.Serialize(Compressible());

        byte[] tampered = Mutate.FlipByte(payload, Wire.PlainHeaderLength + 2);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(tampered));
    }

    [Fact]
    public void Deserialize_CorruptedBrotliBody_ThrowsFormat()
    {
        var serializer = With(new BrotliCompression());
        byte[] payload = serializer.Serialize(Compressible());

        byte[] tampered = Mutate.FlipByte(payload, Wire.PlainHeaderLength + 2);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(tampered));
    }

    // --- CMP-01: no compression still answers to the phase limits ---------------------------------

    [Fact]
    public void Serialize_NoCompressionAboveTheCompressedPhaseLimit_ThrowsLimit()
    {
        var serializer = With(
            new NoCompression(), SerializationLimits.Default with { MaxCompressedBytes = 16 });

        AssertEx.Throws<BinaryLimitException>(
            "Compressed payload length", () => serializer.Serialize(new string('x', 512)));
    }

    [Fact]
    public void Deserialize_NoCompressionDeclaringMoreThanTheCompressedPhaseLimit_ThrowsLimit()
    {
        var serializer = With(
            new NoCompression(), SerializationLimits.Default with { MaxCompressedBytes = 16 });

        AssertEx.Throws<BinaryLimitException>(
            "CompressedLength", () => serializer.Deserialize<int>(Wire.Frame(new byte[64])));
    }

    // --- CMP-06: raw input above MaxPayloadBytes --------------------------------------------------

    [Fact]
    public void Serialize_RawPayloadAboveMaxPayloadBytes_ThrowsLimitBeforeCompressing()
    {
        var compression = new IdentityCompression();
        var serializer = With(compression, SerializationLimits.Default with { MaxPayloadBytes = 16 });

        AssertEx.Throws<BinaryLimitException>(
            "payload byte budget", () => serializer.Serialize(new string('x', 512)));

        Assert.Equal(0, compression.CompressCalls);
    }

    // --- CMP-07: compressed output above MaxCompressedBytes ---------------------------------------

    [Fact]
    public void Serialize_CompressedOutputAboveMaxCompressedBytes_ThrowsLimit()
    {
        var serializer = With(
            new DeflateCompression(), SerializationLimits.Default with { MaxCompressedBytes = 16 });

        AssertEx.Throws<BinaryLimitException>(
            "could not fit within the configured maximum",
            () => serializer.Serialize(Incompressible()));
    }

    [Fact]
    public void Serialize_CompressedOutputWithinMaxCompressedBytes_Succeeds()
    {
        // The ceiling bounds what compression produced, not what it was given: a payload far above it
        // is legal as long as its compressed form fits.
        var serializer = With(
            new DeflateCompression(), SerializationLimits.Default with { MaxCompressedBytes = 256 });
        var source = Compressible();

        Assert.Equivalent(source, serializer.Deserialize<Person>(serializer.Serialize(source)));
    }

    // --- CMP-09, CMP-12: the declared output size is refused before anything is built --------------

    [Fact]
    public void Deserialize_ADeclaredUncompressedLengthAboveMaxPayloadBytes_ThrowsLimitBeforeAllocating()
    {
        var serializer = Custom(new IdentityCompression());

        AssertEx.Throws<BinaryLimitException>(
            "UncompressedLength", () => serializer.Deserialize<int>(Bomb()));

        AssertEx.AllocatesLessThan(1024 * 1024, () => serializer.Deserialize<int>(Bomb()));
    }

    [Fact]
    public void Deserialize_ADeclaredUncompressedLengthAboveMaxPayloadBytes_NeverReachesTheAlgorithm()
    {
        var compression = new IdentityCompression();

        Assert.Throws<BinaryLimitException>(() => Custom(compression).Deserialize<int>(Bomb()));

        Assert.Equal(0, compression.DecompressCalls);
    }

    [Fact]
    public void CompressionAlgorithm_LivesInAnAssemblyThatCannotNameALimit()
    {
        var primitives = typeof(ICompressionAlgorithm).Assembly;

        Assert.DoesNotContain(
            typeof(SerializationLimits).Assembly.GetName().Name,
            primitives.GetReferencedAssemblies().Select(reference => reference.Name));
    }

    [Fact]
    public void CompressionAlgorithm_TakesNoPolicyOnAnyMember()
    {
        Type[] policy = [typeof(SerializationLimits), typeof(PhaseBudget), typeof(SerializationBudget)];
        var members = typeof(ICompressionAlgorithm).GetMethods();

        Assert.NotEmpty(members);
        foreach (var method in members)
        {
            Assert.DoesNotContain(method.ReturnType, policy);
            Assert.All(
                method.GetParameters(),
                parameter => Assert.DoesNotContain(parameter.ParameterType, policy));
        }
    }

    // --- CMP-13: a custom algorithm travels under its registered name ------------------------------

    [Fact]
    public void Deserialize_ACustomCompressionAlgorithm_RoundTripsUnderItsRegisteredName()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithCompression(new IdentityCompression())
                .RegisterCustomCompression(
                    IdentityCompression.RegisteredName, static () => new IdentityCompression())
                .Build());
        var source = Compressible();

        byte[] frame = serializer.Serialize(source);
        var header = Wire.ReadHeader(frame);

        Assert.Equal((byte)CompressionAlgorithm.Custom, header.Compression);
        Assert.Equal(IdentityCompression.RegisteredName, header.CustomCompressionName);
        Assert.Equivalent(source, serializer.Deserialize<Person>(frame));
    }

    /// <summary>
    /// A reader that knows the identity codec by name and allows a kilobyte of payload, so a frame
    /// declaring 60 MB of output breaks a limit rather than the codec.
    /// </summary>
    private static BinarySerializer Custom(IdentityCompression compression) =>
        new(BinarySerializerOptions.Configure()
            .RegisterCustomCompression(IdentityCompression.RegisteredName, () => compression)
            .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 1024 })
            .Build());

    /// <summary>A frame whose four compressed bytes claim to expand into 60 MB.</summary>
    private static byte[] Bomb() =>
        Wire.FrameWith(
            [1, 2, 3, 4],
            compression: (byte)CompressionAlgorithm.Custom,
            customCompressionName: IdentityCompression.RegisteredName,
            uncompressedLength: 60 * 1024 * 1024,
            compressedLength: 4,
            onDiskLength: 4);

    private static byte[] Compress(ICompressionAlgorithm algorithm, byte[] source)
    {
        var buffer = new ArrayBufferWriter<byte>();
        algorithm.Compress(source, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    // --- CMP-17: decompression produces exactly the declared length ----------------------------------

    public static TheoryData<string> BuiltIns => ["deflate", "brotli"];

    private static ICompressionAlgorithm BuiltIn(string name) => name == "deflate"
        ? new DeflateCompression()
        : new BrotliCompression();

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Decompress_ABuiltInDeclaredLongerThanItsStream_ThrowsFormat(string name)
    {
        var algorithm = BuiltIn(name);
        byte[] compressed = Compress(algorithm, new byte[1024]);

        Assert.Throws<BinaryFormatException>(
            () => algorithm.Decompress(compressed, new ArrayBufferWriter<byte>(), 1025));
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Decompress_ABuiltInDeclaredShorterThanItsStream_ThrowsFormat(string name)
    {
        var algorithm = BuiltIn(name);
        byte[] compressed = Compress(algorithm, new byte[1024]);

        Assert.Throws<BinaryFormatException>(
            () => algorithm.Decompress(compressed, new ArrayBufferWriter<byte>(), 1023));
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Decompress_ABuiltInDeclaredExactly_WritesExactlyThatMuch(string name)
    {
        var algorithm = BuiltIn(name);
        byte[] source = [.. Enumerable.Range(0, 1024).Select(index => (byte)(index % 7))];
        var destination = new ArrayBufferWriter<byte>();

        algorithm.Decompress(Compress(algorithm, source), destination, source.Length);

        Assert.Equal(source, destination.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Deserialize_ACustomAlgorithmMissingTheDeclaredLength_ThrowsFormat(int surplus)
    {
        byte[] frame = With(new IdentityCompression()).Serialize(Compressible());

        var reader = new BinarySerializer(BinarySerializerOptions.Configure()
            .RegisterCustomCompression(IdentityCompression.RegisteredName, () => new MisreportingDecompression(surplus))
            .Build());

        Assert.Throws<BinaryFormatException>(() => reader.Deserialize<Person>(frame));
    }

    /// <summary>
    /// Passes identity-compressed bytes through, then writes <paramref name="surplus"/> bytes too many,
    /// or drops them when it is negative.
    /// </summary>
    private sealed class MisreportingDecompression(int surplus) : ICompressionAlgorithm
    {
        public CompressionAlgorithm Kind => CompressionAlgorithm.Custom;

        public string? CustomName => IdentityCompression.RegisteredName;

        public void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination) =>
            destination.Write(source);

        public void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength)
        {
            if (surplus < 0)
            {
                destination.Write(source[..(source.Length + surplus)]);
                return;
            }

            destination.Write(source);
            destination.Write(new byte[surplus]);
        }
    }

    // --- the declared expansion is the reader's policy, not the payload's choice -------------------

    [Fact]
    public void Deserialize_APayloadExpandingMoreThanTheRatioAdmits_ThrowsLimit()
    {
        // The same bytes, written by a serializer that allows the expansion and read by one that does
        // not: nothing about the payload changed, only the reader's policy.
        byte[] payload = With(new DeflateCompression()).Serialize(new string('x', 20_000));

        var strict = With(
            new DeflateCompression(), SerializationLimits.Default with { MaxDecompressionRatio = 2 });

        AssertEx.Throws<BinaryLimitException>(
            nameof(SerializationLimits.MaxDecompressionRatio),
            () => strict.Deserialize<string>(payload));
    }

    [Fact]
    public void Deserialize_APayloadWithinTheConfiguredRatio_IsAccepted()
    {
        var serializer = With(
            new DeflateCompression(), SerializationLimits.Default with { MaxDecompressionRatio = 100_000 });

        string value = new('x', 20_000);

        Assert.Equal(value, serializer.Deserialize<string>(serializer.Serialize(value)));
    }

    [Fact]
    public void Deserialize_TheSamePayload_PassesTheDefaultReaderAndFailsAStricterOne()
    {
        string value = new('x', 20_000);
        byte[] payload = With(new DeflateCompression()).Serialize(value);

        var strict = With(
            new DeflateCompression(), SerializationLimits.Default with { MaxDecompressionRatio = 2 });

        Assert.Equal(value, With(new DeflateCompression()).Deserialize<string>(payload));
        AssertEx.Throws<BinaryLimitException>(
            nameof(SerializationLimits.MaxDecompressionRatio),
            () => strict.Deserialize<string>(payload));
    }

    [Fact]
    public void Deserialize_AnExpansionExactlyAtTheRatio_IsAcceptedAndOneBelowItIsNot()
    {
        string value = new('x', 20_000);
        byte[] payload = With(new DeflateCompression()).Serialize(value);
        var header = Wire.ReadHeader(payload);

        // The smallest ratio under which the declared expansion is still admitted.
        int exact = (header.UncompressedLength + header.CompressedLength - 1) / header.CompressedLength;

        var atTheRatio = With(new DeflateCompression(), SerializationLimits.Default with { MaxDecompressionRatio = exact });
        var belowIt = With(new DeflateCompression(), SerializationLimits.Default with { MaxDecompressionRatio = exact - 1 });

        Assert.Equal(value, atTheRatio.Deserialize<string>(payload));
        AssertEx.Throws<BinaryLimitException>(
            nameof(SerializationLimits.MaxDecompressionRatio),
            () => belowIt.Deserialize<string>(payload));
    }

    [Fact]
    public void Deserialize_AnUncompressedPayload_IsNotMeasuredAgainstTheRatio()
    {
        // With no compression the two lengths are already required to be equal, so the ratio has
        // nothing left to say about them.
        var serializer = With(
            new NoCompression(), SerializationLimits.Default with { MaxDecompressionRatio = 1 });

        Assert.Equal(42, serializer.Deserialize<int>(serializer.Serialize(42)));
    }

    [Fact]
    public void Deserialize_ABrotliStreamThatYieldsTheDeclaredLengthButNeverEnds_ThrowsFormat()
    {
        var serializer = With(new BrotliCompression());
        byte[] payload = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build()).Serialize(Compressible());

        // Flushed but never finished: every byte of the payload is in the stream, the end marker is not.
        using var encoder = new BrotliEncoder(quality: 5, window: 22);
        byte[] buffer = new byte[BrotliEncoder.GetMaxCompressedLength(payload.Length)];
        encoder.Compress(payload, buffer, out _, out int written, isFinalBlock: false);
        encoder.Flush(buffer.AsSpan(written), out int flushed);
        byte[] unterminated = buffer[..(written + flushed)];

        using (var decoder = new BrotliDecoder())
        {
            byte[] produced = new byte[payload.Length + 1];
            var status = decoder.Decompress(unterminated, produced, out _, out int length);

            Assert.Equal(OperationStatus.NeedMoreData, status);
            Assert.Equal(payload, produced[..length]);
        }

        byte[] frame = Wire.FrameWith(
            unterminated,
            compression: (byte)CompressionAlgorithm.Brotli,
            uncompressedLength: payload.Length);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(frame));
    }
}
