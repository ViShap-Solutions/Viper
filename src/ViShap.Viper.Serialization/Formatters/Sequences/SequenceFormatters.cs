using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;

namespace ViShap.Viper.Formatters;

/// <summary>
/// Shared plumbing for sequence formatters: the builder is a <c>List&lt;T&gt;</c> that grows as
/// elements arrive, so a declared count never allocates its full size up front.
/// </summary>
internal static class SequenceSupport
{
    public static IEnumerable<object?> Enumerate(object value) =>
        ((IEnumerable)value).Cast<object?>();

    public static object CreateList(Type elementType, int capacityHint) =>
        ActivatorCache.GetOneArgConstructor(
            typeof(List<>).MakeGenericType(elementType),
            typeof(int))(capacityHint);

    public static Array ToArray(object builder, Type elementType)
    {
        var list = (IList)builder;
        var array = Array.CreateInstance(elementType, list.Count);
        list.CopyTo(array, 0);
        return array;
    }
}

/// <summary>
/// Base for every sequence formatter. All interface members are declared virtual here so a derived
/// formatter can adjust them; the engine still owns the count, the loop and the accounting.
/// </summary>
internal abstract class SequenceFormatterBase : ISequenceFormatter
{
    public abstract bool CanHandle(Type declaredType);

    public virtual CountKind CountKind => CountKind.Collection;

    public virtual string CountName => "Collection count";

    public virtual bool ReverseOnWrite => false;

    public virtual bool BuilderIsInstance => true;

    public virtual Type ElementType(Type declaredType) => declaredType.GetGenericArguments()[0];

    public virtual int? CountOf(object value) => CollectionCountCache.CountOf(value);

    public virtual IEnumerable<object?> Enumerate(object value, Type declaredType) =>
        SequenceSupport.Enumerate(value);

    public abstract object CreateBuilder(Type declaredType, int capacityHint);

    public abstract void Add(object builder, object? element, Type declaredType);

    public virtual object Complete(object builder, Type declaredType) => builder;
}

/// <summary>A sequence materialized through a <c>List&lt;T&gt;</c> and then converted.</summary>
internal abstract class ListBackedSequenceFormatter : SequenceFormatterBase
{
    public override bool BuilderIsInstance => false;

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        SequenceSupport.CreateList(ElementType(declaredType), capacityHint);

    public override void Add(object builder, object? element, Type declaredType) =>
        ((IList)builder).Add(element);

    public abstract override object Complete(object builder, Type declaredType);
}

/// <summary>A mutable collection that is built in place, so identity can be registered early.</summary>
internal abstract class MutableSequenceFormatter : SequenceFormatterBase;

internal sealed class ListFormatter : MutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(List<>) || definition == typeof(IList<>) ||
         definition == typeof(ICollection<>) || definition == typeof(IEnumerable<>) ||
         definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>));

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        SequenceSupport.CreateList(ElementType(declaredType), capacityHint);

    public override void Add(object builder, object? element, Type declaredType) =>
        ((IList)builder).Add(element);
}

internal sealed class HashSetFormatter : MutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(HashSet<>) || definition == typeof(ISet<>));

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(typeof(HashSet<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "Add", ElementType(declaredType))(builder, element);
}

internal sealed class SortedSetFormatter : MutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(SortedSet<>);

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(typeof(SortedSet<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "Add", ElementType(declaredType))(builder, element);
}

internal sealed class LinkedListFormatter : MutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(LinkedList<>);

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(typeof(LinkedList<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "AddLast", ElementType(declaredType))(builder, element);
}

internal sealed class ObservableCollectionFormatter : MutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(ObservableCollection<>);

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(
            typeof(ObservableCollection<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        ((IList)builder).Add(element);
}

internal sealed class QueueFormatter : MutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(Queue<>);

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(typeof(Queue<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "Enqueue", ElementType(declaredType))(builder, element);
}

internal sealed class StackFormatter : MutableSequenceFormatter
{
    public override bool ReverseOnWrite => true;

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(Stack<>);

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(typeof(Stack<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "Push", ElementType(declaredType))(builder, element);
}

internal sealed class ConcurrentBagFormatter : MutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ConcurrentBag<>);

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(typeof(ConcurrentBag<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "Add", ElementType(declaredType))(builder, element);
}

internal sealed class ConcurrentQueueFormatter : MutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ConcurrentQueue<>);

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(typeof(ConcurrentQueue<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "Enqueue", ElementType(declaredType))(builder, element);
}

internal sealed class ConcurrentStackFormatter : MutableSequenceFormatter
{
    public override bool ReverseOnWrite => true;

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(ConcurrentStack<>);

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(typeof(ConcurrentStack<>).MakeGenericType(ElementType(declaredType)));

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(builder.GetType(), "Push", ElementType(declaredType))(builder, element);
}

/// <summary>Any concrete <c>ICollection&lt;T&gt;</c> with a parameterless constructor and <c>Add</c>.</summary>
internal sealed class CustomCollectionFormatter : MutableSequenceFormatter
{
    private static readonly ConcurrentDictionary<Type, Type?> ElementTypes = new();

    public override bool CanHandle(Type declaredType) =>
        !declaredType.IsInterface &&
        !declaredType.IsAbstract &&
        Element(declaredType) is not null &&
        declaredType.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is not null;

    public override Type ElementType(Type declaredType) => Element(declaredType)!;

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(declaredType);

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(declaredType, "Add", ElementType(declaredType))(builder, element);

    private static Type? Element(Type declaredType) =>
        ElementTypes.GetOrAdd(declaredType, static type => type
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>))
            ?.GetGenericArguments()[0]);
}

internal sealed class ArrayFormatter : ListBackedSequenceFormatter
{
    public override CountKind CountKind => CountKind.Array;
    public override string CountName => "Array length";

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsArray && declaredType.GetArrayRank() == 1;

    public override Type ElementType(Type declaredType) => declaredType.GetElementType()!;

    public override object Complete(object builder, Type declaredType) =>
        SequenceSupport.ToArray(builder, ElementType(declaredType));
}

/// <summary><c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c> and <c>ArraySegment&lt;T&gt;</c>.</summary>
internal sealed class MemoryLikeFormatter : ListBackedSequenceFormatter
{
    public override string CountName => "Memory element count";

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(Memory<>) || definition == typeof(ReadOnlyMemory<>) ||
         definition == typeof(ArraySegment<>));

    public override IEnumerable<object?> Enumerate(object value, Type declaredType)
    {
        if (IsDefaultSegment(value, declaredType))
            return [];

        var array = (Array)MethodInvokerCache
            .GetInstanceFinalizerInvoker(declaredType, "ToArray")(value);
        return array.Cast<object?>();
    }

    public override object Complete(object builder, Type declaredType)
    {
        var elementType = ElementType(declaredType);
        return ActivatorCache.GetOneArgConstructor(declaredType, elementType.MakeArrayType())(
            SequenceSupport.ToArray(builder, elementType));
    }

    /// <summary>
    /// A default <c>ArraySegment&lt;T&gt;</c> has no backing array and refuses every accessor that
    /// would reach one. It is an empty segment here, because <c>ImmutableArray&lt;T&gt;</c> is the
    /// only container whose default state is distinct from empty on the wire.
    /// </summary>
    private static bool IsDefaultSegment(object value, Type declaredType) =>
        declaredType.GetGenericTypeDefinition() == typeof(ArraySegment<>) &&
        MethodInvokerCache.GetInstanceFinalizerInvoker(declaredType, "get_Array")(value) is null;
}

internal sealed class ReadOnlySequenceFormatter : ListBackedSequenceFormatter
{
    public override string CountName => "ReadOnlySequence length";

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(System.Buffers.ReadOnlySequence<>);

    public override IEnumerable<object?> Enumerate(object value, Type declaredType)
    {
        var array = (Array)ReadOnlySequenceAccessorCache
            .GetToArray(ElementType(declaredType))(value);
        return array.Cast<object?>();
    }

    public override object Complete(object builder, Type declaredType)
    {
        var elementType = ElementType(declaredType);
        return ActivatorCache.GetOneArgConstructor(declaredType, elementType.MakeArrayType())(
            SequenceSupport.ToArray(builder, elementType));
    }
}

internal sealed class ReadOnlyCollectionFormatter : ListBackedSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>);

    public override object Complete(object builder, Type declaredType)
    {
        var elementType = ElementType(declaredType);
        return ActivatorCache.GetOneArgConstructor(
            declaredType,
            typeof(IList<>).MakeGenericType(elementType))(builder);
    }
}

internal sealed class ReadOnlyObservableCollectionFormatter : ISequenceFormatter
{
    public bool BuilderIsInstance => false;

    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(ReadOnlyObservableCollection<>);

    public Type ElementType(Type declaredType) => declaredType.GetGenericArguments()[0];

    public IEnumerable<object?> Enumerate(object value, Type declaredType) =>
        SequenceSupport.Enumerate(value);

    public object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(
            typeof(ObservableCollection<>).MakeGenericType(ElementType(declaredType)));

    public void Add(object builder, object? element, Type declaredType) =>
        ((IList)builder).Add(element);

    public object Complete(object builder, Type declaredType) =>
        ActivatorCache.GetOneArgConstructor(declaredType, builder.GetType())(builder);
}
