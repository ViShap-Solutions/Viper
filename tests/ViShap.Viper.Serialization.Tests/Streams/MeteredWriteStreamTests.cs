using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Streams;

/// <summary>
/// Pins STR-09, STR-10 and ENV-09: the write budget belongs to the operation, so appending to a
/// stream that already holds data costs that operation nothing.
/// </summary>
public class MeteredWriteStreamTests
{
    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

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

    [Fact]
    public void Serialize_IntoAFailingStream_ThrowsStreamExceptionPreservingTheCause()
    {
        using var destination = new FailingStream(bytesBeforeFailure: 4);

        var error = Assert.Throws<BinaryStreamException>(
            () => new BinarySerializer().Serialize(destination, new Person { Name = "Alice", Age = 1 }));

        Assert.IsType<IOException>(error.InnerException);
    }
}
