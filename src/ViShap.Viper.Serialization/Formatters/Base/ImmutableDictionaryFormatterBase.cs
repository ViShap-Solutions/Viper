using System.Collections;

namespace ViShap.Viper.Formatters;

internal abstract class ImmutableDictionaryFormatterBase : ITypeFormatter
{
    public abstract bool CanHandle(Type declaredType);
    protected abstract Type ConcreteType(Type keyType, Type valueType);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        Type keyType = args[0], valueType = args[1];

        var entries = ((IEnumerable)value).Cast<object>().ToList();
        writer.WriteInt32(entries.Count);
        foreach (var entry in entries)
        {
            var accessors = DictionaryAccessorCache.GetEntryAccessors(entry.GetType());
            writer.WriteElement(accessors.KeyGetter(entry), keyType);
            writer.WriteElement(accessors.ValueGetter(entry), valueType);
        }
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        Type keyType = args[0], valueType = args[1];
        var concreteType = ConcreteType(keyType, valueType);

        var createBuilder = MethodInvokerCache.GetStaticFactoryInvoker(concreteType, "CreateBuilder");
        var builder = createBuilder();
        var add = MethodInvokerCache.GetTwoArgInvoker(builder.GetType(), "Add", keyType, valueType);
        var toImmutable = MethodInvokerCache.GetInstanceFinalizerInvoker(builder.GetType(), "ToImmutable");

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
            add(builder, reader.ReadElement(keyType), reader.ReadElement(valueType));

        return toImmutable(builder);
    }
}