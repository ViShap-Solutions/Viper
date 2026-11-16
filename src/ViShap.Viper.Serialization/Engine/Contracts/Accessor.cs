namespace ViShap.Viper.Engine;

/// <summary>
/// One member of a contract's accessor table as the engine walks it: the member's value written and read
/// through the codec the engine would use for the member's type, so a table writes exactly the bytes
/// <c>MemberWriter.Member</c> writes and reads them under the same checks.
/// </summary>
internal abstract class Accessor<T>
{
    public abstract Type MemberType { get; }

    public abstract void Write(ref WireWriter writer, in T owner);

    public abstract void Read(ref WireReader reader, ref T owner);

    /// <summary>
    /// The entry for a member of type <typeparamref name="TMember"/>: a built-in scalar is written and
    /// read by its formatter directly, the null of a string by the folded length, and any other member
    /// by the codec of its type.
    /// </summary>
    public static Accessor<T> For<TMember>(MemberGetter<T, TMember> get, MemberSetter<T, TMember> set)
    {
        if (typeof(TMember) == typeof(bool))
            return new ScalarAccessor<T, bool, BooleanFormat>((MemberGetter<T, bool>)(object)get, (MemberSetter<T, bool>)(object)set);

        if (typeof(TMember) == typeof(byte))
            return new ScalarAccessor<T, byte, ByteFormat>((MemberGetter<T, byte>)(object)get, (MemberSetter<T, byte>)(object)set);

        if (typeof(TMember) == typeof(sbyte))
            return new ScalarAccessor<T, sbyte, SByteFormat>((MemberGetter<T, sbyte>)(object)get, (MemberSetter<T, sbyte>)(object)set);

        if (typeof(TMember) == typeof(short))
            return new ScalarAccessor<T, short, Int16Format>((MemberGetter<T, short>)(object)get, (MemberSetter<T, short>)(object)set);

        if (typeof(TMember) == typeof(ushort))
            return new ScalarAccessor<T, ushort, UInt16Format>((MemberGetter<T, ushort>)(object)get, (MemberSetter<T, ushort>)(object)set);

        if (typeof(TMember) == typeof(int))
            return new ScalarAccessor<T, int, Int32Format>((MemberGetter<T, int>)(object)get, (MemberSetter<T, int>)(object)set);

        if (typeof(TMember) == typeof(uint))
            return new ScalarAccessor<T, uint, UInt32Format>((MemberGetter<T, uint>)(object)get, (MemberSetter<T, uint>)(object)set);

        if (typeof(TMember) == typeof(long))
            return new ScalarAccessor<T, long, Int64Format>((MemberGetter<T, long>)(object)get, (MemberSetter<T, long>)(object)set);

        if (typeof(TMember) == typeof(ulong))
            return new ScalarAccessor<T, ulong, UInt64Format>((MemberGetter<T, ulong>)(object)get, (MemberSetter<T, ulong>)(object)set);

        if (typeof(TMember) == typeof(float))
            return new ScalarAccessor<T, float, SingleFormat>((MemberGetter<T, float>)(object)get, (MemberSetter<T, float>)(object)set);

        if (typeof(TMember) == typeof(double))
            return new ScalarAccessor<T, double, DoubleFormat>((MemberGetter<T, double>)(object)get, (MemberSetter<T, double>)(object)set);

        if (typeof(TMember) == typeof(decimal))
            return new ScalarAccessor<T, decimal, DecimalFormat>((MemberGetter<T, decimal>)(object)get, (MemberSetter<T, decimal>)(object)set);

        if (typeof(TMember) == typeof(char))
            return new ScalarAccessor<T, char, CharFormat>((MemberGetter<T, char>)(object)get, (MemberSetter<T, char>)(object)set);

        if (typeof(TMember) == typeof(Half))
            return new ScalarAccessor<T, Half, HalfFormat>((MemberGetter<T, Half>)(object)get, (MemberSetter<T, Half>)(object)set);

        if (typeof(TMember) == typeof(Int128))
            return new ScalarAccessor<T, Int128, Int128Format>((MemberGetter<T, Int128>)(object)get, (MemberSetter<T, Int128>)(object)set);

        if (typeof(TMember) == typeof(UInt128))
            return new ScalarAccessor<T, UInt128, UInt128Format>((MemberGetter<T, UInt128>)(object)get, (MemberSetter<T, UInt128>)(object)set);

        if (typeof(TMember) == typeof(nint))
            return new ScalarAccessor<T, nint, IntPtrFormat>((MemberGetter<T, nint>)(object)get, (MemberSetter<T, nint>)(object)set);

        if (typeof(TMember) == typeof(nuint))
            return new ScalarAccessor<T, nuint, UIntPtrFormat>((MemberGetter<T, nuint>)(object)get, (MemberSetter<T, nuint>)(object)set);

        if (typeof(TMember) == typeof(System.Text.Rune))
            return new ScalarAccessor<T, System.Text.Rune, RuneFormat>((MemberGetter<T, System.Text.Rune>)(object)get, (MemberSetter<T, System.Text.Rune>)(object)set);

        if (typeof(TMember) == typeof(Guid))
            return new ScalarAccessor<T, Guid, GuidFormat>((MemberGetter<T, Guid>)(object)get, (MemberSetter<T, Guid>)(object)set);

        if (typeof(TMember) == typeof(DateTime))
            return new ScalarAccessor<T, DateTime, DateTimeFormat>((MemberGetter<T, DateTime>)(object)get, (MemberSetter<T, DateTime>)(object)set);

        if (typeof(TMember) == typeof(DateTimeOffset))
            return new ScalarAccessor<T, DateTimeOffset, DateTimeOffsetFormat>((MemberGetter<T, DateTimeOffset>)(object)get, (MemberSetter<T, DateTimeOffset>)(object)set);

        if (typeof(TMember) == typeof(TimeSpan))
            return new ScalarAccessor<T, TimeSpan, TimeSpanFormat>((MemberGetter<T, TimeSpan>)(object)get, (MemberSetter<T, TimeSpan>)(object)set);

        if (typeof(TMember) == typeof(DateOnly))
            return new ScalarAccessor<T, DateOnly, DateOnlyFormat>((MemberGetter<T, DateOnly>)(object)get, (MemberSetter<T, DateOnly>)(object)set);

        if (typeof(TMember) == typeof(TimeOnly))
            return new ScalarAccessor<T, TimeOnly, TimeOnlyFormat>((MemberGetter<T, TimeOnly>)(object)get, (MemberSetter<T, TimeOnly>)(object)set);

        if (typeof(TMember) == typeof(string))
            return new ScalarAccessor<T, string?, StringFormat>((MemberGetter<T, string?>)(object)get, (MemberSetter<T, string?>)(object)set);

        return new CodecAccessor<T, TMember>(get, set);
    }
}

/// <summary>A built-in scalar member, written and read by its formatter with no dispatch between.</summary>
internal sealed class ScalarAccessor<T, TMember, TFormat>(MemberGetter<T, TMember> get, MemberSetter<T, TMember> set)
    : Accessor<T>
    where TFormat : struct, IScalarFormat<TMember>
{
    public override Type MemberType => typeof(TMember);

    public override void Write(ref WireWriter writer, in T owner) => TFormat.Write(ref writer, get(in owner));

    public override void Read(ref WireReader reader, ref T owner) => set(ref owner, TFormat.Read(ref reader));
}

/// <summary>Any other member, written and read by the engine's codec for its type.</summary>
internal sealed class CodecAccessor<T, TMember>(MemberGetter<T, TMember> get, MemberSetter<T, TMember> set) : Accessor<T>
{
    public override Type MemberType => typeof(TMember);

    public override void Write(ref WireWriter writer, in T owner) => FormatterCache<TMember>.Instance.Write(ref writer, get(in owner));

    public override void Read(ref WireReader reader, ref T owner) => set(ref owner, FormatterCache<TMember>.Instance.Read(ref reader));
}

/// <summary>A built-in scalar's formatter reached statically, so an accessor over it is specialized per scalar.</summary>
internal interface IScalarFormat<TMember>
{
    static abstract void Write(ref WireWriter writer, TMember value);

    static abstract TMember Read(ref WireReader reader);
}

internal readonly struct BooleanFormat : IScalarFormat<bool>
{
    private static readonly BooleanFormatter Formatter = new();

    public static void Write(ref WireWriter writer, bool value) => Formatter.Write(ref writer, value);

    public static bool Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct ByteFormat : IScalarFormat<byte>
{
    private static readonly ByteFormatter Formatter = new();

    public static void Write(ref WireWriter writer, byte value) => Formatter.Write(ref writer, value);

    public static byte Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct SByteFormat : IScalarFormat<sbyte>
{
    private static readonly SByteFormatter Formatter = new();

    public static void Write(ref WireWriter writer, sbyte value) => Formatter.Write(ref writer, value);

    public static sbyte Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct Int16Format : IScalarFormat<short>
{
    private static readonly Int16Formatter Formatter = new();

    public static void Write(ref WireWriter writer, short value) => Formatter.Write(ref writer, value);

    public static short Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct UInt16Format : IScalarFormat<ushort>
{
    private static readonly UInt16Formatter Formatter = new();

    public static void Write(ref WireWriter writer, ushort value) => Formatter.Write(ref writer, value);

    public static ushort Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct Int32Format : IScalarFormat<int>
{
    private static readonly Int32Formatter Formatter = new();

    public static void Write(ref WireWriter writer, int value) => Formatter.Write(ref writer, value);

    public static int Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct UInt32Format : IScalarFormat<uint>
{
    private static readonly UInt32Formatter Formatter = new();

    public static void Write(ref WireWriter writer, uint value) => Formatter.Write(ref writer, value);

    public static uint Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct Int64Format : IScalarFormat<long>
{
    private static readonly Int64Formatter Formatter = new();

    public static void Write(ref WireWriter writer, long value) => Formatter.Write(ref writer, value);

    public static long Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct UInt64Format : IScalarFormat<ulong>
{
    private static readonly UInt64Formatter Formatter = new();

    public static void Write(ref WireWriter writer, ulong value) => Formatter.Write(ref writer, value);

    public static ulong Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct SingleFormat : IScalarFormat<float>
{
    private static readonly SingleFormatter Formatter = new();

    public static void Write(ref WireWriter writer, float value) => Formatter.Write(ref writer, value);

    public static float Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct DoubleFormat : IScalarFormat<double>
{
    private static readonly DoubleFormatter Formatter = new();

    public static void Write(ref WireWriter writer, double value) => Formatter.Write(ref writer, value);

    public static double Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct DecimalFormat : IScalarFormat<decimal>
{
    private static readonly DecimalFormatter Formatter = new();

    public static void Write(ref WireWriter writer, decimal value) => Formatter.Write(ref writer, value);

    public static decimal Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct CharFormat : IScalarFormat<char>
{
    private static readonly CharFormatter Formatter = new();

    public static void Write(ref WireWriter writer, char value) => Formatter.Write(ref writer, value);

    public static char Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct HalfFormat : IScalarFormat<Half>
{
    private static readonly HalfFormatter Formatter = new();

    public static void Write(ref WireWriter writer, Half value) => Formatter.Write(ref writer, value);

    public static Half Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct Int128Format : IScalarFormat<Int128>
{
    private static readonly Int128Formatter Formatter = new();

    public static void Write(ref WireWriter writer, Int128 value) => Formatter.Write(ref writer, value);

    public static Int128 Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct UInt128Format : IScalarFormat<UInt128>
{
    private static readonly UInt128Formatter Formatter = new();

    public static void Write(ref WireWriter writer, UInt128 value) => Formatter.Write(ref writer, value);

    public static UInt128 Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct IntPtrFormat : IScalarFormat<nint>
{
    private static readonly IntPtrFormatter Formatter = new();

    public static void Write(ref WireWriter writer, nint value) => Formatter.Write(ref writer, value);

    public static nint Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct UIntPtrFormat : IScalarFormat<nuint>
{
    private static readonly UIntPtrFormatter Formatter = new();

    public static void Write(ref WireWriter writer, nuint value) => Formatter.Write(ref writer, value);

    public static nuint Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct RuneFormat : IScalarFormat<System.Text.Rune>
{
    private static readonly RuneFormatter Formatter = new();

    public static void Write(ref WireWriter writer, System.Text.Rune value) => Formatter.Write(ref writer, value);

    public static System.Text.Rune Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct GuidFormat : IScalarFormat<Guid>
{
    private static readonly GuidFormatter Formatter = new();

    public static void Write(ref WireWriter writer, Guid value) => Formatter.Write(ref writer, value);

    public static Guid Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct DateTimeFormat : IScalarFormat<DateTime>
{
    private static readonly DateTimeFormatter Formatter = new();

    public static void Write(ref WireWriter writer, DateTime value) => Formatter.Write(ref writer, value);

    public static DateTime Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct DateTimeOffsetFormat : IScalarFormat<DateTimeOffset>
{
    private static readonly DateTimeOffsetFormatter Formatter = new();

    public static void Write(ref WireWriter writer, DateTimeOffset value) => Formatter.Write(ref writer, value);

    public static DateTimeOffset Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct TimeSpanFormat : IScalarFormat<TimeSpan>
{
    private static readonly TimeSpanFormatter Formatter = new();

    public static void Write(ref WireWriter writer, TimeSpan value) => Formatter.Write(ref writer, value);

    public static TimeSpan Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct DateOnlyFormat : IScalarFormat<DateOnly>
{
    private static readonly DateOnlyFormatter Formatter = new();

    public static void Write(ref WireWriter writer, DateOnly value) => Formatter.Write(ref writer, value);

    public static DateOnly Read(ref WireReader reader) => Formatter.Read(ref reader);
}

internal readonly struct TimeOnlyFormat : IScalarFormat<TimeOnly>
{
    private static readonly TimeOnlyFormatter Formatter = new();

    public static void Write(ref WireWriter writer, TimeOnly value) => Formatter.Write(ref writer, value);

    public static TimeOnly Read(ref WireReader reader) => Formatter.Read(ref reader);
}

/// <summary>A string, whose null the engine folds into its length as it does for a string member.</summary>
internal readonly struct StringFormat : IScalarFormat<string?>
{
    private static readonly StringFormatter Formatter = new();

    public static void Write(ref WireWriter writer, string? value)
    {
        if (value is null)
            writer.WriteNull();
        else
            Formatter.Write(ref writer, value);
    }

    public static string? Read(ref WireReader reader) => reader.TryReadNull() ? null : Formatter.Read(ref reader);
}
