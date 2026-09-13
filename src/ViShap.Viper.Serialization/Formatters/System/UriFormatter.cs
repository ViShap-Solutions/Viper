namespace ViShap.Viper.Formatters;

internal sealed class UriFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Uri);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.RawWriter.Write(((Uri)value).OriginalString);
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Uri(reader.RawReader.ReadString(), UriKind.RelativeOrAbsolute);
}