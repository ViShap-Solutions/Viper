namespace ViShap.Viper.Formatters;

internal sealed class LazyFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(Lazy<>);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var valueType = declaredType.GetGenericArguments()[0];
        var innerValue = LazyAccessorCache.GetValueGetter(declaredType)(value);
        writer.WriteElement(innerValue, valueType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var valueType = declaredType.GetGenericArguments()[0];
        var innerValue = reader.ReadElement(valueType);
        return LazyAccessorCache.GetFactory(valueType)(innerValue);
    }
}