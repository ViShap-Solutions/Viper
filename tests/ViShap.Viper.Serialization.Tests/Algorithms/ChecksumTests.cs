using ViShap.Viper.Checksum;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins CHK-02, CHK-04 and CHK-08: the built-in checksum round trips under its own identifier, a
/// checksum whose length does not match the algorithm the header names is malformed input, and
/// requiring a checksum is a statement about the payload's metadata rather than a capability of the
/// reader.
/// </summary>
public class ChecksumTests
{
    private static BinarySerializer WithCrc32() =>
        new(BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build());

    // --- CHK-02: Crc32 round trips -----------------------------------------------------------------

    [Fact]
    public void Deserialize_Crc32Payload_RoundTrips()
    {
        var serializer = WithCrc32();
        var source = new Person { Name = "Alice", Age = 30 };

        Assert.Equivalent(source, serializer.Deserialize<Person>(serializer.Serialize(source)));
    }

    [Fact]
    public void Serialize_Crc32Payload_RecordsTheAlgorithmAndItsFourDigestBytes()
    {
        var header = Wire.ReadHeader(WithCrc32().Serialize(42));

        Assert.Equal((byte)ChecksumAlgorithm.Crc32, header.ChecksumAlgorithm);
        Assert.Null(header.CustomChecksumName);
        Assert.Equal(4, header.Checksum.Length);
    }

    [Fact]
    public void Deserialize_ACrc32PayloadWhoseBodyChanged_ThrowsIntegrity()
    {
        var serializer = WithCrc32();
        byte[] frame = serializer.Serialize(new Person { Name = "Alice", Age = 30 });

        byte[] tampered = Mutate.FlipByte(frame, frame.Length - 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<Person>(tampered));
    }

    // --- CHK-04: a digest of the wrong width for the algorithm named --------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(8)]
    public void Deserialize_AChecksumOfAWidthTheAlgorithmNeverProduces_ThrowsFormat(int checksumBytes)
    {
        byte[] frame = Wire.FrameWith(
            Wire.Payload(writer => writer.Write(123)),
            checksumAlgorithm: (byte)ChecksumAlgorithm.Crc32,
            checksum: new byte[checksumBytes]);

        AssertEx.Throws<BinaryFormatException>(
            "Checksum length", () => WithCrc32().Deserialize<int>(frame));
    }

    // --- CHK-08: RequireChecksum is a policy, a configured algorithm is only a capability ----------

    [Fact]
    public void Deserialize_APayloadCarryingNoChecksum_IsRejectedWhenAChecksumIsRequired()
    {
        byte[] unprotected = new BinarySerializer().Serialize(123);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32())
                .RequireChecksum()
                .Build());

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<int>(unprotected));
    }

    [Fact]
    public void Deserialize_APayloadCarryingNoChecksum_IsAcceptedWhenAChecksumIsOnlyACapability()
    {
        byte[] unprotected = new BinarySerializer().Serialize(123);

        Assert.Equal(123, WithCrc32().Deserialize<int>(unprotected));
    }
}
