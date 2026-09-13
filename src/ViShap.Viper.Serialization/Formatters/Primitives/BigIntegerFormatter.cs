using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class BigIntegerFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(BigInteger);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var v = (BigInteger)value;
        int maxLength = v.GetByteCount();
        Span<byte> buffer = maxLength <= 64 ? stackalloc byte[maxLength] : new byte[maxLength];
        v.TryWriteBytes(buffer, out int bytesWritten);

        writer.RawWriter.Write(bytesWritten);
        writer.RawWriter.Write(buffer[..bytesWritten]);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        int length = reader.RawReader.ReadInt32();
        return new BigInteger(reader.RawReader.ReadBytes(length));
    }
}