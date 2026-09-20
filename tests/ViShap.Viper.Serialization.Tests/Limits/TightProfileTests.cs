using System.Collections;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins P2-01: profile P2 is the plan's tight policy, and this suite proves it is a working policy
/// rather than a list of numbers. Every limit it names is reached by a payload built for it, and the
/// failure reports the limit that actually broke — a profile where one ceiling always fired first
/// would leave the others untested everywhere else the profile is used.
/// </summary>
public class TightProfileTests
{
    /// <summary>Profile P2, exactly as the plan states it.</summary>
    private static readonly SerializationLimits Tight = SerializationLimits.Default with
    {
        MaxDepth = 4, MaxArrayLength = 3, MaxCollectionLength = 3, MaxDictionaryEntries = 2,
        MaxStringBytes = 8, MaxByteBlobBytes = 8, MaxTotalElements = 5, MaxObjectGraphNodes = 5,
        MaxKeyedFields = 3, MaxTotalKeyedFields = 6,
        MaxPayloadBytes = 64, MaxCompressedBytes = 64, MaxEncryptedBytes = 128, MaxWireBytes = 256
    };

    private static readonly BinarySerializer Serializer =
        new(BinarySerializerOptions.Configure().WithLimits(Tight).Build());

    private static Cyclic Chain(int length)
    {
        var head = new Cyclic { Name = string.Empty };
        for (int link = 1; link < length; link++)
            head = new Cyclic { Name = string.Empty, Next = head };

        return head;
    }

    [Fact]
    public void Build_TheProfile_IsAcceptedAsAPolicy()
    {
        var options = BinarySerializerOptions.Configure().WithLimits(Tight).Build();

        Assert.Equal(Tight, options.Limits);
    }

    // --- the per-value limits -------------------------------------------------------------------

    [Fact]
    public void MaxDepth_IsReached()
    {
        AssertEx.Throws<BinaryLimitException>("Nesting depth", () => Serializer.Serialize(Chain(5)));
    }

    [Fact]
    public void MaxDepth_AdmitsTheDepthBelowIt()
    {
        Assert.NotNull(Serializer.Deserialize<Cyclic>(Serializer.Serialize(Chain(4))));
    }

    [Fact]
    public void MaxArrayLength_IsReached()
    {
        AssertEx.Throws<BinaryLimitException>("Array length", () => Serializer.Serialize(new int[4]));
    }

    [Fact]
    public void MaxArrayLength_AdmitsTheLengthAtIt()
    {
        Assert.Equal(new int[3], Serializer.Deserialize<int[]>(Serializer.Serialize(new int[3])));
    }

    [Fact]
    public void MaxCollectionLength_IsReached()
    {
        AssertEx.Throws<BinaryLimitException>(
            "Collection count", () => Serializer.Serialize(new List<int> { 1, 2, 3, 4 }));
    }

    [Fact]
    public void MaxDictionaryEntries_IsReached()
    {
        AssertEx.Throws<BinaryLimitException>(
            "Dictionary entry count",
            () => Serializer.Serialize(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Fact]
    public void MaxStringBytes_IsReached()
    {
        AssertEx.Throws<BinaryLimitException>("String byte length", () => Serializer.Serialize("123456789"));
    }

    [Fact]
    public void MaxStringBytes_AdmitsTheLengthAtIt()
    {
        Assert.Equal("12345678", Serializer.Deserialize<string>(Serializer.Serialize("12345678")));
    }

    [Fact]
    public void MaxByteBlobBytes_IsReached()
    {
        AssertEx.Throws<BinaryLimitException>("BitArray length", () => Serializer.Serialize(new BitArray(65)));
    }

    // --- the cumulative budgets -----------------------------------------------------------------

    [Fact]
    public void MaxTotalElements_IsReached()
    {
        // Two collections of three: each is within MaxCollectionLength, and together they are not.
        AssertEx.Throws<BinaryLimitException>(
            "Cumulative element count",
            () => Serializer.Serialize(new SharedLists { A = [1, 2, 3], B = [4, 5, 6] }));
    }

    [Fact]
    public void MaxObjectGraphNodes_IsReached()
    {
        // Three elements is within MaxCollectionLength and within MaxTotalElements, and the depth
        // stays at three, so the only ceiling left is the node count: a list of three pairs is seven
        // objects.
        var value = new List<Cyclic> { Chain(2), Chain(2), Chain(2) };

        AssertEx.Throws<BinaryLimitException>(
            "Object graph node count", () => Serializer.Serialize(value));
    }

    [Fact]
    public void MaxKeyedFields_IsReached()
    {
        AssertEx.Throws<BinaryLimitException>(
            "Keyed field count", () => Serializer.Serialize(new CompleteContract()));
    }

    [Fact]
    public void MaxTotalKeyedFields_IsReached()
    {
        // Three objects of three fields: each is within MaxKeyedFields, and nine is not within six.
        var value = Enumerable.Range(0, 3).Select(_ => new ThreeKeys()).ToList();

        AssertEx.Throws<BinaryLimitException>(
            "Cumulative keyed field count", () => Serializer.Serialize(value));
    }

    // --- the phase ceilings ---------------------------------------------------------------------

    [Fact]
    public void MaxPayloadBytes_IsReached()
    {
        byte[] frame = Wire.FrameWith(
            Wire.Payload(writer => writer.Write(0)),
            uncompressedLength: 65, compressedLength: 65, onDiskLength: 65);

        AssertEx.Throws<BinaryLimitException>(
            "UncompressedLength", () => Serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void MaxCompressedBytes_IsReached()
    {
        // The uncompressed length is at its own ceiling, so only the compressed one can break.
        byte[] frame = Wire.FrameWith(
            Wire.Payload(writer => writer.Write(0)),
            compression: 1,
            uncompressedLength: 64, compressedLength: 65, onDiskLength: 65);

        AssertEx.Throws<BinaryLimitException>(
            "CompressedLength", () => Serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void MaxEncryptedBytes_IsReached()
    {
        byte[] frame = Wire.FrameWith(
            Wire.Payload(writer => writer.Write(0)),
            encryption: 1,
            uncompressedLength: 64, compressedLength: 64, onDiskLength: 129);

        AssertEx.Throws<BinaryLimitException>(
            "OnDiskLength", () => Serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void MaxWireBytes_IsReached()
    {
        // Every declared phase length stays within its own ceiling, so the bytes that break the
        // budget are the header's own: the outermost bound is the one that catches them.
        byte[] frame = Wire.FrameWith(
            Wire.Payload(writer => writer.Write(0)),
            compression: 255, customCompressionName: new string('c', 200),
            checksumAlgorithm: 255, customChecksumName: new string('k', 200),
            uncompressedLength: 4, compressedLength: 4, onDiskLength: 4);

        Assert.True(frame.Length > Tight.MaxWireBytes);
        Assert.Throws<BinaryLimitException>(() => Serializer.Deserialize<int>(frame));
    }
}
