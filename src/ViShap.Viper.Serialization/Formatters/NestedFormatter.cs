namespace ViShap.Viper.Formatters;

internal sealed class NestedFormatter : ITypeFormatter
{
    public static readonly NestedFormatter Instance = new();
    public bool CanHandle(Type declaredType) => true;
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) => writer.WriteNestedTracked(value, declaredType);
    public object Read(BinaryPayloadReader reader, Type declaredType) => reader.ReadNestedTracked(declaredType);
}