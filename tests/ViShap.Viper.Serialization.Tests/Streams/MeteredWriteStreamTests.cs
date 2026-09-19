using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Streams;

/// <summary>
/// Pins STR-09…STR-16 and ENV-09: the write budget belongs to the operation, so appending to a
/// stream that already holds data costs that operation nothing, and a rewind used to patch a keyed
/// field length is not charged twice — the budget follows the high-water mark.
/// </summary>
public class MeteredWriteStreamTests
{
    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    // --- STR-09 / STR-10: the budget is relative to where the operation starts -------------------

    [Fact]
    public void Serialize_IntoStreamAtNonZeroOffset_ChargesOnlyTheBytesItWrites()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 64 });

        using var atOrigin = new MemoryStream();
        serializer.Serialize(atOrigin, 123);
        long produced = atOrigin.Length;

        using var atOffset = new MemoryStream();
        atOffset.Write(new byte[1024]);
        serializer.Serialize(atOffset, 123);

        Assert.Equal(1024 + produced, atOffset.Length);
    }

    [Fact]
    public void BytesWritten_OverAStreamAtANonZeroOffset_CountsFromZero()
    {
        using var destination = new MemoryStream();
        destination.Write(new byte[100]);

        var stream = new MeteredWriteStream(destination, 1_000, "test");
        stream.Write(new byte[10]);

        Assert.Equal(10, stream.BytesWritten);
        Assert.Equal(10, stream.Position);
    }

    // --- STR-11 / STR-12: under, at and beyond the budget ---------------------------------------

    [Fact]
    public void Write_UnderTheBudget_Succeeds()
    {
        using var destination = new MemoryStream();
        var stream = new MeteredWriteStream(destination, 10, "test");

        stream.Write(new byte[9]);

        Assert.Equal(9, stream.BytesWritten);
    }

    [Fact]
    public void Write_AtExactlyTheBudget_Succeeds()
    {
        using var destination = new MemoryStream();
        var stream = new MeteredWriteStream(destination, 10, "test");

        stream.Write(new byte[10]);

        Assert.Equal(10, stream.BytesWritten);
    }

    [Fact]
    public void Write_OneByteBeyondTheBudget_ThrowsLimit()
    {
        using var destination = new MemoryStream();
        var stream = new MeteredWriteStream(destination, 10, "test");
        stream.Write(new byte[10]);

        Assert.Throws<BinaryLimitException>(() => stream.WriteByte(0));
    }

    [Fact]
    public void Serialize_ProducingMoreThanTheWireBudget_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 32 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Serialize(new Person { Name = new string('x', 64), Age = 1 }));
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

    // --- STR-13: a rewind does not double-charge -------------------------------------------------

    [Fact]
    public void Write_AfterARewind_ChargesOnlyTheHighWaterMark()
    {
        using var destination = new MemoryStream();
        var stream = new MeteredWriteStream(destination, 10, "test");

        stream.Write(new byte[10]);
        stream.Position = 4;
        stream.Write(new byte[4]);

        Assert.Equal(10, stream.BytesWritten);
    }

    [Fact]
    public void Seek_BeforeTheStartOfTheOperation_ThrowsStream()
    {
        using var destination = new MemoryStream();
        destination.Write(new byte[16]);
        var stream = new MeteredWriteStream(destination, 100, "test");
        stream.Write(new byte[4]);

        Assert.Throws<BinaryStreamException>(() => stream.Seek(-1, SeekOrigin.Begin));
    }

    [Fact]
    public void Serialize_AKeyedContractAtTheExactBudget_IsNotChargedForItsLengthPatches()
    {
        // Every keyed field is written, then its length patched in place. The payload is charged
        // once, so the tightest budget that admits the bytes is the length of the payload itself.
        var contract = new NewSchema { Removed = new Node { Value = 1 }, Kept = new Node { Value = 2 } };
        byte[] produced = new BinarySerializer().Serialize(contract);

        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = produced.Length });

        Assert.Equal(produced.Length, serializer.Serialize(contract).Length);
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
    public void Dispose_LeavesTheCallerStreamOpen()
    {
        using var destination = new TrackingStream();

        using (var stream = new MeteredWriteStream(destination, 1_000, "test"))
            stream.Write(new byte[4]);

        Assert.False(destination.Disposed);
    }

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

    // --- STR-16: seekability follows the destination ---------------------------------------------

    [Fact]
    public void CanSeek_OverASeekableDestination_IsTrue()
    {
        using var destination = new MemoryStream();

        Assert.True(new MeteredWriteStream(destination, 1_000, "test").CanSeek);
    }

    [Fact]
    public void CanSeek_OverANonSeekableDestination_IsFalse()
    {
        using var destination = new NonSeekableWriteStream();

        Assert.False(new MeteredWriteStream(destination, 1_000, "test").CanSeek);
    }

    [Fact]
    public void Position_OverANonSeekableDestination_ThrowsNotSupported()
    {
        using var destination = new NonSeekableWriteStream();
        var stream = new MeteredWriteStream(destination, 1_000, "test");

        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
    }
}
