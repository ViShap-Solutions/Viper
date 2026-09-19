using System.Numerics;
using System.Text;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// Plan §19.1: RT-01…RT-22 and the boundaries RT-B01…RT-B06, over contract §23's primitive family.
/// Every integral type carries zero, its two extremes and, where it is signed, minus one; the
/// floating-point types carry their special values bit for bit.
/// </summary>
public abstract partial class Corpus
{
    [Fact]
    public void Deserialize_Boolean_RestoresBothStates()
    {
        Assert.True(RoundTrip(true));
        Assert.False(RoundTrip(false));
    }

    [Fact]
    public void Deserialize_Byte_RestoresTheBoundaries() =>
        AssertRoundTrips<byte>(0, 1, 0x7F, 0x80, byte.MaxValue);

    [Fact]
    public void Deserialize_SByte_RestoresTheBoundaries() =>
        AssertRoundTrips<sbyte>(0, -1, 1, sbyte.MinValue, sbyte.MaxValue);

    [Fact]
    public void Deserialize_Int16_RestoresTheBoundaries() =>
        AssertRoundTrips<short>(0, -1, 1, short.MinValue, short.MaxValue);

    [Fact]
    public void Deserialize_UInt16_RestoresTheBoundaries() =>
        AssertRoundTrips<ushort>(0, 1, 0x1234, ushort.MaxValue);

    [Fact]
    public void Deserialize_Int32_RestoresTheBoundaries() =>
        AssertRoundTrips(0, -1, 1, int.MinValue, int.MaxValue);

    [Fact]
    public void Deserialize_UInt32_RestoresTheBoundaries() =>
        AssertRoundTrips(0u, 1u, 0x89ABCDEFu, uint.MaxValue);

    [Fact]
    public void Deserialize_Int64_RestoresTheBoundaries() =>
        AssertRoundTrips(0L, -1L, 1L, long.MinValue, long.MaxValue);

    [Fact]
    public void Deserialize_UInt64_RestoresTheBoundaries() =>
        AssertRoundTrips(0UL, 1UL, 0x8899AABBCCDDEEFFUL, ulong.MaxValue);

    [Fact]
    public void Deserialize_Single_RestoresTheBoundaries() =>
        AssertRoundTrips(0f, -1f, 1f, float.MinValue, float.MaxValue, float.Epsilon);

    [Fact]
    public void Deserialize_Single_RestoresEverySpecialValueBitForBit()
    {
        // RT-B02: negative zero equals zero under ==, so only the bits show that it survived.
        AssertSingleBits(float.NaN);
        AssertSingleBits(float.PositiveInfinity);
        AssertSingleBits(float.NegativeInfinity);
        AssertSingleBits(-0f);

        void AssertSingleBits(float value) =>
            Assert.Equal(
                BitConverter.SingleToInt32Bits(value),
                BitConverter.SingleToInt32Bits(RoundTrip(value)));
    }

    [Fact]
    public void Deserialize_Double_RestoresTheBoundaries() =>
        AssertRoundTrips(0d, -1d, 1d, double.MinValue, double.MaxValue, double.Epsilon);

    [Fact]
    public void Deserialize_Double_RestoresEverySpecialValueBitForBit()
    {
        AssertDoubleBits(double.NaN);
        AssertDoubleBits(double.PositiveInfinity);
        AssertDoubleBits(double.NegativeInfinity);
        AssertDoubleBits(-0d);

        void AssertDoubleBits(double value) =>
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(value),
                BitConverter.DoubleToInt64Bits(RoundTrip(value)));
    }

    [Fact]
    public void Deserialize_Decimal_RestoresTheBoundaries() =>
        AssertRoundTrips(0m, -1m, 1m, decimal.MinValue, decimal.MaxValue, 12345.6789m);

    [Fact]
    public void Deserialize_Decimal_RestoresTheScale()
    {
        // §22.1 writes the four int32 of GetBits, flags included, so the scale and the sign of a
        // negative zero survive values that compare equal under ==.
        Assert.Equal(decimal.GetBits(1.00m), decimal.GetBits(RoundTrip(1.00m)));
        Assert.Equal(decimal.GetBits(-0.0m), decimal.GetBits(RoundTrip(-0.0m)));
        Assert.NotEqual(decimal.GetBits(1.0m), decimal.GetBits(RoundTrip(1.00m)));
    }

    [Fact]
    public void Deserialize_Char_RestoresTheBoundaries() =>
        AssertRoundTrips('\0', 'A', 'ß', '￿', '\uD800');

    [Fact]
    public void Deserialize_String_RestoresTheBoundaries()
    {
        // RT-B03: empty, a surrogate pair, and text whose UTF-8 length exceeds its char count.
        AssertRoundTrips(string.Empty, "a", "héllo ☃", "𝄞");
        Assert.Equal("𝄞", RoundTrip("𝄞"));
    }

    [Fact]
    public void Deserialize_StringOfExactlyMaxStringBytes_RoundTrips()
    {
        var limits = SerializationLimits.Default with { MaxStringBytes = 16 };

        // Four three-byte characters plus four one-byte ones are exactly sixteen UTF-8 bytes.
        string exact = new string('☃', 4) + new string('a', 4);
        Assert.Equal(16, Encoding.UTF8.GetByteCount(exact));

        Assert.Equal(exact, RoundTrip(exact, limits));
    }

    [Fact]
    public void Deserialize_Enum_TravelsAsEachUnderlyingIntegralType()
    {
        AssertRoundTrips(ByteEnum.Value);
        AssertRoundTrips(SByteEnum.Value);
        AssertRoundTrips(ShortEnum.Value);
        AssertRoundTrips(UShortEnum.Value);
        AssertRoundTrips(IntEnum.Value);
        AssertRoundTrips(UIntEnum.Value);
        AssertRoundTrips(LongEnum.Value);
        AssertRoundTrips(ULongEnum.Value);
    }

    [Fact]
    public void Deserialize_EnumValueThatIsNotDeclared_RestoresTheUnderlyingNumber()
    {
        // RT-B05: an enum travels as its underlying primitive, so an undefined value is data, not an error.
        Assert.Equal((IntEnum)0x7FFFFFFF, RoundTrip((IntEnum)0x7FFFFFFF));
        Assert.Equal((ByteEnum)0xFF, RoundTrip((ByteEnum)0xFF));
        Assert.Equal((SByteEnum)(-128), RoundTrip((SByteEnum)(-128)));
    }

    [Fact]
    public void Deserialize_Half_RestoresTheBoundaries() =>
        AssertRoundTrips(
            (Half)0, (Half)(-1), (Half)1.5, Half.MinValue, Half.MaxValue, Half.Epsilon);

    [Fact]
    public void Deserialize_Half_RestoresEverySpecialValueBitForBit()
    {
        AssertHalfBits(Half.NaN);
        AssertHalfBits(Half.PositiveInfinity);
        AssertHalfBits(Half.NegativeInfinity);
        AssertHalfBits(Half.NegativeZero);

        void AssertHalfBits(Half value) =>
            Assert.Equal(
                BitConverter.HalfToInt16Bits(value),
                BitConverter.HalfToInt16Bits(RoundTrip(value)));
    }

    [Fact]
    public void Deserialize_Int128_RestoresTheBoundaries() =>
        AssertRoundTrips(
            Int128.Zero, Int128.NegativeOne, Int128.One, Int128.MinValue, Int128.MaxValue);

    [Fact]
    public void Deserialize_UInt128_RestoresTheBoundaries() =>
        AssertRoundTrips(UInt128.Zero, UInt128.One, UInt128.MaxValue);

    [Fact]
    public void Deserialize_IntPtr_RestoresTheBoundaries() =>
        AssertRoundTrips(IntPtr.Zero, new IntPtr(-1), new IntPtr(42), IntPtr.MinValue, IntPtr.MaxValue);

    [Fact]
    public void Deserialize_UIntPtr_RestoresTheBoundaries() =>
        AssertRoundTrips(UIntPtr.Zero, new UIntPtr(42), UIntPtr.MinValue, UIntPtr.MaxValue);

    [Fact]
    public void Deserialize_Rune_RestoresTheBoundaries() =>
        AssertRoundTrips(new Rune(0), new Rune('A'), new Rune(0xFFFF), new Rune(0x10FFFF));

    [Fact]
    public void Deserialize_BigInteger_RestoresEveryMagnitude() =>
        // RT-B04: zero is one byte, the powers of two are multi-byte, and the sign is part of the blob.
        AssertRoundTrips(
            BigInteger.Zero,
            BigInteger.One,
            BigInteger.MinusOne,
            new BigInteger(long.MaxValue) + 1,
            BigInteger.Pow(2, 200),
            -BigInteger.Pow(2, 200));

    [Fact]
    public void Deserialize_NullableValueType_RestoresNullAndTheValue()
    {
        // RT-B06: one nullable per value-type family — primitive, wide numeric, enum, time, composite.
        AssertNullable<int>(7);
        AssertNullable<double>(1.5);
        AssertNullable<decimal>(1.5m);
        AssertNullable<Half>((Half)1.5);
        AssertNullable<Int128>(Int128.MaxValue);
        AssertNullable<char>('x');
        AssertNullable<IntEnum>(IntEnum.Value);
        AssertNullable<Guid>(Guid.Parse("8d0c1b2a-3e4f-5061-7283-94a5b6c7d8e9"));
        AssertNullable<DateTime>(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc));
        AssertNullable<TimeSpan>(TimeSpan.FromMinutes(90));
        AssertNullable<Vector2>(new Vector2(1, 2));
        AssertNullable<(int, string)>((1, "a"));

        void AssertNullable<T>(T value)
            where T : struct
        {
            Assert.Equal(value, RoundTrip<T?>(value));
            Assert.Null(RoundTrip<T?>(null));
        }
    }

    [Fact]
    public void Deserialize_NullReference_RestoresNull()
    {
        Assert.Null(RoundTrip<string?>(null));
        Assert.Null(RoundTrip<Person?>(null));
        Assert.Null(RoundTrip<List<int>?>(null));
    }
}
