using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Security;

/// <summary>
/// Per-operation resource accounting. The budget answers "what has this operation already consumed";
/// it lives inside the one <see cref="OperationState"/> of a public call and is never shared between
/// calls or threads.
/// </summary>
internal struct SerializationBudget(SerializationLimits limits)
{
    private long _totalElements;
    private long _objectGraphNodes;
    private long _keyedFields;
    private int _depth;

    public readonly SerializationLimits Limits { get; } = limits;
    public readonly int Depth => _depth;
    public readonly long TotalElements => _totalElements;
    public readonly long ObjectGraphNodes => _objectGraphNodes;
    public readonly long KeyedFields => _keyedFields;

    public void ConsumeElements(long count) =>
        Consume(ref _totalElements, count, Limits.MaxTotalElements,
            "Cumulative element count across the payload",
            nameof(SerializationLimits.MaxTotalElements));

    public void ConsumeObjectGraphNodes(long count) =>
        Consume(ref _objectGraphNodes, count, Limits.MaxObjectGraphNodes,
            "Object graph node count",
            nameof(SerializationLimits.MaxObjectGraphNodes));

    public void ConsumeKeyedFields(long count) =>
        Consume(ref _keyedFields, count, Limits.MaxTotalKeyedFields,
            "Cumulative keyed field count across the payload",
            nameof(SerializationLimits.MaxTotalKeyedFields));

    /// <summary>
    /// Enters one structural level. The returned scope restores the previous depth exactly once;
    /// it is a <c>ref struct</c> over this budget, so entering a node costs no allocation.
    /// </summary>
    [UnscopedRef]
    public DepthScope EnterDepth()
    {
        if (_depth >= Limits.MaxDepth)
            throw new BinaryLimitException(
                $"Nesting depth exceeds the configured limit of {Limits.MaxDepth} " +
                $"({nameof(SerializationLimits.MaxDepth)}).");

        _depth++;
        return new DepthScope(ref this);
    }

    private static void Consume(
        ref long consumed, long count, long maximum, string what, string limit)
    {
        if (count < 0)
            throw new BinaryFormatException($"{what} {count} must be non-negative.");

        if (count > maximum - consumed)
            throw new BinaryLimitException(
                $"{what} exceeds the configured limit of {maximum} ({limit}).");

        consumed += count;
    }

    internal ref struct DepthScope
    {
        private ref SerializationBudget _owner;

        internal DepthScope(ref SerializationBudget owner) => _owner = ref owner;

        public void Dispose()
        {
            if (Unsafe.IsNullRef(ref _owner))
                return;

            if (_owner._depth > 0)
                _owner._depth--;

            _owner = ref Unsafe.NullRef<SerializationBudget>();
        }
    }
}
