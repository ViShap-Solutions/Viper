namespace ViShap.Viper.Formatters;

internal sealed class PrimitiveFormatter<T>(Action<BinaryWriter, T> write, Func<BinaryReader, T> read) : ITypeFormatter
    where T : notnull
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(T);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) => write(writer.RawWriter, (T)value);
    public object Read(BinaryPayloadReader reader, Type declaredType) => read(reader.RawReader)!;
}