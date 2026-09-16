using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

public sealed class PrimitiveRoundTripTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Bool(bool value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Theory]
    [InlineData(byte.MinValue)] [InlineData(byte.MaxValue)] [InlineData(127)]
    public void Byte(byte value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Theory]
    [InlineData(sbyte.MinValue)] [InlineData(sbyte.MaxValue)] [InlineData(-1)] [InlineData(0)]
    public void SByte(sbyte value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Theory]
    [InlineData(short.MinValue)] [InlineData(short.MaxValue)] [InlineData(-1)] [InlineData(0)]
    public void Short(short value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Theory]
    [InlineData(ushort.MinValue)] [InlineData(ushort.MaxValue)] [InlineData(0)]
    public void UShort(ushort value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Theory]
    [InlineData(int.MinValue)] [InlineData(int.MaxValue)] [InlineData(-1)] [InlineData(0)] [InlineData(42)]
    public void Int(int value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Theory]
    [InlineData(uint.MinValue)] [InlineData(uint.MaxValue)] [InlineData(42u)]
    public void UInt(uint value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Theory]
    [InlineData(long.MinValue)] [InlineData(long.MaxValue)] [InlineData(-1)] [InlineData(0)]
    public void Long(long value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Theory]
    [InlineData(ulong.MinValue)] [InlineData(ulong.MaxValue)] [InlineData(42ul)]
    public void ULong(ulong value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Fact] public void FloatSpecialValues()
    {
        foreach (var value in new[] { 0f, -0f, 1.25f, -1.25f, float.MinValue, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var actual = TestHelpers.RoundTrip(value);
            Assert.Equal(BitConverter.SingleToInt32Bits(value), BitConverter.SingleToInt32Bits(actual));
        }
    }

    [Fact] public void DoubleSpecialValues()
    {
        foreach (var value in new[] { 0d, -0d, 1.25d, -1.25d, double.MinValue, double.MaxValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var actual = TestHelpers.RoundTrip(value);
            Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(actual));
        }
    }

    [Fact] public void DecimalBoundaries()
    {
        foreach (var value in new[] { 0m, decimal.MinValue, decimal.MaxValue, 1234567890.1234567890123456789m })
            Assert.Equal(value, TestHelpers.RoundTrip(value));
    }

    [Fact] public void CharBoundaryAndSurrogateCodeUnits()
    {
        foreach (var value in new[] { '\0', 'A', 'Ж', '\uD800', '\uDFFF' })
            Assert.Equal(value, TestHelpers.RoundTrip(value));
    }

    [Fact] public void StringUnicodeNulAndConfiguredMaximum()
    {
        foreach (var value in new[] { "", "ASCII", "Привет世界", "e\u0301", "😀", "a\0b" })
            Assert.Equal(value, TestHelpers.RoundTrip(value));

        var options = BinarySerializerOptions.Configure()
            .WithLimits(new DeserializationLimits
            {
                MaxDepth = 16, MaxArrayLength = 64, MaxCollectionLength = 64,
                MaxDictionaryEntries = 64, MaxStringLength = 8,
                MaxByteBlobLength = 64, MaxTotalElements = 256, MaxMessageBytes = 1024
            })
            .Build();
        var maximum = new string('x', 8);
        Assert.Equal(maximum, TestHelpers.RoundTrip(maximum, options));
    }

    public enum SampleEnum : long { Zero = 0, Negative = -1, Large = 123456789 }
    [Theory]
    [InlineData(SampleEnum.Zero)] [InlineData(SampleEnum.Negative)] [InlineData(SampleEnum.Large)]
    public void Enum(SampleEnum value) => Assert.Equal(value, TestHelpers.RoundTrip(value));

    [Fact] public void HalfRoundTrip() => Assert.Equal((Half)1.5, TestHelpers.RoundTrip((Half)1.5));
    [Fact] public void Int128Value() => Assert.Equal(Int128.Parse("-170141183460469231731687303715884105728"), TestHelpers.RoundTrip(Int128.MinValue));
    [Fact] public void UInt128Value() => Assert.Equal(UInt128.MaxValue, TestHelpers.RoundTrip(UInt128.MaxValue));
    [Fact] public void IntPtrValue() => Assert.Equal(new IntPtr(-123456), TestHelpers.RoundTrip(new IntPtr(-123456)));
    [Fact] public void UIntPtrValue() => Assert.Equal(new UIntPtr(123456), TestHelpers.RoundTrip(new UIntPtr(123456)));
    [Fact] public void RuneRoundTrip() => Assert.Equal(new Rune(0x1F600), TestHelpers.RoundTrip(new Rune(0x1F600)));
    [Fact] public void BigIntegerRoundTrip()
    {
        var representative = BigInteger.Parse("-123456789012345678901234567890");
        Assert.Equal(representative, TestHelpers.RoundTrip(representative));

        var exactBlob = BigInteger.One << 55;
        Assert.Equal(8, exactBlob.ToByteArray().Length);
        var options = BinarySerializerOptions.Configure()
            .WithLimits(new DeserializationLimits
            {
                MaxDepth = 16, MaxArrayLength = 64, MaxCollectionLength = 64,
                MaxDictionaryEntries = 64, MaxStringLength = 64,
                MaxByteBlobLength = 8, MaxTotalElements = 256, MaxMessageBytes = 1024
            })
            .Build();
        Assert.Equal(exactBlob, TestHelpers.RoundTrip(exactBlob, options));
    }
}

public sealed class TimeRoundTripTests
{
    [Fact] public void DateTimePreservesValueAndKindRoundTrip()
    {
        foreach (var value in new[]
        {
            new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Local),
            new DateTime(2026, 2, 28, 12, 34, 56, 789, DateTimeKind.Unspecified)
        })
        {
            var actual = TestHelpers.RoundTrip(value);
            Assert.Equal(value, actual);
            Assert.Equal(value.Kind, actual.Kind);
        }
    }

    [Fact] public void DateTimeOffsetExtremeOffsetsRoundTrip()
    {
        foreach (var value in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(14)) })
            Assert.Equal(value, TestHelpers.RoundTrip(value));
    }

    [Fact] public void TimeSpanBoundariesRoundTrip()
    {
        foreach (var value in new[] { TimeSpan.Zero, TimeSpan.MinValue, TimeSpan.MaxValue, TimeSpan.FromTicks(-1), TimeSpan.FromTicks(1234567) })
            Assert.Equal(value, TestHelpers.RoundTrip(value));
    }

    [Fact] public void DateOnlyBoundariesAndLeapDayRoundTrip()
    {
        foreach (var value in new[] { DateOnly.MinValue, DateOnly.MaxValue, new DateOnly(2024, 2, 29) })
            Assert.Equal(value, TestHelpers.RoundTrip(value));
    }

    [Fact] public void TimeOnlyBoundariesRoundTrip()
    {
        foreach (var value in new[] { TimeOnly.MinValue, TimeOnly.MaxValue, new TimeOnly(12, 34, 56, 789) })
            Assert.Equal(value, TestHelpers.RoundTrip(value));
    }

    [Fact] public void TimeZoneInfoUtcRoundTrip()
    {
        var value = TimeZoneInfo.Utc;
        var actual = TestHelpers.RoundTrip(value);
        Assert.Equal(value.Id, actual.Id);
    }
}

public sealed class NumericRoundTripTests
{
    [Fact] public void ComplexRoundTrip() => Assert.Equal(new Complex(-1.25, 3.5), TestHelpers.RoundTrip(new Complex(-1.25, 3.5)));
    [Fact] public void PlaneRoundTrip() => Assert.Equal(new Plane(1, -2, 3, -4), TestHelpers.RoundTrip(new Plane(1, -2, 3, -4)));
    [Fact] public void QuaternionRoundTrip() => Assert.Equal(new Quaternion(1, -2, 3, -4), TestHelpers.RoundTrip(new Quaternion(1, -2, 3, -4)));
    [Fact] public void Matrix3x2RoundTrip() => Assert.Equal(new Matrix3x2(1,2,3,4,5,6), TestHelpers.RoundTrip(new Matrix3x2(1,2,3,4,5,6)));
    [Fact] public void Matrix4x4RoundTrip() => Assert.Equal(new Matrix4x4(1,2,0,0, 0,1,0,0, 0,0,1,-3.5f, 0,0,0,1), TestHelpers.RoundTrip(new Matrix4x4(1,2,0,0, 0,1,0,0, 0,0,1,-3.5f, 0,0,0,1)));
    [Fact] public void Vector2RoundTrip() => Assert.Equal(new Vector2(-1.25f, 2.5f), TestHelpers.RoundTrip(new Vector2(-1.25f, 2.5f)));
    [Fact] public void Vector3RoundTrip() => Assert.Equal(new Vector3(-1.25f, 2.5f, 3.75f), TestHelpers.RoundTrip(new Vector3(-1.25f, 2.5f, 3.75f)));
    [Fact] public void Vector4RoundTrip() => Assert.Equal(new Vector4(-1, 2, -3, 4), TestHelpers.RoundTrip(new Vector4(-1, 2, -3, 4)));
}
