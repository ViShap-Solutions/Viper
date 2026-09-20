namespace ViShap.Viper.Io;

/// <summary>What a count off the wire is going to be used for, and therefore which limit bounds it.</summary>
internal enum CountKind
{
    Array,
    Collection,
    Dictionary
}

/// <summary>
/// A count that has been checked against its limit and charged to the operation's element budget.
/// <para>
/// The only way to obtain one is <see cref="Validate"/>, which performs both steps, and the engine
/// accepts nothing else as a loop bound. "Read a length and allocate it" is therefore not
/// expressible: there is no unchecked path to a count.
/// </para>
/// </summary>
internal readonly struct ElementCount
{
    private const int CapacityGrowthHint = 1024;

    private ElementCount(int value) => Value = value;

    public int Value { get; }

    /// <summary>
    /// Initial capacity for incremental materialization. A declared count never allocates its full
    /// size up front — the buffer grows as elements actually arrive.
    /// </summary>
    public int CapacityHint => Math.Min(Value, CapacityGrowthHint);

    public static implicit operator int(ElementCount count) => count.Value;

    internal static ElementCount Validate(
        int raw,
        CountKind kind,
        SerializationOperation operation,
        string what)
    {
        if (raw < 0)
            throw new BinaryFormatException($"{what} {raw} must be non-negative.");

        long maximum = kind switch
        {
            CountKind.Array => operation.Limits.MaxArrayLength,
            CountKind.Collection => operation.Limits.MaxCollectionLength,
            CountKind.Dictionary => operation.Limits.MaxDictionaryEntries,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        if (raw > maximum)
            throw new BinaryLimitException(
                $"{what} {raw} exceeds the configured maximum of {maximum}.");

        operation.Budget.ConsumeElements(raw);
        return new ElementCount(raw);
    }

    /// <summary>
    /// Validates a multi-dimensional shape: every dimension against <c>MaxArrayLength</c>, the
    /// overflow-safe product, and the resulting element budget. A dimension is bounded on its own as
    /// well as through the product, because a shape such as <c>[0, int.MaxValue]</c> has no elements
    /// yet still describes an array the runtime cannot create.
    /// </summary>
    internal static ElementCount ValidateShape(
        int[] lengths,
        SerializationOperation operation,
        string what)
    {
        ArgumentNullException.ThrowIfNull(lengths);

        long maximum = operation.Limits.MaxArrayLength;
        long total = 1;

        foreach (int length in lengths)
        {
            if (length < 0)
                throw new BinaryFormatException(
                    $"{what}: a dimension length {length} must be non-negative.");

            if (length > maximum)
                throw new BinaryLimitException(
                    $"{what}: a dimension length {length} exceeds the configured maximum of " +
                    $"{maximum}.");

            if (length == 0)
            {
                total = 0;
                continue;
            }

            if (total > maximum / length)
                throw new BinaryLimitException(
                    $"{what}: total element count exceeds the configured maximum of {maximum}.");

            total *= length;
        }

        if (total > maximum)
            throw new BinaryLimitException(
                $"{what}: total element count {total} exceeds the configured maximum of {maximum}.");

        operation.Budget.ConsumeElements(total);
        return new ElementCount((int)total);
    }
}
