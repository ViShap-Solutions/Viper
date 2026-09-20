using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins D1 at the public boundary: a declared length is compared with the bytes that can physically
/// still arrive before it drives an allocation — on the wire and in the header, not only inside the
/// payload.
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
}
