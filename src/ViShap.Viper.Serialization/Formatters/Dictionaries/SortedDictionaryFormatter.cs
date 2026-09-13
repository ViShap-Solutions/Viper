namespace ViShap.Viper.Formatters;

internal sealed class SortedDictionaryFormatter : DictionaryFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(SortedDictionary<,>);

    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(SortedDictionary<,>).MakeGenericType(keyType, valueType);
}