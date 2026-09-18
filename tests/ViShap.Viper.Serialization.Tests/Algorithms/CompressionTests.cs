using ViShap.Viper.Compression;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins CMP-02, CMP-03, CMP-04, CMP-10 and CMP-11: a compressed payload round trips, and
/// decompression produces exactly the declared length so a payload cannot hide part of its own
/// content.
/// </summary>
public class CompressionTests
{
    private static BinarySerializer With(ICompressionAlgorithm compression) =>
        new(BinarySerializerOptions.Configure().WithCompression(compression).Build());

    private static Person Compressible() => new() { Name = new string('x', 5_000), Age = 7 };

    [Fact]
    public void Deserialize_DeflatePayload_RoundTrips()
    {
        var serializer = With(new Deflate());
        var source = Compressible();

        Assert.Equivalent(source, serializer.Deserialize<Person>(serializer.Serialize(source)));
    }

    [Fact]
    public void Deserialize_BrotliPayload_RoundTrips()
    {
        var serializer = With(new Brotli());
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

        int compressed = With(new Deflate()).Serialize(source).Length;
        int plain = new BinarySerializer().Serialize(source).Length;

        Assert.True(compressed < plain, $"compressed {compressed} is not smaller than plain {plain}");
    }

    [Fact]
    public void Decompress_OutputLongerThanDeclared_ThrowsFormat()
    {
        var deflate = new Deflate();
        byte[] compressed = Compress(deflate, new byte[1024], out int length);

        Assert.Throws<BinaryFormatException>(
            () => deflate.Decompress(compressed.AsSpan(0, length), new byte[4]));
    }

    [Fact]
    public void Deserialize_HeaderDeclaringMoreUncompressedBytesThanTheStreamProduces_ThrowsFormat()
    {
        // The exactness rule lives on the phase boundary, not in the algorithm: the algorithm refuses
        // to overrun the buffer, and the service refuses a stream that underfills it.
        var serializer = With(new Deflate());
        byte[] payload = serializer.Serialize(Compressible());

        int declared = BitConverter.ToInt32(payload, Wire.UncompressedLengthOffset);
        byte[] tampered = Mutate.SetInt32(payload, Wire.UncompressedLengthOffset, declared + 64);

        AssertEx.Throws<BinaryFormatException>(
            "expected", () => serializer.Deserialize<Person>(tampered));
    }

    [Fact]
    public void Deserialize_CorruptedDeflateBody_ThrowsFormat()
    {
        var serializer = With(new Deflate());
        byte[] payload = serializer.Serialize(Compressible());

        byte[] tampered = Mutate.FlipByte(payload, Wire.PlainHeaderLength + 2);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(tampered));
    }

    [Fact]
    public void Deserialize_CorruptedBrotliBody_ThrowsFormat()
    {
        var serializer = With(new Brotli());
        byte[] payload = serializer.Serialize(Compressible());

        byte[] tampered = Mutate.FlipByte(payload, Wire.PlainHeaderLength + 2);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(tampered));
    }

    private static byte[] Compress(ICompressionAlgorithm algorithm, byte[] source, out int length)
    {
        byte[] buffer = new byte[algorithm.GetMaxCompressedLength(source.Length)];
        length = algorithm.Compress(source, buffer);
        return buffer;
    }
}
