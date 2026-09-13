namespace ViShap.Viper.Formatters;

internal sealed class StackFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(Stack<>);

    protected override bool ReverseOnWrite => true;

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(Stack<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) =>
        MethodInvokerCache.GetOneArgInvoker(instance.GetType(), "Push", elementType)(instance, element);
}