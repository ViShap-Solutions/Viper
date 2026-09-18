using System.Collections;

namespace ViShap.Viper.Formatters;

internal abstract class ImmutableBuilderFormatterBase : ITypeFormatter
{
    public abstract bool CanHandle(Type declaredType);
    protected abstract Type BuilderFactoryType(Type elementType);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var items = ((IEnumerable)value).Cast<object?>().ToList();
        writer.WriteInt32(writer.ValidateCollectionLengthForWrite(items.Count, "Collection count"));
        foreach (var item in items) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];
        var factoryType = BuilderFactoryType(elementType);

        var createBuilder = MethodInvokerCache.GetGenericStaticFactoryInvoker(factoryType, "CreateBuilder", elementType);
        var builder = createBuilder();
        var add = MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "Add", elementType);
        var toImmutable = MethodInvokerCache.GetInstanceFinalizerInvoker(builder.GetType(), "ToImmutable");

        int count = DeserializationGuard.ValidateCount(
            reader,
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxCollectionLength,
            "Collection count");
        
        for (int i = 0; i < count; i++) add(builder, reader.ReadElement(elementType));

        return toImmutable(builder);
    }
}