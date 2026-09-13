namespace ViShap.Viper.Formatters;

internal sealed class ConcurrentBagFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentBag<>);

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(System.Collections.Concurrent.ConcurrentBag<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) =>
        MethodInvokerCache.GetOneArgInvoker(instance.GetType(), "Add", elementType)(instance, element);
}