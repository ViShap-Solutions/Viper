using System.Collections;

namespace ViShap.Viper.Formatters;

internal sealed class ObservableCollectionFormatter : MutableAddFormatterBase
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(System.Collections.ObjectModel.ObservableCollection<>);

    protected override object CreateInstance(Type elementType) =>
        ActivatorCache.CreateInstance(typeof(System.Collections.ObjectModel.ObservableCollection<>).MakeGenericType(elementType));

    protected override void AddElement(object instance, object? element, Type elementType) => ((IList)instance).Add(element);
}