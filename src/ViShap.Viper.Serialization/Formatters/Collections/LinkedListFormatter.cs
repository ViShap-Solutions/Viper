namespace ViShap.Viper.Formatters;

internal sealed class LinkedListFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(LinkedList<>);

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(LinkedList<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) =>
        MethodInvokerCache.GetOneArgInvoker(instance.GetType(), "AddLast", elementType)(instance, element);
}