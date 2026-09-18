using System.Collections;
using System.Collections.Frozen;

namespace ViShap.Viper.Formatters;

internal sealed class FrozenSetFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(FrozenSet<>);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var items = ((IEnumerable)value).Cast<object?>().ToList();
        writer.WriteInt32(writer.ValidateCollectionLengthForWrite(items.Count, "FrozenSet count"));
        foreach (var item in items) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        
        int count = DeserializationGuard.ValidateCount(
            reader, 
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxCollectionLength, 
            "FrozenSet count");

        var listType = typeof(List<>).MakeGenericType(elementType);
        var temp = (IList)ActivatorCache.CreateInstance(listType);
        for (int i = 0; i < count; i++) temp.Add(reader.ReadElement(elementType));

        return FrozenFactoryCache.GetToFrozenSet(elementType)(temp);
    }
}