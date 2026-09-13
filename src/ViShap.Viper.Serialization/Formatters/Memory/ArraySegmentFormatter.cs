using System.Collections;

namespace ViShap.Viper.Formatters;

internal sealed class ArraySegmentFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ArraySegment<>);

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
        int count = reader.ReadInt32();
        var array = Array.CreateInstance(elementType, count);
        for (int i = 0; i < count; i++) array.SetValue(reader.ReadElement(elementType), i);

        return ActivatorCache.GetOneArgConstructor(declaredType, elementType.MakeArrayType())(array);
    }
}