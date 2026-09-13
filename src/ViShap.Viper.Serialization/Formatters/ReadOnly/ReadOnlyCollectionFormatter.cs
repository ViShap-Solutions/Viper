using System.Collections;
using System.Collections.ObjectModel;

namespace ViShap.Viper.Formatters;

internal sealed class ReadOnlyCollectionFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>);

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
        var list = (IList)ActivatorCache.CreateInstance(typeof(List<>).MakeGenericType(elementType));

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++) list.Add(reader.ReadElement(elementType));

        var listInterface = typeof(IList<>).MakeGenericType(elementType);
        return ActivatorCache.GetOneArgConstructor(declaredType, listInterface)(list);
    }
}