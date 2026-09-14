using System.Collections;

namespace ViShap.Viper.Formatters;

internal abstract class MutableAddFormatterBase : ITypeFormatter
{
    public abstract bool CanHandle(Type declaredType);
    protected abstract object CreateInstance(Type elementType);
    protected abstract void AddElement(object instance, object? element, Type elementType);
    protected virtual bool ReverseOnWrite => false;

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var items = ((IEnumerable)value).Cast<object?>().ToList();
        if (ReverseOnWrite) items.Reverse();

        writer.WriteInt32(items.Count);
        foreach (var item in items) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var instance = CreateInstance(elementType);

        int count = DeserializationGuard.ValidateCount(
            reader,
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxCollectionLength,
            "Collection count");
        
        for (int i = 0; i < count; i++) AddElement(instance, reader.ReadElement(elementType), elementType);

        return instance;
    }
}