using System.Collections;
using System.Numerics;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-01…LIM-09: every per-value limit gets below, exact, one above and structurally invalid,
/// in both directions. A configured zero and a wire zero are different things — the first is a
/// configuration error, the second is an empty value wherever the shape admits one.
/// </summary>
public class ValueLimitTests
{
    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    // --- LIM-01: MaxArrayLength -----------------------------------------------------------------

    [Fact]
    public void Deserialize_ArrayBelowMaxArrayLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 3 });

        Assert.Equal(2, serializer.Deserialize<int[]>(Wire.Container(2, 2))!.Length);
    }

    [Fact]
    public void Deserialize_ArrayAtMaxArrayLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 3 });

        Assert.Equal(3, serializer.Deserialize<int[]>(Wire.Container(3, 3))!.Length);
    }

    [Fact]
    public void Deserialize_ArrayOneAboveMaxArrayLength_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 3 });

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<int[]>(Wire.Container(4, 0)));
    }

    [Fact]
    public void Deserialize_ArrayWithANegativeCount_ThrowsFormat()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 3 });

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<int[]>(Wire.Container(-1, 0)));
    }

    [Fact]
    public void Serialize_ArrayOneAboveMaxArrayLength_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 3 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(new int[4]));
    }

    [Fact]
    public void Serialize_ArrayAtMaxArrayLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 3 });

        Assert.NotEmpty(serializer.Serialize(new int[3]));
    }

    // --- LIM-02: MaxCollectionLength ------------------------------------------------------------

    [Fact]
    public void Deserialize_CollectionBelowMaxCollectionLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 3 });

        Assert.Equal(2, serializer.Deserialize<List<int>>(Wire.Container(2, 2))!.Count);
    }

    [Fact]
    public void Deserialize_CollectionAtMaxCollectionLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 3 });

        Assert.Equal(3, serializer.Deserialize<List<int>>(Wire.Container(3, 3))!.Count);
    }

    [Fact]
    public void Deserialize_CollectionOneAboveMaxCollectionLength_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 3 });

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<List<int>>(Wire.Container(4, 0)));
    }

    [Fact]
    public void Deserialize_CollectionWithANegativeCount_ThrowsFormat()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 3 });

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<List<int>>(Wire.Container(-1, 0)));
    }

    [Fact]
    public void Serialize_CollectionOneAboveMaxCollectionLength_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 3 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(new List<int> { 1, 2, 3, 4 }));
    }

    [Fact]
    public void Serialize_CollectionAtMaxCollectionLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 3 });

        Assert.NotEmpty(serializer.Serialize(new List<int> { 1, 2, 3 }));
    }

    // --- LIM-03: MaxDictionaryEntries -----------------------------------------------------------

    [Fact]
    public void Deserialize_DictionaryBelowMaxDictionaryEntries_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDictionaryEntries = 2 });

        Assert.Single(serializer.Deserialize<Dictionary<int, int>>(Wire.Container(1, 2))!);
    }

    [Fact]
    public void Deserialize_DictionaryAtMaxDictionaryEntries_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDictionaryEntries = 2 });

        Assert.Equal(2, serializer.Deserialize<Dictionary<int, int>>(Wire.Container(2, 4))!.Count);
    }

    [Fact]
    public void Deserialize_DictionaryOneAboveMaxDictionaryEntries_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDictionaryEntries = 2 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<Dictionary<int, int>>(Wire.Container(3, 0)));
    }

    [Fact]
    public void Deserialize_DictionaryWithANegativeEntryCount_ThrowsFormat()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDictionaryEntries = 2 });

        Assert.Throws<BinaryFormatException>(
            () => serializer.Deserialize<Dictionary<int, int>>(Wire.Container(-1, 0)));
    }

    [Fact]
    public void Serialize_DictionaryOneAboveMaxDictionaryEntries_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDictionaryEntries = 2 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Serialize(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    // --- LIM-04: MaxStringBytes counts UTF-8 bytes ----------------------------------------------

    [Fact]
    public void Deserialize_StringBelowMaxStringBytes_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxStringBytes = 8 });

        Assert.Equal("abcd", serializer.Deserialize<string>(Wire.StringValue(4, "abcd"u8.ToArray())));
    }

    [Fact]
    public void Deserialize_StringAtMaxStringBytes_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxStringBytes = 8 });

        Assert.Equal(
            "abcdefgh",
            serializer.Deserialize<string>(Wire.StringValue(8, "abcdefgh"u8.ToArray())));
    }

    [Fact]
    public void Deserialize_StringOneAboveMaxStringBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxStringBytes = 8 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<string>(Wire.StringValue(9, "abcdefghi"u8.ToArray())));
    }

    [Fact]
    public void Serialize_StringOfFiveTwoByteCharacters_ExceedsAnEightByteLimit()
    {
        // Five two-byte characters are ten UTF-8 bytes, so a character count would have admitted them.
        var serializer = Limited(SerializationLimits.Default with { MaxStringBytes = 8 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize("ééééé"));
    }

    [Fact]
    public void Serialize_StringOfEightAsciiCharacters_FitsTheSameLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxStringBytes = 8 });

        Assert.NotEmpty(serializer.Serialize("abcdefgh"));
    }

    [Fact]
    public void Deserialize_StringOfFiveTwoByteCharacters_IsMeasuredInBytes()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxStringBytes = 8 });
        byte[] encoded = System.Text.Encoding.UTF8.GetBytes("ééééé");

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<string>(Wire.StringValue(encoded.Length, encoded)));
    }

    // --- LIM-05: MaxByteBlobBytes ---------------------------------------------------------------

    [Fact]
    public void Deserialize_BlobBelowMaxByteBlobBytes_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 4 });

        Assert.Equal(16, serializer.Deserialize<BitArray>(Wire.BitArrayValue(16, 2))!.Length);
    }

    [Fact]
    public void Deserialize_BlobAtMaxByteBlobBytes_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 4 });

        Assert.Equal(32, serializer.Deserialize<BitArray>(Wire.BitArrayValue(32, 4))!.Length);
    }

    [Fact]
    public void Deserialize_BlobOneAboveMaxByteBlobBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 4 });
        // BigInteger is a value type, so the blob length is the first byte of the payload.
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write7BitEncodedInt(5);       // the magnitude declares five bytes
            writer.Write(new byte[5]);
        }));

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<BigInteger>(frame));
    }

    [Fact]
    public void Deserialize_ZeroLengthBlob_YieldsTheEmptyValue()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 4 });
        byte[] frame = Wire.Frame(Wire.Payload(writer => writer.Write7BitEncodedInt(0)));

        Assert.Equal(BigInteger.Zero, serializer.Deserialize<BigInteger>(frame));
    }

    [Fact]
    public void Serialize_BlobOneAboveMaxByteBlobBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 4 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(BigInteger.Pow(2, 64)));
    }

    // --- LIM-06: bit counts ---------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void Serialize_Deserialize_BitArrayAcrossAByteBoundary_RoundTrips(int bits)
    {
        var original = new BitArray(bits);
        for (int index = 0; index < bits; index += 2)
            original[index] = true;

        var serializer = new BinarySerializer();

        var restored = serializer.Deserialize<BitArray>(serializer.Serialize(original));

        Assert.Equal(bits, restored!.Length);
        for (int index = 0; index < bits; index++)
            Assert.Equal(original[index], restored[index]);
    }

    [Fact]
    public void Deserialize_BitCountAtTheBlobCeiling_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 2 });

        Assert.Equal(16, serializer.Deserialize<BitArray>(Wire.BitArrayValue(16, 2))!.Length);
    }

    [Fact]
    public void Deserialize_BitCountOneAboveTheBlobCeiling_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 2 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<BitArray>(Wire.BitArrayValue(17, 3)));
    }

    [Fact]
    public void Deserialize_NegativeBitCount_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<BitArray>(Wire.BitArrayValue(-1, 0)));
    }

    [Fact]
    public void Serialize_BitCountOneAboveTheBlobCeiling_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 2 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(new BitArray(17)));
    }

    // --- LIM-07 / LIM-08: the two structurally invalid declarations -----------------------------

    [Fact]
    public void Deserialize_NegativeCount_IsAFormatErrorAndNotALimitError()
    {
        // BinaryLimitException derives from BinaryFormatException, so the exact type is the assertion.
        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<List<int>>(Wire.Container(-1, 0)));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_NegativeKeyedFieldLength_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(
        [
            .. Wire.NotNull,
            .. Wire.KeyedBody([new Wire.KeyedField(1, [0, 0, 0, 0], DeclaredLength: -4)])
        ]);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<EmptyContract>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_NegativeHeaderPhaseLength_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith([], uncompressedLength: -1, compressedLength: -1, onDiskLength: -1);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_StringLengthOverflowingInt32_ThrowsFormat()
    {
        // Five bytes carrying a 7-bit value above Int32.MaxValue, which the encoding admits
        // physically but the format does not.
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F });
        }));

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<string>(frame));
    }

    // --- LIM-09: a wire zero is an empty value --------------------------------------------------

    [Fact]
    public void Deserialize_ZeroLengthArray_YieldsAnEmptyArray()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 3 });

        Assert.Empty(serializer.Deserialize<int[]>(Wire.Container(0, 0))!);
    }

    [Fact]
    public void Deserialize_ZeroLengthCollection_YieldsAnEmptyCollection()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 3 });

        Assert.Empty(serializer.Deserialize<List<int>>(Wire.Container(0, 0))!);
    }

    [Fact]
    public void Deserialize_ZeroEntryDictionary_YieldsAnEmptyDictionary()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDictionaryEntries = 2 });

        Assert.Empty(serializer.Deserialize<Dictionary<int, int>>(Wire.Container(0, 0))!);
    }

    [Fact]
    public void Deserialize_ZeroLengthString_YieldsAnEmptyString()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxStringBytes = 8 });

        Assert.Equal(string.Empty, serializer.Deserialize<string>(Wire.StringValue(0)));
    }

    [Fact]
    public void Deserialize_ZeroBitBitArray_YieldsAnEmptyBitArray()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxByteBlobBytes = 2 });

        Assert.Equal(0, serializer.Deserialize<BitArray>(Wire.BitArrayValue(0, 0))!.Length);
    }
}
