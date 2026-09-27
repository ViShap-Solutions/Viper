using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Metering;

/// <summary>
/// Pins STR-09…STR-16: a write is built in the serializer's own buffer, which refuses space past the
/// budget and whose patches overwrite bytes already counted, and the finished frame is checked
/// against the wire budget before it is copied out. The budget therefore belongs to the operation:
/// bytes a destination already held cost it nothing, and a destination never has to seek.
/// </summary>
public class WriteMeteringTests
{
    private static BinarySerializer Limited(SerializationLimits limits, int version = 1) =>
        new(BinarySerializerOptions.Configure().WithVersion(version).WithLimits(limits).Build());

    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    private static NewSchema Keyed() => new() { Removed = new Node { Value = 1 }, Kept = new Node { Value = 2 } };

    // --- STR-09 / STR-10: the budget is relative to where the operation starts -------------------

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Serialize_IntoAStreamAtANonZeroOffset_ChargesOnlyTheBytesItWrites(int version)
    {
        byte[] produced = Limited(SerializationLimits.Default, version).Serialize(Sample());
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = produced.Length }, version);

        using var destination = new MemoryStream();
        destination.Write(new byte[1024]);
        serializer.Serialize(destination, Sample());

        Assert.Equal(1024 + produced.Length, destination.Length);
        Assert.Equal(produced, destination.ToArray()[1024..]);
    }

    [Fact]
    public void Serialize_AppendingTwice_LeavesBothPayloadsReadable()
    {
        var serializer = new BinarySerializer();
        using var stream = new MemoryStream();

        serializer.Serialize(stream, 1);
        long firstLength = stream.Length;
        serializer.Serialize(stream, 2);

        stream.Position = 0;
        Assert.Equal(1, serializer.Deserialize<int>(stream));
        Assert.Equal(firstLength, stream.Position);
        Assert.Equal(2, serializer.Deserialize<int>(stream));
    }

    // --- STR-11 / STR-12: under, at and beyond the budget ---------------------------------------

    [Fact]
    public void GetSpan_UpToTheBudget_Succeeds()
    {
        using var buffer = new PayloadBuffer(10, "test");

        buffer.GetSpan(9);
        buffer.Advance(9);
        buffer.GetSpan(1);
        buffer.Advance(1);

        Assert.Equal(10, buffer.Length);
    }

    [Fact]
    public void GetSpan_NeverHandsOutSpaceBeyondTheBudget()
    {
        using var buffer = new PayloadBuffer(10, "test");

        Assert.True(buffer.GetSpan(4).Length <= 10);
    }

    [Fact]
    public void GetSpan_OneByteBeyondTheBudget_ThrowsLimit()
    {
        using var buffer = new PayloadBuffer(10, "test");
        buffer.GetSpan(10);
        buffer.Advance(10);

        AssertEx.Throws<BinaryLimitException>("test", () => buffer.GetSpan(1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Serialize_AtExactlyTheWireBudget_Succeeds(int version)
    {
        byte[] produced = Limited(SerializationLimits.Default, version).Serialize(Sample());

        byte[] limited = Limited(SerializationLimits.Default with { MaxWireBytes = produced.Length }, version)
            .Serialize(Sample());

        Assert.Equal(produced, limited);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Serialize_OneByteOverTheWireBudget_ThrowsLimitAndWritesNothing(int version)
    {
        byte[] produced = Limited(SerializationLimits.Default, version).Serialize(Sample());
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = produced.Length - 1 }, version);
        using var destination = new MemoryStream();

        AssertEx.Throws<BinaryLimitException>("wire", () => serializer.Serialize(destination, Sample()));

        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public void Serialize_ProducingMoreThanThePayloadBudget_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxPayloadBytes = 32 });

        AssertEx.Throws<BinaryLimitException>(
            "payload", () => serializer.Serialize(new Person { Name = new string('x', 64), Age = 1 }));
    }

    // --- STR-13: a patch overwrites bytes already counted ---------------------------------------

    [Fact]
    public void Patch_OverwritesInPlaceWithoutChargingTheBudgetAgain()
    {
        using var buffer = new PayloadBuffer(10, "test");
        buffer.GetSpan(10).Fill(0xAA);
        buffer.Advance(10);

        buffer.Patch(4, [1, 2, 3, 4]);

        Assert.Equal(10, buffer.Length);
        Assert.Equal<byte>([0xAA, 0xAA, 0xAA, 0xAA, 1, 2, 3, 4, 0xAA, 0xAA], buffer.ToArray());
    }

    [Fact]
    public void Patch_AcrossASegmentBoundary_WritesBothSides()
    {
        using var buffer = new PayloadBuffer(1 << 20, "test");
        int first = buffer.GetSpan(1).Length;
        buffer.Advance(first);
        buffer.GetSpan(first * 2);
        buffer.Advance(8);

        buffer.Patch(first - 2, [1, 2, 3, 4]);

        byte[] bytes = buffer.ToArray();
        Assert.Equal<byte>([1, 2, 3, 4], bytes[(first - 2)..(first + 2)]);
    }

    [Fact]
    public void Patch_BeyondTheCommittedBytes_Throws()
    {
        using var buffer = new PayloadBuffer(10, "test");
        buffer.GetSpan(4);
        buffer.Advance(4);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Patch(2, [1, 2, 3]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Serialize_AKeyedContractAtTheExactBudget_IsNotChargedForItsLengthPatches(int version)
    {
        // Every keyed field is written, then its length patched in place. The payload is charged
        // once, so the tightest budget that admits the bytes is the length of the frame itself.
        byte[] produced = Limited(SerializationLimits.Default, version).Serialize(Keyed());

        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = produced.Length }, version);

        Assert.Equal(produced, serializer.Serialize(Keyed()));
    }

    // --- STR-14: an underlying I/O failure keeps its cause ---------------------------------------

    [Fact]
    public void Serialize_IntoAFailingStream_ThrowsStreamExceptionPreservingTheCause()
    {
        using var destination = new FailingStream(bytesBeforeFailure: 4);

        var error = Assert.Throws<BinaryStreamException>(
            () => new BinarySerializer().Serialize(destination, new Person { Name = "Alice", Age = 1 }));

        Assert.IsType<IOException>(error.InnerException);
    }

    // --- STR-15: the caller's stream is never disposed -------------------------------------------

    [Fact]
    public void Serialize_IntoACallerStream_LeavesItOpen()
    {
        using var destination = new TrackingStream();

        new BinarySerializer().Serialize(destination, 123);

        Assert.False(destination.Disposed);
    }

    [Fact]
    public void Serialize_IntoACallerStreamThatBreachesTheBudget_LeavesItOpen()
    {
        using var destination = new TrackingStream();
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 8 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(destination, "a long value"));

        Assert.False(destination.Disposed);
    }

    // --- STR-16: a destination is never asked to seek --------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Serialize_AKeyedContractToANonSeekableStream_WritesTheSameBytes(int version)
    {
        var serializer = Limited(SerializationLimits.Default, version);
        using var destination = new NonSeekableWriteStream();

        serializer.Serialize(destination, Keyed());

        Assert.Equal(serializer.Serialize(Keyed()), destination.Written);
    }
}
