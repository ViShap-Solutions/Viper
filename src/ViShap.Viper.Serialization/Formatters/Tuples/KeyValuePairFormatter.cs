namespace ViShap.Viper.Formatters;

internal sealed class KeyValuePairFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        var accessors = DictionaryAccessorCache.GetEntryAccessors(declaredType);
        writer.WriteElement(accessors.KeyGetter(value), args[0]);
        writer.WriteElement(accessors.ValueGetter(value), args[1]);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        var key = reader.ReadElement(args[0]);
        var value = reader.ReadElement(args[1]);

        return ActivatorCache.GetTwoArgConstructor(declaredType, args[0], args[1])(key, value);
    }
}