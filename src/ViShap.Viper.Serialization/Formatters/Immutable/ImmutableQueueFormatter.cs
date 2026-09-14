using System.Collections;
using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableQueueFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(ImmutableQueue<>) || def == typeof(IImmutableQueue<>));

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
        
        int count = DeserializationGuard.ValidateCount(
            reader, 
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxCollectionLength, 
            "ImmutableQueue count");
        
        var array = DeserializationGuard.ReadIntoArray(
            reader, 
            elementType, 
            count);

        var create = ImmutableFactoryCache.GetArrayFactory(typeof(ImmutableQueue), "Create", elementType);
        return create(array) ?? throw new BinaryTypeException($"Failed to construct ImmutableQueue<{elementType.Name}>.");
    }
}