using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// Plan §19.2: RT-23…RT-42, the time, numeric and system families of contract §23. Every numeric
/// component is asserted on its own, because a vector that arrives with two of its fields swapped
/// still compares unequal only if each one is looked at.
/// </summary>
public abstract partial class Corpus
{
    [Fact]
    public void Deserialize_DateTime_RestoresTheTicksAndTheKind()
    {
        // §22.4 encodes ToBinary(), which carries the kind, so both halves are asserted.
        foreach (var kind in (ReadOnlySpan<DateTimeKind>)
                 [DateTimeKind.Unspecified, DateTimeKind.Utc, DateTimeKind.Local])
        {
            var moment = new DateTime(2026, 9, 19, 12, 30, 45, 123, kind);
            var restored = RoundTrip(moment);

            Assert.Equal(moment, restored);
            Assert.Equal(kind, restored.Kind);
        }
    }

    [Fact]
    public void Deserialize_DateTimeAtItsExtremes_RoundTrips()
    {
        AssertRoundTrips(DateTime.MinValue, DateTime.MaxValue, DateTime.UnixEpoch);

        Assert.Equal(DateTimeKind.Utc, RoundTrip(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)).Kind);
        Assert.Equal(DateTimeKind.Utc, RoundTrip(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)).Kind);
    }

    [Fact]
    public void Deserialize_DateTimeOffset_RestoresTheInstantAndTheOffset()
    {
        // ±14:00 are the extreme offsets the type admits, and §22.4 writes the offset separately.
        foreach (var offset in (ReadOnlySpan<TimeSpan>)
                 [TimeSpan.Zero, TimeSpan.FromHours(14), TimeSpan.FromHours(-14), new TimeSpan(5, 30, 0)])
        {
            var moment = new DateTimeOffset(2026, 9, 19, 12, 30, 45, offset);
            var restored = RoundTrip(moment);

            Assert.Equal(moment, restored);
            Assert.Equal(offset, restored.Offset);
        }

        AssertRoundTrips(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
    }

    [Fact]
    public void Deserialize_TimeSpan_RestoresTheBoundaries() =>
        AssertRoundTrips(
            TimeSpan.Zero,
            TimeSpan.MinValue,
            TimeSpan.MaxValue,
            TimeSpan.FromTicks(-1),
            TimeSpan.FromTicks(1),
            new TimeSpan(1, 2, 3, 4, 5));

    [Fact]
    public void Deserialize_DateOnly_RestoresTheBoundaries() =>
        AssertRoundTrips(DateOnly.MinValue, DateOnly.MaxValue, new DateOnly(2026, 9, 19));

    [Fact]
    public void Deserialize_TimeOnly_RestoresTheBoundaries() =>
        AssertRoundTrips(TimeOnly.MinValue, TimeOnly.MaxValue, new TimeOnly(12, 30, 45, 123));

    [Fact]
    public void Deserialize_TimeZoneInfo_RestoresTheZone()
    {
        // A custom zone rather than a machine zone, so the corpus does not depend on the host's data.
        var fixedZone = TimeZoneInfo.CreateCustomTimeZone(
            "Viper/Fixed", new TimeSpan(5, 30, 0), "Viper Fixed", "Viper Fixed Standard Time");

        Assert.Equal(TimeZoneInfo.Utc, RoundTrip(TimeZoneInfo.Utc));

        var restored = RoundTrip(fixedZone)!;
        Assert.Equal("Viper/Fixed", restored.Id);
        Assert.Equal(new TimeSpan(5, 30, 0), restored.BaseUtcOffset);
    }

    [Fact]
    public void Deserialize_Complex_RestoresBothComponents()
    {
        var restored = RoundTrip(new Complex(1.5, -2.5));

        Assert.Equal(1.5, restored.Real);
        Assert.Equal(-2.5, restored.Imaginary);
    }

    [Fact]
    public void Deserialize_Vector2_RestoresEveryComponent()
    {
        var restored = RoundTrip(new Vector2(1.5f, -2.5f));

        Assert.Equal(1.5f, restored.X);
        Assert.Equal(-2.5f, restored.Y);
    }

    [Fact]
    public void Deserialize_Vector3_RestoresEveryComponent()
    {
        var restored = RoundTrip(new Vector3(1.5f, -2.5f, 3.5f));

        Assert.Equal(1.5f, restored.X);
        Assert.Equal(-2.5f, restored.Y);
        Assert.Equal(3.5f, restored.Z);
    }

    [Fact]
    public void Deserialize_Vector4_RestoresEveryComponent()
    {
        var restored = RoundTrip(new Vector4(1.5f, -2.5f, 3.5f, -4.5f));

        Assert.Equal(1.5f, restored.X);
        Assert.Equal(-2.5f, restored.Y);
        Assert.Equal(3.5f, restored.Z);
        Assert.Equal(-4.5f, restored.W);
    }

    [Fact]
    public void Deserialize_Quaternion_RestoresEveryComponent()
    {
        var restored = RoundTrip(new Quaternion(1.5f, -2.5f, 3.5f, -4.5f));

        Assert.Equal(1.5f, restored.X);
        Assert.Equal(-2.5f, restored.Y);
        Assert.Equal(3.5f, restored.Z);
        Assert.Equal(-4.5f, restored.W);
    }

    [Fact]
    public void Deserialize_Plane_RestoresTheNormalAndTheDistance()
    {
        var restored = RoundTrip(new Plane(1.5f, -2.5f, 3.5f, -4.5f));

        Assert.Equal(1.5f, restored.Normal.X);
        Assert.Equal(-2.5f, restored.Normal.Y);
        Assert.Equal(3.5f, restored.Normal.Z);
        Assert.Equal(-4.5f, restored.D);
    }

    [Fact]
    public void Deserialize_Matrix3x2_RestoresEveryCellInOrder()
    {
        var restored = RoundTrip(new Matrix3x2(11, 12, 21, 22, 31, 32));

        Assert.Equal(11f, restored.M11);
        Assert.Equal(12f, restored.M12);
        Assert.Equal(21f, restored.M21);
        Assert.Equal(22f, restored.M22);
        Assert.Equal(31f, restored.M31);
        Assert.Equal(32f, restored.M32);
    }

    [Fact]
    public void Deserialize_Matrix4x4_RestoresEveryCellInRowMajorOrder()
    {
        var restored = RoundTrip(new Matrix4x4(
            11, 12, 13, 14,
            21, 22, 23, 24,
            31, 32, 33, 34,
            41, 42, 43, 44));

        Assert.Equal(11f, restored.M11);
        Assert.Equal(12f, restored.M12);
        Assert.Equal(13f, restored.M13);
        Assert.Equal(14f, restored.M14);
        Assert.Equal(21f, restored.M21);
        Assert.Equal(22f, restored.M22);
        Assert.Equal(23f, restored.M23);
        Assert.Equal(24f, restored.M24);
        Assert.Equal(31f, restored.M31);
        Assert.Equal(32f, restored.M32);
        Assert.Equal(33f, restored.M33);
        Assert.Equal(34f, restored.M34);
        Assert.Equal(41f, restored.M41);
        Assert.Equal(42f, restored.M42);
        Assert.Equal(43f, restored.M43);
        Assert.Equal(44f, restored.M44);
    }

    [Fact]
    public void Deserialize_Guid_RestoresTheBoundaries() =>
        AssertRoundTrips(
            Guid.Empty,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            Guid.Parse("8d0c1b2a-3e4f-5061-7283-94a5b6c7d8e9"));

    [Fact]
    public void Deserialize_Uri_RestoresTheOriginalString()
    {
        AssertRoundTrips(
            new Uri("https://example.test/a/b?c=1&d=2#frag"),
            new Uri("relative/path?q=1", UriKind.Relative),
            new Uri("file:///c:/tmp/x.bin"),
            new Uri("https://example.test/ünï%20code"));

        // §22.4 writes OriginalString, so the spelling survives, not just the parsed form.
        Assert.Equal(
            "https://example.test/a/./b",
            RoundTrip(new Uri("https://example.test/a/./b"))!.OriginalString);
    }

    [Fact]
    public void Deserialize_Version_RestoresEveryComponentCount()
    {
        AssertRoundTrips(new Version(1, 2), new Version(1, 2, 3), new Version(1, 2, 3, 4));

        var restored = RoundTrip(new Version(1, 2))!;
        Assert.Equal(-1, restored.Build);
        Assert.Equal(-1, restored.Revision);
    }

    [Fact]
    public void Deserialize_StringBuilder_RestoresTheText()
    {
        Assert.Equal(string.Empty, RoundTrip(new StringBuilder())!.ToString());
        Assert.Equal("abc", RoundTrip(new StringBuilder("abc"))!.ToString());

        // Long enough to span more than one internal chunk, which ToString() has to flatten.
        string long_ = new('x', 10_000);
        Assert.Equal(long_, RoundTrip(new StringBuilder(long_))!.ToString());
    }

    [Fact]
    public void Deserialize_CultureInfo_RestoresTheCultureByName()
    {
        Assert.Equal(CultureInfo.InvariantCulture, RoundTrip(CultureInfo.InvariantCulture));
        Assert.Equal(CultureInfo.GetCultureInfo("fr-FR"), RoundTrip(CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Equal("fr", RoundTrip(CultureInfo.GetCultureInfo("fr"))!.Name);
    }

    [Fact]
    public void Deserialize_BitArray_RestoresEveryBitCountAcrossAByteBoundary()
    {
        // RT-42: 0, 1, 7, 8 and 9 bits, so the ceil(bits / 8) blob is exercised on both sides of the
        // boundary and the trailing bits of the last byte are proven not to leak in.
        foreach (int length in (ReadOnlySpan<int>)[0, 1, 7, 8, 9])
        {
            var bits = new BitArray(length);
            for (int i = 0; i < length; i += 2)
                bits[i] = true;

            var restored = RoundTrip(bits)!;

            Assert.Equal(length, restored.Length);
            for (int i = 0; i < length; i++)
                Assert.Equal(bits[i], restored[i]);
        }
    }
}
