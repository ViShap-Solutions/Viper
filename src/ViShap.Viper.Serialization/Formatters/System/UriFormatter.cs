namespace ViShap.Viper.Formatters;

internal sealed class UriFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Uri);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.WriteString(((Uri)value).OriginalString);
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Uri(
            DeserializationGuard.ReadString(
                reader,
                reader.Budget.Limits.MaxStringBytes,
                "URI length"),
            UriKind.RelativeOrAbsolute);
}