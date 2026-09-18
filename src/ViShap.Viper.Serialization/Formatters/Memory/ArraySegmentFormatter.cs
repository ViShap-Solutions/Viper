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
        writer.WriteInt32(writer.ValidateCollectionLengthForWrite(items.Count, "ArraySegment element count"));
        foreach (var item in items) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        
        int count = DeserializationGuard.ValidateCount(
            reader, 
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxCollectionLength, 
            "ArraySegment element count");
        
        var array = DeserializationGuard.ReadIntoArray(
            reader, 
            elementType, 
            count);

        return ActivatorCache.GetOneArgConstructor(declaredType, elementType.MakeArrayType())(array);
    }
}