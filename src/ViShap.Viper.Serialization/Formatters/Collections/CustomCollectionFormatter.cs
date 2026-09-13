using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace ViShap.Viper.Formatters;

internal sealed class CustomCollectionFormatter : ITypeFormatter
{
    private static readonly ConcurrentDictionary<Type, Type?> ElementTypeCache = new();

    public bool CanHandle(Type declaredType) =>
        !declaredType.IsInterface &&
        GetElementType(declaredType) is not null &&
        declaredType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is not null;

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var elementType = GetElementType(declaredType)!;
        var items = ((IEnumerable)value).Cast<object?>().ToList();
        writer.WriteInt32(items.Count);
        foreach (var item in items) writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = GetElementType(declaredType)!;
        var instance = ActivatorCache.CreateInstance(declaredType);
        var add = MethodInvokerCache.GetOneArgInvoker(declaredType, "Add", elementType);

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++) add(instance, reader.ReadElement(elementType));

        return instance;
    }

    private static Type? GetElementType(Type declaredType) =>
        ElementTypeCache.GetOrAdd(declaredType, static t =>
            t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>))
                ?.GetGenericArguments()[0]);
}