using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// One round trip per scalar family of contract §23. The boundary values and per-type checkpoints of
/// plan §19.1 and §19.2 are added in stage M5; this is the regression net the formatter layer needs
/// before then.
/// </summary>
public class ScalarTests
{
    private readonly BinarySerializer _serializer = new();

    private T? RoundTrip<T>(T value) => _serializer.Deserialize<T>(_serializer.Serialize(value));

    [Fact]
    public void Primitives()
    {
        Assert.True(RoundTrip(true));
        Assert.Equal((byte)7, RoundTrip((byte)7));
        Assert.Equal((sbyte)-7, RoundTrip((sbyte)-7));
        Assert.Equal((short)-300, RoundTrip((short)-300));
        Assert.Equal((ushort)300, RoundTrip((ushort)300));
        Assert.Equal(-100_000, RoundTrip(-100_000));
        Assert.Equal(100_000u, RoundTrip(100_000u));
        Assert.Equal(-10_000_000_000L, RoundTrip(-10_000_000_000L));
        Assert.Equal(10_000_000_000UL, RoundTrip(10_000_000_000UL));
        Assert.Equal(3.5f, RoundTrip(3.5f));
        Assert.Equal(3.5e300, RoundTrip(3.5e300));
        Assert.Equal(12345.6789m, RoundTrip(12345.6789m));
        Assert.Equal('ß', RoundTrip('ß'));
        Assert.Equal("héllo ☃", RoundTrip("héllo ☃"));
        Assert.Equal(string.Empty, RoundTrip(string.Empty));
        Assert.Equal((Half)1.5, RoundTrip((Half)1.5));
        Assert.Equal(Int128.MinValue, RoundTrip(Int128.MinValue));
        Assert.Equal(UInt128.MaxValue, RoundTrip(UInt128.MaxValue));
        Assert.Equal(new IntPtr(42), RoundTrip(new IntPtr(42)));
        Assert.Equal(new UIntPtr(42), RoundTrip(new UIntPtr(42)));
        Assert.Equal(new Rune('A'), RoundTrip(new Rune('A')));
        Assert.Equal(BigInteger.Pow(2, 200), RoundTrip(BigInteger.Pow(2, 200)));
        Assert.Equal(DayOfWeek.Friday, RoundTrip(DayOfWeek.Friday));
    }

    [Fact]
    public void Nullables()
    {
        Assert.Equal(5, RoundTrip<int?>(5));
        Assert.Null(RoundTrip<int?>(null));
        Assert.Null(RoundTrip<string?>(null));
    }

    [Fact]
    public void TimeAndText()
    {
        var now = new DateTime(2026, 9, 18, 12, 30, 0, DateTimeKind.Utc);
        Assert.Equal(now, RoundTrip(now));
        Assert.Equal(new DateTimeOffset(now, TimeSpan.Zero), RoundTrip(new DateTimeOffset(now, TimeSpan.Zero)));
        Assert.Equal(TimeSpan.FromMinutes(90), RoundTrip(TimeSpan.FromMinutes(90)));
        Assert.Equal(new DateOnly(2026, 9, 18), RoundTrip(new DateOnly(2026, 9, 18)));
        Assert.Equal(new TimeOnly(12, 30), RoundTrip(new TimeOnly(12, 30)));
        Assert.Equal(TimeZoneInfo.Utc, RoundTrip(TimeZoneInfo.Utc));
        Assert.Equal("abc", RoundTrip(new StringBuilder("abc"))!.ToString());
        Assert.Equal(CultureInfo.GetCultureInfo("fr-FR"), RoundTrip(CultureInfo.GetCultureInfo("fr-FR")));
    }

    [Fact]
    public void SystemTypes()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(guid, RoundTrip(guid));
        Assert.Equal(new Uri("https://example.com/a?b=1"), RoundTrip(new Uri("https://example.com/a?b=1")));
        Assert.Equal(new Version(1, 2, 3, 4), RoundTrip(new Version(1, 2, 3, 4)));

        var bits = new BitArray([true, false, true, true, false]);
        var restored = RoundTrip(bits)!;
        Assert.Equal(bits.Length, restored.Length);
        for (int i = 0; i < bits.Length; i++)
            Assert.Equal(bits[i], restored[i]);
    }

    [Fact]
    public void Numerics()
    {
        Assert.Equal(new Complex(1, 2), RoundTrip(new Complex(1, 2)));
        Assert.Equal(new Vector2(1, 2), RoundTrip(new Vector2(1, 2)));
        Assert.Equal(new Vector3(1, 2, 3), RoundTrip(new Vector3(1, 2, 3)));
        Assert.Equal(new Vector4(1, 2, 3, 4), RoundTrip(new Vector4(1, 2, 3, 4)));
        Assert.Equal(new Quaternion(1, 2, 3, 4), RoundTrip(new Quaternion(1, 2, 3, 4)));
        Assert.Equal(new Plane(1, 2, 3, 4), RoundTrip(new Plane(1, 2, 3, 4)));
        Assert.Equal(Matrix3x2.Identity, RoundTrip(Matrix3x2.Identity));
        Assert.Equal(Matrix4x4.Identity, RoundTrip(Matrix4x4.Identity));
    }
}
