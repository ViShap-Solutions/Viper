using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace ViShap.Viper.Engine;

/// <summary>
/// The codecs of the types whose encoding is closed over nothing a caller declares: the scalars, by
/// exact type, and delegates, which are refused. Each is created by statically typed code, so these
/// types need no reflection and no context in any configuration.
/// </summary>
internal static class BuiltInCodecs
{
    /// <summary>The scalar types and the creation of each one's codec.</summary>
    public static readonly FrozenDictionary<Type, Func<object>> Scalars = new Dictionary<Type, Func<object>>
    {
        [typeof(bool)] = static () => Value(new BooleanFormatter()),
        [typeof(byte)] = static () => Value(new ByteFormatter()),
        [typeof(sbyte)] = static () => Value(new SByteFormatter()),
        [typeof(short)] = static () => Value(new Int16Formatter()),
        [typeof(ushort)] = static () => Value(new UInt16Formatter()),
        [typeof(int)] = static () => Value(new Int32Formatter()),
        [typeof(uint)] = static () => Value(new UInt32Formatter()),
        [typeof(long)] = static () => Value(new Int64Formatter()),
        [typeof(ulong)] = static () => Value(new UInt64Formatter()),
        [typeof(float)] = static () => Value(new SingleFormatter()),
        [typeof(double)] = static () => Value(new DoubleFormatter()),
        [typeof(decimal)] = static () => Value(new DecimalFormatter()),
        [typeof(char)] = static () => Value(new CharFormatter()),
        [typeof(string)] = static () => Reference(new StringFormatter()),
        [typeof(Half)] = static () => Value(new HalfFormatter()),
        [typeof(Int128)] = static () => Value(new Int128Formatter()),
        [typeof(UInt128)] = static () => Value(new UInt128Formatter()),
        [typeof(IntPtr)] = static () => Value(new IntPtrFormatter()),
        [typeof(UIntPtr)] = static () => Value(new UIntPtrFormatter()),
        [typeof(Rune)] = static () => Value(new RuneFormatter()),
        [typeof(BigInteger)] = static () => Value(new BigIntegerFormatter()),

        [typeof(DateTime)] = static () => Value(new DateTimeFormatter()),
        [typeof(DateTimeOffset)] = static () => Value(new DateTimeOffsetFormatter()),
        [typeof(TimeSpan)] = static () => Value(new TimeSpanFormatter()),
        [typeof(DateOnly)] = static () => Value(new DateOnlyFormatter()),
        [typeof(TimeOnly)] = static () => Value(new TimeOnlyFormatter()),
        [typeof(TimeZoneInfo)] = static () => Reference(new TimeZoneInfoFormatter()),

        [typeof(Complex)] = static () => Value(new ComplexFormatter()),
        [typeof(Vector2)] = static () => Value(new Vector2Formatter()),
        [typeof(Vector3)] = static () => Value(new Vector3Formatter()),
        [typeof(Vector4)] = static () => Value(new Vector4Formatter()),
        [typeof(Quaternion)] = static () => Value(new QuaternionFormatter()),
        [typeof(Plane)] = static () => Value(new PlaneFormatter()),
        [typeof(Matrix3x2)] = static () => Value(new Matrix3x2Formatter()),
        [typeof(Matrix4x4)] = static () => Value(new Matrix4x4Formatter()),

        [typeof(Guid)] = static () => Value(new GuidFormatter()),
        [typeof(Uri)] = static () => Reference(new UriFormatter()),
        [typeof(Version)] = static () => Reference(new VersionFormatter()),
        [typeof(StringBuilder)] = static () => Reference(new StringBuilderFormatter()),
        [typeof(CultureInfo)] = static () => Reference(new CultureInfoFormatter()),
        [typeof(BitArray)] = static () => Reference(new BitArrayFormatter())
    }.ToFrozenDictionary();

    /// <summary>
    /// The codec of <typeparamref name="T"/> when it is a scalar or a delegate, and otherwise
    /// <see langword="null"/>.
    /// </summary>
    public static Codec<T>? Resolve<T>()
    {
        if (typeof(Delegate).IsAssignableFrom(typeof(T)))
            return new RejectedCodec<T>(
                $"Delegate types cannot be serialized ('{typeof(T)}') — as a root value, a member, " +
                "or a collection element. Exclude the containing member with [BinaryIgnore] instead.",
                $"Delegate types cannot be deserialized ('{typeof(T)}').");

        return Scalars.TryGetValue(typeof(T), out var create) ? (Codec<T>)create() : null;
    }

    private static ScalarCodec<T> Value<T>(IScalarFormatter<T> formatter)
        where T : struct => new(formatter);

    private static NullableScalarCodec<T> Reference<T>(IScalarFormatter<T> formatter)
        where T : class => new(formatter);
}
