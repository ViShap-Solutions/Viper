using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins WF-01, WF-02, WF-04 and WF-21…WF-24: every row of the §22.1 and §22.4 tables, asserted at
/// the byte level against the payload a V1 frame carries. A second implementation is written from
/// this file.
/// </summary>
public class ScalarWireTests
{
    private readonly BinarySerializer _serializer = new();

    /// <summary>The payload bytes of a V1 frame, with the fixed-length header removed.</summary>
    private byte[] Payload<T>(T value) => _serializer.Serialize(value)[Wire.PlainHeaderLength..];

    // --- WF-01, WF-02: the fixed-size primitives of §22.1 ---------------------------------------

    [Fact]
    public void Booleans_AreOneByte()
    {
        Assert.Equal<byte[]>([1], Payload(true));
        Assert.Equal<byte[]>([0], Payload(false));
    }

    [Fact]
    public void EightBitIntegers_AreOneByte()
    {
        Assert.Equal<byte[]>([0xAB], Payload((byte)0xAB));
        Assert.Equal<byte[]>([0xFE], Payload((sbyte)-2));
    }

    [Fact]
    public void SixteenBitValues_AreTwoBytesLittleEndian()
    {
        Assert.Equal<byte[]>([0xFE, 0xFF], Payload((short)-2));
        Assert.Equal<byte[]>([0x34, 0x12], Payload((ushort)0x1234));
        Assert.Equal<byte[]>([0x41, 0x00], Payload('A'));
    }

    [Fact]
    public void ThirtyTwoBitValues_AreFourBytesLittleEndian()
    {
        Assert.Equal<byte[]>([0x44, 0x33, 0x22, 0x11], Payload(0x11223344));
        Assert.Equal<byte[]>([0xEF, 0xCD, 0xAB, 0x89], Payload(0x89ABCDEFu));
        Assert.Equal<byte[]>([0x00, 0x00, 0xC0, 0x3F], Payload(1.5f));
    }

    [Fact]
    public void SixtyFourBitValues_AreEightBytesLittleEndian()
    {
        Assert.Equal<byte[]>([0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11], Payload(0x1122334455667788L));
        Assert.Equal<byte[]>([0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88], Payload(0x8899AABBCCDDEEFFUL));
        Assert.Equal<byte[]>([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF8, 0x3F], Payload(1.5d));
    }

    [Fact]
    public void Decimal_IsFourInt32InGetBitsOrder()
    {
        // 1.5m is 15 with a scale of 1: lo = 15, mid = 0, hi = 0, flags = 0x00010000.
        Assert.Equal<byte[]>(
            [
                0x0F, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x01, 0x00
            ],
            Payload(1.5m));
    }

    // --- WF-04: a blob is a 7-bit length prefix then bytes --------------------------------------

    [Fact]
    public void BigInteger_IsABlobOfItsTwosComplementBytes()
    {
        // 300 is 0x012C, written little-endian by BigInteger.TryWriteBytes.
        Assert.Equal<byte[]>([0x02, 0x2C, 0x01], Payload(new BigInteger(300)));
        Assert.Equal<byte[]>([0x01, 0x00], Payload(BigInteger.Zero));
        Assert.Equal<byte[]>([0x01, 0xFF], Payload(BigInteger.MinusOne));
    }

    // --- WF-21: the remaining rows of the §22.4 table -------------------------------------------

    [Fact]
    public void Half_IsTheInt16OfItsBits()
    {
        Assert.Equal<byte[]>([0x00, 0x3E], Payload((Half)1.5));
    }

    [Fact]
    public void Int128AndUInt128_AreSixteenBytesLittleEndian()
    {
        Assert.Equal<byte[]>(
            [0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11,
             0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01],
            Payload(new Int128(0x0102030405060708, 0x1112131415161718)));

        Assert.Equal<byte[]>(
            [0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            Payload(UInt128.One));
    }

    [Fact]
    public void NativeIntegers_AreSixtyFourBitWhateverThePlatform()
    {
        Assert.Equal<byte[]>([0x2A, 0, 0, 0, 0, 0, 0, 0], Payload(new IntPtr(42)));
        Assert.Equal<byte[]>([0x2A, 0, 0, 0, 0, 0, 0, 0], Payload(new UIntPtr(42)));
    }

    [Fact]
    public void Rune_IsItsInt32ScalarValue()
    {
        Assert.Equal<byte[]>([0x41, 0x00, 0x00, 0x00], Payload(new Rune('A')));
        Assert.Equal<byte[]>([0x00, 0xF6, 0x01, 0x00], Payload(new Rune(0x1F600)));
    }

    [Fact]
    public void TimeValues_AreTheirDocumentedIntegers()
    {
        var moment = new DateTime(2026, 9, 18, 12, 30, 0, DateTimeKind.Utc);
        var offset = new DateTimeOffset(2026, 9, 18, 12, 30, 0, TimeSpan.FromHours(-5));

        Assert.Equal(Wire.Payload(writer => writer.Write(moment.ToBinary())), Payload(moment));

        Assert.Equal(
            Wire.Payload(writer =>
            {
                writer.Write(offset.Ticks);
                writer.Write(offset.Offset.Ticks);
            }),
            Payload(offset));

        Assert.Equal(
            Wire.Payload(writer => writer.Write(TimeSpan.FromMinutes(90).Ticks)),
            Payload(TimeSpan.FromMinutes(90)));

        Assert.Equal(
            Wire.Payload(writer => writer.Write(new DateOnly(2026, 9, 18).DayNumber)),
            Payload(new DateOnly(2026, 9, 18)));

        Assert.Equal(
            Wire.Payload(writer => writer.Write(new TimeOnly(12, 30).Ticks)),
            Payload(new TimeOnly(12, 30)));
    }

    [Fact]
    public void TimeZoneInfo_IsItsSerializedString()
    {
        Assert.Equal(
            Wire.Payload(writer =>
            {
                writer.Write(true);
                writer.Write(TimeZoneInfo.Utc.ToSerializedString());
            }),
            Payload(TimeZoneInfo.Utc));
    }

    [Fact]
    public void Guid_IsSixteenBytesInItsWriteBytesLayout()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        byte[] expected = new byte[16];
        id.TryWriteBytes(expected);

        Assert.Equal(expected, Payload(id));
        Assert.Equal<byte[]>(
            [0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66,
             0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF],
            Payload(id));
    }

    [Fact]
    public void TextLikeValues_AreStrings()
    {
        Assert.Equal(Text("https://example.test/a?b=c"), Payload(new Uri("https://example.test/a?b=c")));
        Assert.Equal(Text("1.2.3.4"), Payload(new Version(1, 2, 3, 4)));
        Assert.Equal(Text("text"), Payload(new StringBuilder("text")));
        Assert.Equal(Text("en-GB"), Payload(CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal(Text(string.Empty), Payload(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Complex_IsRealThenImaginary()
    {
        Assert.Equal(
            Wire.Payload(writer =>
            {
                writer.Write(1.5d);
                writer.Write(-2.5d);
            }),
            Payload(new Complex(1.5, -2.5)));
    }

    [Fact]
    public void VectorTypes_AreTheirComponentsInDeclarationOrder()
    {
        Assert.Equal(Floats(1, 2), Payload(new Vector2(1, 2)));
        Assert.Equal(Floats(1, 2, 3), Payload(new Vector3(1, 2, 3)));
        Assert.Equal(Floats(1, 2, 3, 4), Payload(new Vector4(1, 2, 3, 4)));
        Assert.Equal(Floats(1, 2, 3, 4), Payload(new Quaternion(1, 2, 3, 4)));
        Assert.Equal(Floats(1, 2, 3, 4), Payload(new Plane(new Vector3(1, 2, 3), 4)));
        Assert.Equal(Floats(1, 2, 3, 4, 5, 6), Payload(new Matrix3x2(1, 2, 3, 4, 5, 6)));
    }

    [Fact]
    public void Matrix4x4_IsSixteenFloatsRowMajor()
    {
        var matrix = new Matrix4x4(
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16);

        Assert.Equal(Floats(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16), Payload(matrix));
    }

    // --- WF-22: an enum is its underlying primitive ---------------------------------------------

    [Fact]
    public void Enums_AreEncodedAsTheirUnderlyingPrimitive()
    {
        Assert.Equal(Payload((byte)0xAB), Payload(ByteEnum.Value));
        Assert.Equal(Payload((sbyte)-2), Payload(SByteEnum.Value));
        Assert.Equal(Payload((short)-2), Payload(ShortEnum.Value));
        Assert.Equal(Payload((ushort)0x1234), Payload(UShortEnum.Value));
        Assert.Equal(Payload(0x11223344), Payload(IntEnum.Value));
        Assert.Equal(Payload(0x89ABCDEFu), Payload(UIntEnum.Value));
        Assert.Equal(Payload(0x1122334455667788L), Payload(LongEnum.Value));
        Assert.Equal(Payload(0x8899AABBCCDDEEFFUL), Payload(ULongEnum.Value));
    }

    // --- WF-23: an invalid Rune scalar is malformed input ---------------------------------------

    [Theory]
    [InlineData(0x110000)]   // above the Unicode range
    [InlineData(0xD800)]     // a surrogate code point
    [InlineData(-1)]
    public void Deserialize_RuneWithAnInvalidScalarValue_ThrowsFormat(int scalar)
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer => writer.Write(scalar)));

        AssertEx.Throws<BinaryFormatException>(
            "not a valid Unicode scalar", () => _serializer.Deserialize<Rune>(frame));
    }

    // --- WF-24: a BitArray is a bit count then a blob of the packed bytes ------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    public void BitArray_IsABitCountThenCeilingOfBitsOverEightBytes(int bits, int expectedBytes)
    {
        byte[] payload = Payload(new BitArray(bits, defaultValue: false));

        Assert.Equal<byte[]>([1], payload[..1]);                         // non-null
        Assert.Equal(bits, BitConverter.ToInt32(payload, 1));            // int32 bit count
        Assert.Equal(expectedBytes, payload[5]);                         // 7-bit blob length
        Assert.Equal(6 + expectedBytes, payload.Length);
    }

    [Fact]
    public void BitArray_PacksItsBitsLeastSignificantFirst()
    {
        // Bits 0 and 2 set in a nine-bit array: 0b0000_0101 then a byte holding bit 8.
        byte[] payload = Payload(new BitArray([true, false, true, false, false, false, false, false, true]));

        Assert.Equal<byte[]>([1, 9, 0, 0, 0, 2, 0x05, 0x01], payload);
    }

    /// <summary>A non-null string value: the null flag, then the §22.1 string encoding.</summary>
    private static byte[] Text(string value) =>
        Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(value);
        });

    private static byte[] Floats(params float[] values) =>
        Wire.Payload(writer =>
        {
            foreach (float value in values)
                writer.Write(value);
        });
}
