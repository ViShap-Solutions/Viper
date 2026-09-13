namespace ViShap.Viper.Formatters;

internal sealed class SortedListFormatter : DictionaryFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(SortedList<,>);

    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(SortedList<,>).MakeGenericType(keyType, valueType);
}