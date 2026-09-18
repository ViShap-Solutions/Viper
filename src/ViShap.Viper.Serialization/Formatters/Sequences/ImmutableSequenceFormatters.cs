using System.Collections.Frozen;
using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

/// <summary>An immutable set or list built through its builder and frozen on completion.</summary>
internal abstract class ImmutableSequenceFormatter : SequenceFormatterBase
{
    public override bool BuilderIsInstance => false;

    protected abstract Type BuilderFactoryType { get; }

    public override object CreateBuilder(Type declaredType, int capacityHint) =>
        MethodInvokerCache.GetGenericStaticFactoryInvoker(
            BuilderFactoryType, "CreateBuilder", ElementType(declaredType))();

    public override void Add(object builder, object? element, Type declaredType) =>
        MethodInvokerCache.GetOneArgInvoker(
            builder.GetType(), "Add", ElementType(declaredType))(builder, element);

    public override object Complete(object builder, Type declaredType) =>
        MethodInvokerCache.GetInstanceFinalizerInvoker(builder.GetType(), "ToImmutable")(builder);
}

internal sealed class ImmutableListFormatter : ImmutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ImmutableList<>) || definition == typeof(IImmutableList<>));

    protected override Type BuilderFactoryType => typeof(ImmutableList);
}

internal sealed class ImmutableHashSetFormatter : ImmutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ImmutableHashSet<>) || definition == typeof(IImmutableSet<>));

    protected override Type BuilderFactoryType => typeof(ImmutableHashSet);
}

internal sealed class ImmutableSortedSetFormatter : ImmutableSequenceFormatter
{
    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(ImmutableSortedSet<>);

    protected override Type BuilderFactoryType => typeof(ImmutableSortedSet);
}

internal sealed class ImmutableQueueFormatter : ListBackedSequenceFormatter
{
    public override string CountName => "ImmutableQueue count";

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ImmutableQueue<>) || definition == typeof(IImmutableQueue<>));

    public override object Complete(object builder, Type declaredType)
    {
        var elementType = ElementType(declaredType);
        return ImmutableFactoryCache.GetArrayFactory(typeof(ImmutableQueue), "Create", elementType)(
            SequenceSupport.ToArray(builder, elementType));
    }
}

internal sealed class ImmutableStackFormatter : ListBackedSequenceFormatter
{
    public override string CountName => "ImmutableStack count";

    public override bool ReverseOnWrite => true;

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(ImmutableStack<>) || definition == typeof(IImmutableStack<>));

    public override object Complete(object builder, Type declaredType)
    {
        var elementType = ElementType(declaredType);
        return ImmutableFactoryCache.GetArrayFactory(typeof(ImmutableStack), "Create", elementType)(
            SequenceSupport.ToArray(builder, elementType));
    }
}

internal sealed class FrozenSetFormatter : ListBackedSequenceFormatter
{
    public override string CountName => "FrozenSet count";

    public override bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(FrozenSet<>);

    public override object Complete(object builder, Type declaredType) =>
        FrozenFactoryCache.GetToFrozenSet(ElementType(declaredType))(builder);
}
