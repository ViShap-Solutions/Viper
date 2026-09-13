namespace ViShap.Viper.Formatters;

internal sealed class IntPtrFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(IntPtr);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.RawWriter.Write(((IntPtr)value).ToInt64());
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new IntPtr(reader.RawReader.ReadInt64());
}