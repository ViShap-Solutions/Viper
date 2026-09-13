using System.Collections;

namespace ViShap.Viper.Formatters;

internal sealed class PriorityQueueFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(PriorityQueue<,>);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        Type elementType = args[0], priorityType = args[1];

        var unorderedItems = (IEnumerable)MethodInvokerCache.GetInstanceFinalizerInvoker(declaredType, "get_UnorderedItems")(value);
        var items = new List<(object? Element, object? Priority)>();

        foreach (var entry in unorderedItems)
        {
            var accessors = TupleAccessorCache.GetAccessors(entry.GetType());
            items.Add((accessors.Getters[0](entry), accessors.Getters[1](entry)));
        }

        writer.WriteInt32(items.Count);
        foreach (var (element, priority) in items)
        {
            writer.WriteElement(element, elementType);
            writer.WriteElement(priority, priorityType);
        }
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        Type elementType = args[0], priorityType = args[1];

        var queue = ActivatorCache.CreateInstance(declaredType);
        var enqueue = MethodInvokerCache.GetTwoArgInvoker(declaredType, "Enqueue", elementType, priorityType);

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
            enqueue(queue, reader.ReadElement(elementType), reader.ReadElement(priorityType));

        return queue;
    }
}