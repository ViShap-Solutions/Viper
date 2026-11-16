using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ViShap.Viper.Formatters;

/// <summary>
/// The equality comparer a hashed container is built with when the engine creates it from a payload:
/// for a built-in key type, one whose hash is computed by <see cref="HashCode"/> — seeded per process —
/// over the whole value, so keys a payload chooses cannot be made to share a hash; for any other key
/// type <c>null</c>, which every container reads as its default comparer.
/// </summary>
/// <remarks>
/// Equality is always the key type's own. Only the hash changes, and only for types whose default hash
/// is a fixed function of the value: <see cref="string"/> keeps its default, which the runtime already
/// randomizes per process.
/// </remarks>
internal static class KeyComparer<T>
{
    public static readonly IEqualityComparer<T>? Instance = (IEqualityComparer<T>?)Create();

    private static object? Create()
    {
        if (typeof(T).IsEnum ||
            typeof(T) == typeof(bool) || typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) ||
            typeof(T) == typeof(short) || typeof(T) == typeof(ushort) || typeof(T) == typeof(char) ||
            typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) ||
            typeof(T) == typeof(ulong) || typeof(T) == typeof(Int128) || typeof(T) == typeof(UInt128) ||
            typeof(T) == typeof(nint) || typeof(T) == typeof(nuint) || typeof(T) == typeof(Rune) ||
            typeof(T) == typeof(Guid) || typeof(T) == typeof(TimeSpan) || typeof(T) == typeof(DateOnly) ||
            typeof(T) == typeof(TimeOnly))
        {
            return new BitwiseKeyComparer<T>();
        }

        if (typeof(T) == typeof(double)) return new DoubleKeyComparer();
        if (typeof(T) == typeof(float)) return new SingleKeyComparer();
        if (typeof(T) == typeof(Half)) return new HalfKeyComparer();
        if (typeof(T) == typeof(decimal)) return new DecimalKeyComparer();
        if (typeof(T) == typeof(DateTime)) return new DateTimeKeyComparer();
        if (typeof(T) == typeof(DateTimeOffset)) return new DateTimeOffsetKeyComparer();
        if (typeof(T) == typeof(BigInteger)) return new BigIntegerKeyComparer();
        if (typeof(T) == typeof(Version)) return new VersionKeyComparer();

        return null;
    }
}

/// <summary>Hashes the bytes of a value type whose equality is the equality of its bytes.</summary>
internal sealed class BitwiseKeyComparer<T> : IEqualityComparer<T>
{
    public bool Equals(T? x, T? y) => EqualityComparer<T>.Default.Equals(x, y);

    public int GetHashCode(T value) =>
        KeyHash.Of(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref value), Unsafe.SizeOf<T>()));
}

/// <summary>
/// <see cref="double"/> equality treats every NaN as one value and both zeros as one value, so the hash
/// is taken over the bits of a canonical NaN and of positive zero.
/// </summary>
internal sealed class DoubleKeyComparer : IEqualityComparer<double>
{
    public bool Equals(double x, double y) => x.Equals(y);

    public int GetHashCode(double value) =>
        KeyHash.Of(BitConverter.DoubleToInt64Bits(double.IsNaN(value) ? double.NaN : value == 0 ? 0d : value));
}

/// <summary>The <see cref="float"/> counterpart of <see cref="DoubleKeyComparer"/>.</summary>
internal sealed class SingleKeyComparer : IEqualityComparer<float>
{
    public bool Equals(float x, float y) => x.Equals(y);

    public int GetHashCode(float value) =>
        KeyHash.Of(BitConverter.SingleToInt32Bits(float.IsNaN(value) ? float.NaN : value == 0 ? 0f : value));
}

/// <summary>The <see cref="Half"/> counterpart of <see cref="DoubleKeyComparer"/>.</summary>
internal sealed class HalfKeyComparer : IEqualityComparer<Half>
{
    public bool Equals(Half x, Half y) => x.Equals(y);

    public int GetHashCode(Half value) =>
        KeyHash.Of(BitConverter.HalfToInt16Bits(Half.IsNaN(value) ? Half.NaN : value == Half.Zero ? Half.Zero : value));
}

/// <summary>
/// <see cref="decimal"/> equality ignores trailing zeros and the sign of zero, so the hash is taken over
/// the coefficient and scale with trailing zeros removed.
/// </summary>
internal sealed class DecimalKeyComparer : IEqualityComparer<decimal>
{
    public bool Equals(decimal x, decimal y) => x.Equals(y);

    public int GetHashCode(decimal value)
    {
        if (value == 0m)
            return KeyHash.Of(0L);

        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);

        var coefficient = new UInt128((uint)bits[2], ((ulong)(uint)bits[1] << 32) | (uint)bits[0]);
        int scale = (bits[3] >> 16) & 0xFF;

        while (scale > 0 && coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }

        return HashCode.Combine(KeyHash.Of(coefficient), scale, value < 0m);
    }
}

/// <summary><see cref="DateTime"/> equality compares ticks and ignores the kind, so the hash does too.</summary>
internal sealed class DateTimeKeyComparer : IEqualityComparer<DateTime>
{
    public bool Equals(DateTime x, DateTime y) => x.Equals(y);

    public int GetHashCode(DateTime value) => KeyHash.Of(value.Ticks);
}

/// <summary><see cref="DateTimeOffset"/> equality compares instants, so the hash is over the UTC ticks.</summary>
internal sealed class DateTimeOffsetKeyComparer : IEqualityComparer<DateTimeOffset>
{
    public bool Equals(DateTimeOffset x, DateTimeOffset y) => x.Equals(y);

    public int GetHashCode(DateTimeOffset value) => KeyHash.Of(value.UtcTicks);
}

/// <summary>Hashes the shortest two's complement bytes of a <see cref="BigInteger"/>, which are unique to its value.</summary>
internal sealed class BigIntegerKeyComparer : IEqualityComparer<BigInteger>
{
    public bool Equals(BigInteger x, BigInteger y) => x.Equals(y);

    public int GetHashCode(BigInteger value)
    {
        int count = value.GetByteCount();
        if (count > 256)
            return KeyHash.Of(value.ToByteArray());

        Span<byte> bytes = stackalloc byte[count];
        value.TryWriteBytes(bytes, out _);
        return KeyHash.Of(bytes);
    }
}

/// <summary>Hashes the four components of a <see cref="Version"/> as four inputs rather than one folded number.</summary>
internal sealed class VersionKeyComparer : IEqualityComparer<Version>
{
    public bool Equals(Version? x, Version? y) => EqualityComparer<Version>.Default.Equals(x, y);

    public int GetHashCode(Version value) => HashCode.Combine(value.Major, value.Minor, value.Build, value.Revision);
}

internal static class KeyHash
{
    public static int Of(ReadOnlySpan<byte> bytes)
    {
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }

    public static int Of<TValue>(TValue value) where TValue : unmanaged =>
        Of(MemoryMarshal.AsBytes(new ReadOnlySpan<TValue>(in value)));
}
