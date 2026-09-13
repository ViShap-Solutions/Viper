namespace ViShap.Viper.Formatters;

internal sealed class VersionFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Version);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.RawWriter.Write(((Version)value).ToString());
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Version(reader.RawReader.ReadString());
}