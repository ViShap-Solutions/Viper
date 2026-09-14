internal sealed class StringFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(string);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.RawWriter.Write((string)value);

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        DeserializationGuard.ReadString(reader, reader.Budget.Limits.MaxStringLength, "String length");
}