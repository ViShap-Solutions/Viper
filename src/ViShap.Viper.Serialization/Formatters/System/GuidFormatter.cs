namespace ViShap.Viper.Formatters;

internal sealed class GuidFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Guid);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[16];
        ((Guid)value).TryWriteBytes(buffer);
        writer.RawWriter.Write(buffer);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) => new Guid(reader.RawReader.ReadBytes(16));
}