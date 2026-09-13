namespace ViShap.Viper.Formatters;

internal sealed class EnumFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType.IsEnum;

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var underlyingType = Enum.GetUnderlyingType(declaredType);
        var underlyingValue = Convert.ChangeType(value, underlyingType);
        TypeFormatterRegistry.Resolve(underlyingType).Write(writer, underlyingValue, underlyingType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var underlyingType = Enum.GetUnderlyingType(declaredType);
        var underlyingValue = TypeFormatterRegistry.Resolve(underlyingType).Read(reader, underlyingType);
        return Enum.ToObject(declaredType, underlyingValue);
    }
}