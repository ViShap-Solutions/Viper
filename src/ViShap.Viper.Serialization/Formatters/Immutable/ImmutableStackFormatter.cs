using System.Collections;
using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableStackFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(ImmutableStack<>) || def == typeof(IImmutableStack<>));

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var items = ((IEnumerable)value).Cast<object?>().Reverse().ToList();
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
            "ImmutableStack count");
        
        var array = DeserializationGuard.ReadIntoArray(
            reader, 
            elementType, 
            count);

        var create = ImmutableFactoryCache.GetArrayFactory(typeof(ImmutableStack), "Create", elementType);
        return create(array) ?? throw new BinaryTypeException($"Failed to construct ImmutableStack<{elementType.Name}>.");
    }
}