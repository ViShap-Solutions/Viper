using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-34…LIM-39: the four phase sizes bound both directions, a declared phase length is
/// refused before the buffer it describes exists, and the refusal comes from the pipeline rather
/// than from the algorithm that would have processed the bytes.
/// </summary>
public class PhaseLimitTests
{
    /// <summary>Bytes the whole operation may allocate. Generous, and orders below the declarations.</summary>
    private const long AllocationCeiling = 1024 * 1024;

    private const byte Deflate = 1;
    private const byte Aes256Gcm = 1;

    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    // --- LIM-34: MaxPayloadBytes -----------------------------------------------------------------

    [Fact]
    public void Serialize_APayloadOverMaxPayloadBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxPayloadBytes = 16 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Serialize(new string('x', 512)));
    }

    [Fact]
    public void Serialize_APayloadWithinMaxPayloadBytes_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxPayloadBytes = 16 });

        Assert.NotEmpty(serializer.Serialize("short"));
    }

    [Fact]
    public void Deserialize_AHeaderDeclaringMoreThanMaxPayloadBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxPayloadBytes = 16 });
        byte[] frame = Wire.Frame(new byte[64]);

        AssertEx.Throws<BinaryLimitException>("UncompressedLength", () => serializer.Deserialize<int>(frame));
    }

    // --- LIM-35: MaxCompressedBytes --------------------------------------------------------------

    [Fact]
    public void Serialize_ACompressedRepresentationOverMaxCompressedBytes_ThrowsLimit()
    {
        // Without compression the compressed phase is the raw payload, so the ceiling is reached
        // with nothing but an ordinary value.
        var serializer = Limited(SerializationLimits.Default with { MaxCompressedBytes = 16 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(new string('x', 512)));
    }

    [Fact]
    public void Deserialize_AHeaderDeclaringMoreThanMaxCompressedBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCompressedBytes = 16 });
        byte[] frame = Wire.FrameWith(
            [], compression: Deflate, uncompressedLength: 4, compressedLength: 64, onDiskLength: 64);

        AssertEx.Throws<BinaryLimitException>("CompressedLength", () => serializer.Deserialize<int>(frame));
    }

    // --- LIM-36: MaxEncryptedBytes ---------------------------------------------------------------

    [Fact]
    public void Serialize_AnOnDiskRepresentationOverMaxEncryptedBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxEncryptedBytes = 16 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(new string('x', 512)));
    }

    [Fact]
    public void Deserialize_AHeaderDeclaringMoreThanMaxEncryptedBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxEncryptedBytes = 16 });
        byte[] frame = Wire.FrameWith(
            [], encryption: Aes256Gcm, uncompressedLength: 4, compressedLength: 4, onDiskLength: 64);

        AssertEx.Throws<BinaryLimitException>("OnDiskLength", () => serializer.Deserialize<int>(frame));
    }

    // --- LIM-37: MaxWireBytes --------------------------------------------------------------------

    [Fact]
    public void Serialize_ProducingMoreThanMaxWireBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 32 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(new string('x', 512)));
    }

    [Fact]
    public void Deserialize_ConsumingMoreThanMaxWireBytes_ThrowsLimit()
    {
        // A complete frame is far longer than eight bytes, so the wire meter stops the read while
        // the header is still being decoded.
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 8 });
        byte[] frame = new BinarySerializer().Serialize(123);

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_ConsumingExactlyMaxWireBytes_Succeeds()
    {
        byte[] frame = new BinarySerializer().Serialize(123);
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = frame.Length });

        Assert.Equal(123, serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_ConsumingOneByteMoreThanMaxWireBytes_ThrowsLimit()
    {
        byte[] frame = new BinarySerializer().Serialize(123);
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = frame.Length - 1 });

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<int>(frame));
    }

    // --- LIM-38: refused before the buffer exists ------------------------------------------------

    [Fact]
    public void Deserialize_AHeaderDeclaringAHugePhaseLength_AllocatesNothingProportional()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxEncryptedBytes = 16 });
        byte[] frame = Wire.FrameWith(
            [],
            encryption: Aes256Gcm,
            uncompressedLength: 4,
            compressedLength: 4,
            onDiskLength: 60 * 1024 * 1024);

        AssertEx.AllocatesLessThan(AllocationCeiling, () => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AHeaderDeclaringAHugePayloadLength_AllocatesNothingProportional()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxPayloadBytes = 16 });
        byte[] frame = Wire.Frame([], declaredLength: 60 * 1024 * 1024);

        AssertEx.AllocatesLessThan(AllocationCeiling, () => serializer.Deserialize<int>(frame));
    }

    // --- LIM-39: the check belongs to the pipeline -----------------------------------------------

    [Fact]
    public void Deserialize_AnOversizedCompressedLength_IsRefusedBeforeTheAlgorithmIsConsulted()
    {
        // The header names DEFLATE and carries no compressed bytes at all: had the decision been
        // left to the algorithm, this would have surfaced as malformed compressed data instead.
        var serializer = Limited(SerializationLimits.Default with { MaxCompressedBytes = 16 });
        byte[] frame = Wire.FrameWith(
            [], compression: Deflate, uncompressedLength: 4, compressedLength: 64, onDiskLength: 64);

        var ex = Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<int>(frame));

        Assert.Contains("CompressedLength", ex.Message, StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Deserialize_AnOversizedKeyedFieldLength_IsBoundedByMaxPayloadBytes()
    {
        // A keyed field payload is part of the logical payload, so it answers to the same ceiling.
        var serializer = Limited(SerializationLimits.Default with { MaxPayloadBytes = 16 });
        byte[] frame = Wire.Frame(
        [
            .. Wire.NotNull,
            .. Wire.KeyedBody([new Wire.KeyedField(1, [], DeclaredLength: 64)])
        ]);

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<EmptyContract>(frame));
    }
}
