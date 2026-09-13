namespace ViShap.Viper.Formatters;

internal sealed class DictionaryFormatter : DictionaryFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>));

    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
}