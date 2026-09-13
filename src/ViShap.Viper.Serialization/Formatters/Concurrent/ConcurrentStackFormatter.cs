namespace ViShap.Viper.Formatters;

internal sealed class ConcurrentStackFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentStack<>);

    protected override bool ReverseOnWrite => true;

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(System.Collections.Concurrent.ConcurrentStack<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) =>
        MethodInvokerCache.GetOneArgInvoker(instance.GetType(), "Push", elementType)(instance, element);
}