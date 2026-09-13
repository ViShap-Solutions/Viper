using System.Collections;
using System.Collections.ObjectModel;

namespace ViShap.Viper.Formatters;

internal sealed class ReadOnlyObservableCollectionFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ReadOnlyObservableCollection<>);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var items = ((IEnumerable)value).Cast<object?>().ToList();
        writer.WriteInt32(items.Count);
        foreach (var item in items) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var observableType = typeof(ObservableCollection<>).MakeGenericType(elementType);
        var observable = ActivatorCache.CreateInstance(observableType);
        var add = MethodInvokerCache.GetOneArgInvoker(observableType, "Add", elementType);

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++) add(observable, reader.ReadElement(elementType));

        return ActivatorCache.GetOneArgConstructor(declaredType, observableType)(observable);
    }
}