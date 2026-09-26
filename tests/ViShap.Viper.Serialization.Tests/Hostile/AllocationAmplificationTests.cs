using System.IO.Compression;
using ViShap.Viper.Compression;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins D1 at the public boundary: a declared length is compared with the bytes that can physically
/// still arrive before it drives an allocation — on the wire and in the header, not only inside the
/// payload.
/// <para>
/// Compression is the one phase whose output may legitimately exceed its input, so it needs both
/// halves of the rule: the declared expansion is bounded against the compressed bytes that did
/// arrive, and the buffer that receives the output grows only as output is produced.
/// </para>
/// </summary>
public class AllocationAmplificationTests
{
    /// <summary>Bytes the whole operation may allocate. Generous, and orders below the declarations.</summary>
    private const long AllocationCeiling = 1024 * 1024;

    private static long AllocatedBy(Action act)
    {
        // Warm the path so one-off JIT and cache allocations are not attributed to the measurement.
        try { act(); } catch (BinarySerializerException) { }

        long before = GC.GetAllocatedBytesForCurrentThread();
        try { act(); } catch (BinarySerializerException) { }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // --- D1-01: the on-disk payload length ------------------------------------------------------

    [Fact]
    public void Deserialize_TinyFrameDeclaringAHugeOnDiskLength_ThrowsFormat()
    {
        byte[] frame = Wire.Frame([1], declaredLength: 60 * 1024 * 1024);
        var serializer = new BinarySerializer();

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_TinyFrameDeclaringAHugeOnDiskLength_AllocatesNothingProportional()
    {
        byte[] frame = Wire.Frame([1], declaredLength: 60 * 1024 * 1024);
        var serializer = new BinarySerializer();

        long allocated = AllocatedBy(() => serializer.Deserialize<int>(frame));

        Assert.True(
            allocated < AllocationCeiling,
            $"A {frame.Length}-byte frame declaring 60 MiB allocated {allocated:N0} bytes.");
    }

    // --- D1-02: header strings ------------------------------------------------------------------

    [Fact]
    public void Deserialize_HeaderDeclaringAHugeCustomAlgorithmName_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWithOversizedCustomName(declaredLength: 3_000_000, actualBytes: 1);
        var serializer = new BinarySerializer();

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_HeaderDeclaringAHugeCustomAlgorithmName_AllocatesNothingProportional()
    {
        byte[] frame = Wire.FrameWithOversizedCustomName(declaredLength: 3_000_000, actualBytes: 1);
        var serializer = new BinarySerializer();

        long allocated = AllocatedBy(() => serializer.Deserialize<int>(frame));

        Assert.True(
            allocated < AllocationCeiling,
            $"A {frame.Length}-byte frame declaring a 3 MB name allocated {allocated:N0} bytes.");
    }

    // --- D1-03: the checksum --------------------------------------------------------------------

    [Fact]
    public void Deserialize_HeaderDeclaringMoreChecksumBytesThanArePresent_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWithOversizedChecksum(declaredChecksumLength: 200, actualBytes: 2);
        var serializer = new BinarySerializer();

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<int>(frame));
    }

    // --- the declared uncompressed length -------------------------------------------------------

    /// <summary>A well-formed Deflate body that decompresses to exactly four bytes.</summary>
    private static byte[] TinyBody()
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write([1, 2, 3, 4]);

        return output.ToArray();
    }

    private static byte[] Compressed(byte[] body, int uncompressedLength) =>
        Wire.FrameWith(
            body,
            compression: (byte)CompressionAlgorithm.Deflate,
            uncompressedLength: uncompressedLength,
            compressedLength: body.Length);

    [Fact]
    public void Deserialize_TinyFrameDeclaringAHugeUncompressedLength_ThrowsLimit()
    {
        byte[] frame = Compressed(TinyBody(), uncompressedLength: 64 * 1024 * 1024);

        AssertEx.Throws<BinaryLimitException>(
            nameof(Security.SerializationLimits.MaxDecompressionRatio),
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_TinyFrameDeclaringAHugeUncompressedLength_AllocatesNothingProportional()
    {
        byte[] frame = Compressed(TinyBody(), uncompressedLength: 64 * 1024 * 1024);
        var serializer = new BinarySerializer();

        long allocated = AllocatedBy(() => serializer.Deserialize<int>(frame));

        Assert.True(
            allocated < AllocationCeiling,
            $"A {frame.Length}-byte frame declaring 64 MiB allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Deserialize_AnExpansionWithinTheRatioThatProducesNothing_AllocatesNothingProportional()
    {
        // The declared expansion is legal against the bytes delivered, so the ratio admits it. What
        // keeps the frame cheap is the second half of the rule: the output buffer follows the bytes
        // the decoder actually produces, and this body produces none.
        byte[] frame = Compressed(new byte[8192], uncompressedLength: 8192 * 5000);
        var serializer = new BinarySerializer();

        long allocated = AllocatedBy(() => serializer.Deserialize<int>(frame));

        Assert.True(
            allocated < AllocationCeiling,
            $"A {frame.Length}-byte frame declaring 40 MB allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Deserialize_AGenuinelyCompressedPayload_IsUnaffected()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithCompression(new Deflate()).Build());

        string value = new('x', 200_000);

        Assert.Equal(value, serializer.Deserialize<string>(serializer.Serialize(value)));
    }
}
