using ViShap.Viper.Checksum;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins CHK-02, CHK-04, CHK-08 and CHK-10: the built-in checksums round trip under their own
/// identifiers and resolve from the catalog, a checksum whose length does not match the algorithm the header names is malformed input, and
/// requiring a checksum is a statement about the payload's metadata rather than a capability of the
/// reader.
/// </summary>
public class ChecksumTests
{
    private static BinarySerializer WithCrc32() =>
        new(BinarySerializerOptions.Configure().WithChecksum(new Crc32Checksum()).Build());

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

    // --- CHK-10: the XXH3 checksums round-trip and resolve from the catalog ------------------------

    public static TheoryData<string, int> XxHashes => new() { { "xxhash3", 8 }, { "xxhash128", 16 } };

    private static IChecksumAlgorithm XxHash(string name) => name == "xxhash3"
        ? new XxHash3Checksum()
        : new XxHash128Checksum();

    [Theory]
    [MemberData(nameof(XxHashes))]
    public void Deserialize_AnXxHashPayload_RoundTripsThroughAReaderThatConfiguresNoChecksum(string name, int width)
    {
        var writer = new BinarySerializer(BinarySerializerOptions.Configure().WithChecksum(XxHash(name)).Build());
        var source = new Person { Name = "Alice", Age = 30 };

        byte[] frame = writer.Serialize(source);

        var header = Wire.ReadHeader(frame);
        Assert.Equal((byte)XxHash(name).Kind, header.ChecksumAlgorithm);
        Assert.Null(header.CustomChecksumName);
        Assert.Equal(width, header.Checksum.Length);
        Assert.Equivalent(source, new BinarySerializer().Deserialize<Person>(frame));
    }

    [Theory]
    [MemberData(nameof(XxHashes))]
    public void Deserialize_AnXxHashPayloadWhoseBodyChanged_ThrowsIntegrity(string name, int width)
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithChecksum(XxHash(name)).Build());
        byte[] frame = serializer.Serialize(new Person { Name = "Alice", Age = 30 });

        byte[] tampered = Mutate.FlipByte(frame, frame.Length - 1);

        Assert.Equal(width, XxHash(name).HashSizeInBytes);
        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<Person>(tampered));
    }

    [Theory]
    [MemberData(nameof(XxHashes))]
    public void Compute_AnXxHash_IsTheSystemIOHashingDigest(string name, int width)
    {
        byte[] source = [1, 2, 3, 4, 5];
        byte[] digest = new byte[width];

        XxHash(name).Compute(source, digest);

        byte[] expected = name == "xxhash3"
            ? System.IO.Hashing.XxHash3.Hash(source)
            : System.IO.Hashing.XxHash128.Hash(source);
        Assert.Equal(expected, digest);
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
                .WithChecksum(new Crc32Checksum())
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

    // --- a configured algorithm is not trusted to size a buffer -----------------------------------

    [Fact]
    public void Serialize_AChecksumReportingANegativeSize_ThrowsConfiguration()
    {
        // The reported size is the one value an external implementation turns into an allocation, so
        // it is checked rather than believed.
        AssertEx.Throws<BinaryConfigurationException>(
            "cannot be represented by the V1 header", () => Wide(-1).Serialize(42));
    }

    [Fact]
    public void Deserialize_AChecksumReportingANegativeSize_ThrowsConfiguration()
    {
        // The payload names the algorithm, so the registered factory decides what runs. A factory
        // returning an unusable algorithm is a configuration failure, not a malformed payload.
        byte[] frame = Wire.FrameWith(
            Wire.Payload(writer => writer.Write(42)),
            checksumAlgorithm: (byte)ChecksumAlgorithm.Custom,
            customChecksumName: WideChecksum.RegisteredName,
            checksum: [0, 0, 0, 0]);

        AssertEx.Throws<BinaryConfigurationException>(
            "cannot be represented by the V1 header", () => Wide(-1).Deserialize<int>(frame));
    }

    [Fact]
    public void Serialize_AChecksumReportingZeroBytes_ThrowsConfiguration()
    {
        // A checksum that computes nothing verifies nothing; only NoChecksum stands for no checksum.
        AssertEx.Throws<BinaryConfigurationException>(
            "between 1 and 255 bytes", () => Wide(0).Serialize(42));
    }

    [Fact]
    public void Deserialize_AChecksumReportingZeroBytes_ThrowsConfiguration()
    {
        byte[] frame = Wire.FrameWith(
            Wire.Payload(writer => writer.Write(42)),
            checksumAlgorithm: (byte)ChecksumAlgorithm.Custom,
            customChecksumName: WideChecksum.RegisteredName);

        AssertEx.Throws<BinaryConfigurationException>(
            "between 1 and 255 bytes", () => Wide(0).Deserialize<int>(frame));
    }

    private static BinarySerializer Wide(int checksumBytes) =>
        new(BinarySerializerOptions.Configure()
            .WithChecksum(new WideChecksum(checksumBytes))
            .RegisterCustomChecksum(WideChecksum.RegisteredName, () => new WideChecksum(checksumBytes))
            .Build());
}
