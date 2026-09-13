using System.Collections;

namespace ViShap.Viper.Formatters;

internal sealed class ListFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(ICollection<>) ||
         def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>));

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(List<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) => ((IList)instance).Add(element);
}