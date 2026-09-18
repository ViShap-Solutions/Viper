using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class BigIntegerFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(BigInteger);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var v = (BigInteger)value;
        int maxLength = v.GetByteCount();
        writer.ValidateByteBlobLengthForWrite(maxLength, "BigInteger byte length");
        Span<byte> buffer = maxLength <= 64 ? stackalloc byte[maxLength] : new byte[maxLength];
        v.TryWriteBytes(buffer, out int bytesWritten);

        writer.RawWriter.Write(bytesWritten);
        writer.RawWriter.Write(buffer[..bytesWritten]);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        byte[] bytes = DeserializationGuard.ReadValidatedBytes(
            reader,
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxByteBlobBytes,
            "BigInteger byte length");
        
        return new BigInteger(bytes);
    }
}