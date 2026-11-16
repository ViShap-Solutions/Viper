using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Format;
using ViShap.Viper.Io;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins HDR-34…HDR-37: the header of a frame with no service record is written and read by its own
/// short path, and that path agrees with the general header writer and reader byte for byte and value
/// for value, across every width of the length and both payload modes. Whatever the short reader cannot
/// decide is left to the general reader, so every failure keeps the type the format gives it.
/// </summary>
public class PlainHeaderTests
{
    /// <summary>Lengths on both sides of every boundary of the 7-bit encoding, up to the largest.</summary>
    public static TheoryData<int, bool> Lengths()
    {
        var data = new TheoryData<int, bool>();
        foreach (int length in new[] { 0, 1, 127, 128, 16_383, 16_384, 2_097_151, 2_097_152, 268_435_455, 268_435_456, int.MaxValue })
        {
            data.Add(length, false);
            data.Add(length, true);
        }

        return data;
    }

    private static BinaryFormatHeaderV1 Plain(bool preserveReferences, int onDiskLength) =>
        new(preserveReferences,
            ChecksumAlgorithm.None, null, [],
            CompressionAlgorithm.None, null, 0,
            EncryptionAlgorithm.None, null, null,
            onDiskLength);

    private static byte[] General(BinaryFormatHeaderV1 header)
    {
        var box = new OperationBox();
        using var buffer = new PayloadBuffer(BinaryFormatHeaderV1.MaxLength, "header");
        var writer = new WireWriter(buffer, ref box.State);
        header.WriteTo(ref writer);
        writer.Flush();
        return buffer.ToArray();
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void WritePlain_EveryLengthAndMode_WritesTheGeneralWritersBytes(int onDiskLength, bool preserveReferences)
    {
        Span<byte> plain = stackalloc byte[BinaryFormatHeaderV1.PlainMaxLength];

        int written = BinaryFormatHeaderV1.WritePlain(plain, preserveReferences, onDiskLength);

        Assert.Equal(General(Plain(preserveReferences, onDiskLength)), plain[..written].ToArray());
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void TryReadPlain_EveryLengthAndMode_ReadsWhatTheGeneralReaderReads(int onDiskLength, bool preserveReferences)
    {
        byte[] bytes = General(Plain(preserveReferences, onDiskLength));
        var box = new OperationBox(SerializationLimits.Default with
        {
            MaxEncryptedBytes = int.MaxValue,
            MaxCompressedBytes = int.MaxValue,
            MaxPayloadBytes = int.MaxValue,
        });
        var reader = new WireReader(bytes, ref box.State);
        var general = BinaryFormatHeaderV1.ReadFrom(ref reader);

        bool read = BinaryFormatHeaderV1.TryReadPlain(bytes, out var plain, out int headerLength);

        Assert.True(read);
        Assert.Equal(reader.Consumed, (long)headerLength);
        Assert.Equal(general.OnDiskLength, plain.OnDiskLength);
        Assert.Equal(general.PreserveReferences, plain.PreserveReferences);
        Assert.Equal(ChecksumAlgorithm.None, plain.ChecksumAlgorithm);
        Assert.Equal(CompressionAlgorithm.None, plain.Compression);
        Assert.Equal(EncryptionAlgorithm.None, plain.Encryption);
    }

    public static TheoryData<string, byte[]> Undecided() => new()
    {
        { "fewer bytes than the shortest header", [0x42, 0x53, 0x45, 0x52, 0x01, 0x00, 0x00] },
        { "another magic number", [0x42, 0x53, 0x45, 0x53, 0x01, 0x00, 0x00, 0x05] },
        { "another version", [0x42, 0x53, 0x45, 0x52, 0x02, 0x00, 0x00, 0x05] },
        { "a payload mode with an undefined bit", [0x42, 0x53, 0x45, 0x52, 0x01, 0x02, 0x00, 0x05] },
        { "a payload mode spelled in two bytes", [0x42, 0x53, 0x45, 0x52, 0x01, 0x80, 0x00, 0x00, 0x05] },
        { "a service record", [0x42, 0x53, 0x45, 0x52, 0x01, 0x00, 0x01, 0x03, 0x01, 0x01, 0x05] },
        { "a length that is not minimal", [0x42, 0x53, 0x45, 0x52, 0x01, 0x00, 0x00, 0x85, 0x00] },
        { "a length that is cut off", [0x42, 0x53, 0x45, 0x52, 0x01, 0x00, 0x00, 0x85] },
        { "a length past five bytes", [0x42, 0x53, 0x45, 0x52, 0x01, 0x00, 0x00, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01] },
        { "a length above Int32.MaxValue", [0x42, 0x53, 0x45, 0x52, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F] },
    };

    [Theory]
    [MemberData(nameof(Undecided))]
    public void TryReadPlain_AHeaderItCannotDecide_LeavesItToTheGeneralReader(string what, byte[] prefix)
    {
        Assert.False(BinaryFormatHeaderV1.TryReadPlain(prefix, out _, out _), what);
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16_383)]
    [InlineData(16_384)]
    public void Serialize_APayloadOnEitherSideOfALengthBoundary_WritesTheGeneralHeader(int payloadLength)
    {
        var serializer = new BinarySerializer();
        int characters = payloadLength - 1;
        while (Wire.Varint(characters + 1).Length + characters > payloadLength)
            characters--;
        string text = new('x', characters);

        byte[] frame = serializer.Serialize(text);
        var header = Wire.ReadHeader(frame);

        Assert.InRange(frame.Length - header.HeaderLength, payloadLength - 1, payloadLength);
        Assert.Equal(General(Plain(preserveReferences: false, frame.Length - header.HeaderLength)), frame[..header.HeaderLength]);
        Assert.Equal(text, serializer.Deserialize<string>(frame));
    }
}
