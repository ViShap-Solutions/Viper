using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace ViShap.Viper.Formatters;

internal static class MapSupport
{
    public static IEnumerable<(object? Key, object? Value)> Enumerate(object value)
    {
        foreach (var entry in (IEnumerable)value)
        {
            var accessors = DictionaryAccessorCache.GetEntryAccessors(entry!.GetType());
            yield return (accessors.KeyGetter(entry), accessors.ValueGetter(entry));
        }
    }
}

/// <summary>
/// Base for every key/value formatter. As with sequences, the engine owns the entry count, the loop,
/// the depth scope, the node budget and identity; the formatter only builds.
/// </summary>
internal abstract class MapFormatterBase : IMapFormatter
{
    public abstract bool CanHandle(Type declaredType);

    public virtual CountKind CountKind => CountKind.Dictionary;

    public virtual string CountName => "Dictionary entry count";

    public virtual bool BuilderIsInstance => true;

    public virtual (Type Key, Type Value) EntryTypes(Type declaredType)
    {
        var arguments = declaredType.GetGenericArguments();
        return (arguments[0], arguments[1]);
    }

    public virtual int? CountOf(object value) => value is ICollection collection ? collection.Count : null;

    public virtual IEnumerable<(object? Key, object? Value)> Enumerate(object value, Type declaredType) =>
        MapSupport.Enumerate(value);

    public abstract object CreateBuilder(Type declaredType, int capacityHint);

    public abstract void Add(object builder, object? key, object? value, Type declaredType);

    public virtual object Complete(object builder, Type declaredType) => builder;
}

/// <summary>A concrete dictionary built in place through a two-argument add method.</summary>
internal abstract class ConcreteMapFormatter : MapFormatterBase
{
    protected abstract Type ConcreteType(Type keyType, Type valueType);

    protected virtual string AddMethodName => "Add";

    public override object CreateBuilder(Type declaredType, int capacityHint)
    {
        var (keyType, valueType) = EntryTypes(declaredType);
        return ActivatorCache.CreateInstance(ConcreteType(keyType, valueType));
    }

    public override void Add(object builder, object? key, object? value, Type declaredType)
    {
        var (keyType, valueType) = EntryTypes(declaredType);
        MethodInvokerCache.GetTwoArgInvoker(builder.GetType(), AddMethodName, keyType, valueType)(
            builder, key, value);
    }
}

internal sealed class DictionaryFormatter : ConcreteMapFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>));

    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
}

internal sealed class SortedDictionaryFormatter : ConcreteMapFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(SortedDictionary<,>);

    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(SortedDictionary<,>).MakeGenericType(keyType, valueType);
}

internal sealed class SortedListFormatter : ConcreteMapFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(SortedList<,>);

    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(SortedList<,>).MakeGenericType(keyType, valueType);
}

internal sealed class ConcurrentDictionaryFormatter : ConcreteMapFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>);

    protected override Type ConcreteType(Type keyType, Type valueType) =>
        typeof(ConcurrentDictionary<,>).MakeGenericType(keyType, valueType);

    protected override string AddMethodName => "TryAdd";
}

internal sealed class ReadOnlyDictionaryFormatter : MapFormatterBase
{
    public override bool BuilderIsInstance => false;

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ReadOnlyDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>));

    public override object CreateBuilder(Type declaredType, int capacityHint)
    {
        var (keyType, valueType) = EntryTypes(declaredType);
        return ActivatorCache.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
    }

    public override void Add(object builder, object? key, object? value, Type declaredType) =>
        ((IDictionary)builder).Add(
            key ?? throw new BinaryFormatException("A dictionary key cannot be null."),
            value);

    public override object Complete(object builder, Type declaredType)
    {
        var (keyType, valueType) = EntryTypes(declaredType);
        return ActivatorCache.GetOneArgConstructor(
            typeof(ReadOnlyDictionary<,>).MakeGenericType(keyType, valueType),
            typeof(IDictionary<,>).MakeGenericType(keyType, valueType))(builder);
    }
}

internal sealed class FrozenDictionaryFormatter : MapFormatterBase
{
    public override bool BuilderIsInstance => false;

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(FrozenDictionary<,>);

    public override object CreateBuilder(Type declaredType, int capacityHint)
    {
        var (keyType, valueType) = EntryTypes(declaredType);
        return ActivatorCache.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
    }

    public override void Add(object builder, object? key, object? value, Type declaredType) =>
        ((IDictionary)builder).Add(
            key ?? throw new BinaryFormatException("A dictionary key cannot be null."),
            value);

    public override object Complete(object builder, Type declaredType)
    {
        var (keyType, valueType) = EntryTypes(declaredType);
        return FrozenFactoryCache.GetToFrozenDictionary(keyType, valueType)(builder);
    }
}

/// <summary>Immutable dictionaries, built through their builder and frozen on completion.</summary>
internal abstract class ImmutableMapFormatter : MapFormatterBase
{
    public override bool BuilderIsInstance => false;

    protected abstract Type BuilderFactoryType { get; }

    public override object CreateBuilder(Type declaredType, int capacityHint)
    {
        var (keyType, valueType) = EntryTypes(declaredType);
        return MethodInvokerCache.GetGenericStaticFactoryInvoker(
            BuilderFactoryType, "CreateBuilder", keyType, valueType)();
    }

    public override void Add(object builder, object? key, object? value, Type declaredType)
    {
        var (keyType, valueType) = EntryTypes(declaredType);
        MethodInvokerCache.GetTwoArgInvoker(builder.GetType(), "Add", keyType, valueType)(
            builder, key, value);
    }

    public override object Complete(object builder, Type declaredType) =>
        MethodInvokerCache.GetInstanceFinalizerInvoker(builder.GetType(), "ToImmutable")(builder);
}

internal sealed class ImmutableDictionaryFormatter : ImmutableMapFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ImmutableDictionary<,>) || definition == typeof(IImmutableDictionary<,>));

    protected override Type BuilderFactoryType => typeof(ImmutableDictionary);
}

internal sealed class ImmutableSortedDictionaryFormatter : ImmutableMapFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(ImmutableSortedDictionary<,>);

    protected override Type BuilderFactoryType => typeof(ImmutableSortedDictionary);
}

/// <summary>
/// A priority queue is a sequence of (element, priority) pairs, so it uses the same engine-driven
/// entry loop as a dictionary.
/// </summary>
internal sealed class PriorityQueueFormatter : MapFormatterBase
{
    public override CountKind CountKind => CountKind.Collection;

    public override string CountName => "PriorityQueue count";

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(PriorityQueue<,>);

    public override int? CountOf(object value) => null;

    public override IEnumerable<(object? Key, object? Value)> Enumerate(object value, Type declaredType)
    {
        var unordered = (IEnumerable)MethodInvokerCache
            .GetInstanceFinalizerInvoker(declaredType, "get_UnorderedItems")(value);

        foreach (var entry in unordered)
        {
            var accessors = TupleAccessorCache.GetAccessors(entry!.GetType());
            yield return (accessors.Getters[0](entry), accessors.Getters[1](entry));
        }
    }

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        ActivatorCache.CreateInstance(declaredType);

    public override void Add(object builder, object? key, object? value, Type declaredType)
    {
        var (elementType, priorityType) = EntryTypes(declaredType);
        MethodInvokerCache.GetTwoArgInvoker(declaredType, "Enqueue", elementType, priorityType)(
            builder, key, value);
    }
}
