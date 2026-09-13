namespace ViShap.Viper.Formatters;

internal sealed class SortedSetFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(SortedSet<>);

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(SortedSet<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) =>
        MethodInvokerCache.GetOneArgInvoker(instance.GetType(), "Add", elementType)(instance, element);
}