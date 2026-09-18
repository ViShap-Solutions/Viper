using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace ViShap.Viper.Formatters;

internal sealed class PrimitiveFormatter<T>(
    Action<ValueWriter, T> write,
    Func<ValueReader, T> read) : IScalarFormatter
    where T : notnull
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(T);

    public void Write(ValueWriter writer, object value, Type declaredType) => write(writer, (T)value);

    public object Read(ValueReader reader, Type declaredType) => read(reader);
}

internal sealed class StringFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(string);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteString((string)value);

    public object Read(ValueReader reader, Type declaredType) => reader.ReadString();
}

internal sealed class EnumFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType.IsEnum;

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var underlyingType = Enum.GetUnderlyingType(declaredType);
        var underlying = Convert.ChangeType(value, underlyingType);
        Underlying(underlyingType).Write(writer, underlying, underlyingType);
    }

    public object Read(ValueReader reader, Type declaredType)
    {
        var underlyingType = Enum.GetUnderlyingType(declaredType);
        return Enum.ToObject(declaredType, Underlying(underlyingType).Read(reader, underlyingType));
    }

    private static IScalarFormatter Underlying(Type underlyingType) =>
        FormatterRegistry.Resolve(underlyingType) as IScalarFormatter
        ?? throw new BinaryTypeException(
            $"Enum underlying type '{underlyingType}' has no scalar encoding.");
}

internal sealed class HalfFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Half);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteInt16(BitConverter.HalfToInt16Bits((Half)value));

    public object Read(ValueReader reader, Type declaredType) =>
        BitConverter.Int16BitsToHalf(reader.ReadInt16());
}

internal sealed class Int128Formatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Int128);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteInt128LittleEndian(buffer, (Int128)value);
        writer.Write(buffer);
    }

    public object Read(ValueReader reader, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[16];
        reader.ReadExact(buffer, "Int128");
        return BinaryPrimitives.ReadInt128LittleEndian(buffer);
    }
}

internal sealed class UInt128Formatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(UInt128);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128LittleEndian(buffer, (UInt128)value);
        writer.Write(buffer);
    }

    public object Read(ValueReader reader, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[16];
        reader.ReadExact(buffer, "UInt128");
        return BinaryPrimitives.ReadUInt128LittleEndian(buffer);
    }
}

internal sealed class IntPtrFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(IntPtr);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteInt64(((IntPtr)value).ToInt64());

    public object Read(ValueReader reader, Type declaredType) => new IntPtr(reader.ReadInt64());
}

internal sealed class UIntPtrFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(UIntPtr);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteUInt64(((UIntPtr)value).ToUInt64());

    public object Read(ValueReader reader, Type declaredType) => new UIntPtr(reader.ReadUInt64());
}

internal sealed class RuneFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Rune);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteInt32(((Rune)value).Value);

    public object Read(ValueReader reader, Type declaredType)
    {
        int value = reader.ReadInt32();
        if (!Rune.IsValid(value))
            throw new BinaryFormatException($"Rune scalar value {value} is not a valid Unicode scalar.");

        return new Rune(value);
    }
}

internal sealed class BigIntegerFormatter : IScalarFormatter
{
    private const int StackBufferSize = 64;

    public bool CanHandle(Type declaredType) => declaredType == typeof(BigInteger);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var number = (BigInteger)value;
        int byteCount = number.GetByteCount();

        Span<byte> buffer = byteCount <= StackBufferSize
            ? stackalloc byte[StackBufferSize]
            : new byte[byteCount];

        number.TryWriteBytes(buffer, out int written);
        writer.WriteBlob(buffer[..written], "BigInteger");
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new BigInteger(reader.ReadBlob("BigInteger"));
}
