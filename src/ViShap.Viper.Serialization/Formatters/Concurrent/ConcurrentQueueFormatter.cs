namespace ViShap.Viper.Formatters;

internal sealed class ConcurrentQueueFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentQueue<>);

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(System.Collections.Concurrent.ConcurrentQueue<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) =>
        MethodInvokerCache.GetOneArgInvoker(instance.GetType(), "Enqueue", elementType)(instance, element);
}