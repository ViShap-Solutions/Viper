namespace ViShap.Viper.Formatters;

internal sealed class UIntPtrFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(UIntPtr);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.RawWriter.Write(((UIntPtr)value).ToUInt64());
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new UIntPtr(reader.RawReader.ReadUInt64());
}