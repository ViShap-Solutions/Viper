namespace ViShap.Viper.Formatters;

internal sealed class HashSetFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(HashSet<>) || def == typeof(ISet<>));

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(HashSet<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) =>
        MethodInvokerCache.GetOneArgInvoker(instance.GetType(), "Add", elementType)(instance, element);
}