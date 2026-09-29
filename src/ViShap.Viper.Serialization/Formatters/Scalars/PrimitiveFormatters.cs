using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ViShap.Viper.Formatters;

internal sealed class BooleanFormatter : IScalarFormatter<bool>
{
    public int MinimumWireSize => sizeof(bool);
    public void Write(ref WireWriter writer, bool value) => writer.WriteBoolean(value);
    public bool Read(ref WireReader reader) => reader.ReadBoolean();
}

internal sealed class ByteFormatter : IScalarFormatter<byte>
{
    public int MinimumWireSize => sizeof(byte);
    public void Write(ref WireWriter writer, byte value) => writer.WriteByte(value);
    public byte Read(ref WireReader reader) => reader.ReadByte();
}

internal sealed class SByteFormatter : IScalarFormatter<sbyte>
{
    public int MinimumWireSize => sizeof(sbyte);
    public void Write(ref WireWriter writer, sbyte value) => writer.WriteSByte(value);
    public sbyte Read(ref WireReader reader) => reader.ReadSByte();
}

internal sealed class Int16Formatter : IScalarFormatter<short>
{
    public int MinimumWireSize => sizeof(short);
    public void Write(ref WireWriter writer, short value) => writer.WriteInt16(value);
    public short Read(ref WireReader reader) => reader.ReadInt16();
}

internal sealed class UInt16Formatter : IScalarFormatter<ushort>
{
    public int MinimumWireSize => sizeof(ushort);
    public void Write(ref WireWriter writer, ushort value) => writer.WriteUInt16(value);
    public ushort Read(ref WireReader reader) => reader.ReadUInt16();
}

internal sealed class Int32Formatter : IScalarFormatter<int>
{
    public int MinimumWireSize => sizeof(int);
    public void Write(ref WireWriter writer, int value) => writer.WriteInt32(value);
    public int Read(ref WireReader reader) => reader.ReadInt32();
}

internal sealed class UInt32Formatter : IScalarFormatter<uint>
{
    public int MinimumWireSize => sizeof(uint);
    public void Write(ref WireWriter writer, uint value) => writer.WriteUInt32(value);
    public uint Read(ref WireReader reader) => reader.ReadUInt32();
}

internal sealed class Int64Formatter : IScalarFormatter<long>
{
    public int MinimumWireSize => sizeof(long);
    public void Write(ref WireWriter writer, long value) => writer.WriteInt64(value);
    public long Read(ref WireReader reader) => reader.ReadInt64();
}

internal sealed class UInt64Formatter : IScalarFormatter<ulong>
{
    public int MinimumWireSize => sizeof(ulong);
    public void Write(ref WireWriter writer, ulong value) => writer.WriteUInt64(value);
    public ulong Read(ref WireReader reader) => reader.ReadUInt64();
}

internal sealed class SingleFormatter : IScalarFormatter<float>
{
    public int MinimumWireSize => sizeof(float);
    public void Write(ref WireWriter writer, float value) => writer.WriteSingle(value);
    public float Read(ref WireReader reader) => reader.ReadSingle();
}

internal sealed class DoubleFormatter : IScalarFormatter<double>
{
    public int MinimumWireSize => sizeof(double);
    public void Write(ref WireWriter writer, double value) => writer.WriteDouble(value);
    public double Read(ref WireReader reader) => reader.ReadDouble();
}

internal sealed class DecimalFormatter : IScalarFormatter<decimal>
{
    public int MinimumWireSize => sizeof(decimal);
    public void Write(ref WireWriter writer, decimal value) => writer.WriteDecimal(value);
    public decimal Read(ref WireReader reader) => reader.ReadDecimal();
}

internal sealed class CharFormatter : IScalarFormatter<char>
{
    public int MinimumWireSize => sizeof(char);
    public void Write(ref WireWriter writer, char value) => writer.WriteChar(value);
    public char Read(ref WireReader reader) => reader.ReadChar();
}

internal sealed class StringFormatter : IScalarFormatter<string>
{
    public int MinimumWireSize => 1;
    public void Write(ref WireWriter writer, string value) => writer.WriteString(value);
    public string Read(ref WireReader reader) => reader.ReadString();
}

/// <summary>
/// An enum travels as its underlying integer. The value is reinterpreted in place, never converted
/// through <see cref="object"/>, so an enum is not boxed in either direction.
/// </summary>
internal sealed class EnumFormatter<TEnum> : IScalarFormatter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly bool IsBoolean = Enum.GetUnderlyingType(typeof(TEnum)) == typeof(bool);

    public int MinimumWireSize => Unsafe.SizeOf<TEnum>();

    public void Write(ref WireWriter writer, TEnum value)
    {
        switch (Unsafe.SizeOf<TEnum>())
        {
            case 1 when IsBoolean:
                writer.WriteBoolean(Unsafe.As<TEnum, bool>(ref value));
                break;
            case 1:
                writer.WriteByte(Unsafe.As<TEnum, byte>(ref value));
                break;
            case 2:
                writer.WriteUInt16(Unsafe.As<TEnum, ushort>(ref value));
                break;
            case 4:
                writer.WriteUInt32(Unsafe.As<TEnum, uint>(ref value));
                break;
            default:
                writer.WriteUInt64(Unsafe.As<TEnum, ulong>(ref value));
                break;
        }
    }

    public TEnum Read(ref WireReader reader)
    {
        switch (Unsafe.SizeOf<TEnum>())
        {
            case 1 when IsBoolean:
                bool flag = reader.ReadBoolean();
                return Unsafe.As<bool, TEnum>(ref flag);
            case 1:
                byte b = reader.ReadByte();
                return Unsafe.As<byte, TEnum>(ref b);
            case 2:
                ushort s = reader.ReadUInt16();
                return Unsafe.As<ushort, TEnum>(ref s);
            case 4:
                uint i = reader.ReadUInt32();
                return Unsafe.As<uint, TEnum>(ref i);
            default:
                ulong l = reader.ReadUInt64();
                return Unsafe.As<ulong, TEnum>(ref l);
        }
    }
}

internal sealed class HalfFormatter : IScalarFormatter<Half>
{
    public int MinimumWireSize => sizeof(short);

    public void Write(ref WireWriter writer, Half value) =>
        writer.WriteInt16(BitConverter.HalfToInt16Bits(value));

    public Half Read(ref WireReader reader) => BitConverter.Int16BitsToHalf(reader.ReadInt16());
}

internal sealed class Int128Formatter : IScalarFormatter<Int128>
{
    public int MinimumWireSize => 16;

    public void Write(ref WireWriter writer, Int128 value)
    {
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteInt128LittleEndian(buffer, value);
        writer.Write(buffer);
    }

    public Int128 Read(ref WireReader reader)
    {
        Span<byte> buffer = stackalloc byte[16];
        reader.ReadExact(buffer, "Int128");
        return BinaryPrimitives.ReadInt128LittleEndian(buffer);
    }
}

internal sealed class UInt128Formatter : IScalarFormatter<UInt128>
{
    public int MinimumWireSize => 16;

    public void Write(ref WireWriter writer, UInt128 value)
    {
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128LittleEndian(buffer, value);
        writer.Write(buffer);
    }

    public UInt128 Read(ref WireReader reader)
    {
        Span<byte> buffer = stackalloc byte[16];
        reader.ReadExact(buffer, "UInt128");
        return BinaryPrimitives.ReadUInt128LittleEndian(buffer);
    }
}

internal sealed class IntPtrFormatter : IScalarFormatter<IntPtr>
{
    public int MinimumWireSize => sizeof(long);
    public void Write(ref WireWriter writer, IntPtr value) => writer.WriteInt64(value.ToInt64());
    public IntPtr Read(ref WireReader reader) => new(reader.ReadInt64());
}

internal sealed class UIntPtrFormatter : IScalarFormatter<UIntPtr>
{
    public int MinimumWireSize => sizeof(ulong);
    public void Write(ref WireWriter writer, UIntPtr value) => writer.WriteUInt64(value.ToUInt64());
    public UIntPtr Read(ref WireReader reader) => new(reader.ReadUInt64());
}

internal sealed class RuneFormatter : IScalarFormatter<Rune>
{
    public int MinimumWireSize => sizeof(int);

    public void Write(ref WireWriter writer, Rune value) => writer.WriteInt32(value.Value);

    public Rune Read(ref WireReader reader)
    {
        int value = reader.ReadInt32();
        if (!Rune.IsValid(value))
            throw new BinaryFormatException($"Rune scalar value {value} is not a valid Unicode scalar.");

        return new Rune(value);
    }
}

internal sealed class BigIntegerFormatter : IScalarFormatter<BigInteger>
{
    private const int StackBufferSize = 64;

    public int MinimumWireSize => 1;

    public void Write(ref WireWriter writer, BigInteger value)
    {
        int byteCount = value.GetByteCount();

        Span<byte> buffer = byteCount <= StackBufferSize
            ? stackalloc byte[StackBufferSize]
            : new byte[byteCount];

        value.TryWriteBytes(buffer, out int written);
        writer.WriteBlob(buffer[..written], "BigInteger");
    }

    public BigInteger Read(ref WireReader reader)
    {
        byte[] bytes = reader.ReadBlob("BigInteger");
        var value = new BigInteger(bytes);

        if (bytes.Length == 0 || value.GetByteCount() != bytes.Length)
            throw new BinaryFormatException(
                $"BigInteger is not in its shortest two's-complement form: {bytes.Length} byte(s) " +
                $"hold a value that is written in {value.GetByteCount()}.");

        return value;
    }
}
