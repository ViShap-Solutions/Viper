namespace ViShap.Viper.Engine;

/// <summary>The validated shape of a multi-dimensional array: its dimensions and their product.</summary>
internal readonly struct ArrayShape(int[] lengths, ElementCount total)
{
    public int[] Lengths { get; } = lengths;

    public ElementCount Total { get; } = total;
}

/// <summary>
/// Everything a composite formatter may do on the way in, and nothing else.
/// <para>
/// A composite has a fixed, type-determined child layout, so the engine cannot drive its loop the way
/// it drives a sequence's. What it can do is withhold the means to write an unchecked one: this
/// surface exposes no raw integer read, so a count can only be obtained as a validated
/// <see cref="ElementCount"/> and an array shape only as a validated <see cref="ArrayShape"/>. The
/// engine's own recursion and the payload primitives are not reachable from here at all.
/// </para>
/// <para>
/// Only <see cref="Decode"/> creates a surface: it lends the engine's reader to the surface for the
/// length of one formatter call and takes it back, advanced, when the formatter returns.
/// </para>
/// </summary>
internal ref struct CompositeReader
{
    private readonly GraphReader _engine;
    private WireReader _values;

    private CompositeReader(GraphReader engine, WireReader values)
    {
        _engine = engine;
        _values = values;
    }

    /// <summary>Reads one composite value through <paramref name="formatter"/>.</summary>
    public static object Decode(
        ICompositeFormatter formatter,
        GraphReader engine,
        ref WireReader reader,
        Type declaredType)
    {
        var surface = new CompositeReader(engine, reader);
        var value = formatter.Read(ref surface, declaredType);
        reader = surface._values;
        return value;
    }

    /// <summary>Reads one framed child value of the declared type.</summary>
    public object? ReadValue(Type declaredType) => _engine.ReadValue(ref _values, declaredType);

    /// <summary>Reads a presence flag, the only single-byte decision a composite layout may carry.</summary>
    public bool ReadFlag() => _values.ReadBoolean();

    /// <summary>Reads a count, validated against its limit and charged to the element budget.</summary>
    public ElementCount ReadCount(CountKind kind, string what) => _values.ReadCount(kind, what);

    /// <summary>
    /// Reads an array shape: the rank, which must match the declared type, then one length per
    /// dimension, validated as a whole before any of it is used.
    /// </summary>
    public ArrayShape ReadShape(int expectedRank, string what)
    {
        int rank = _values.ReadInt32();
        if (rank != expectedRank)
            throw new BinaryFormatException(
                $"Array rank {rank} does not match the declared array rank {expectedRank}.");

        var lengths = new int[rank];
        for (int dimension = 0; dimension < rank; dimension++)
            lengths[dimension] = _values.ReadInt32();

        return new ArrayShape(lengths, ElementCount.ValidateShape(lengths, _values.Operation, what));
    }
}

/// <summary>The write-side mirror of <see cref="CompositeReader"/>, with the same restrictions.</summary>
internal ref struct CompositeWriter
{
    private readonly GraphWriter _engine;
    private WireWriter _values;

    private CompositeWriter(GraphWriter engine, WireWriter values)
    {
        _engine = engine;
        _values = values;
    }

    /// <summary>Writes one composite value through <paramref name="formatter"/>.</summary>
    public static void Encode(
        ICompositeFormatter formatter,
        GraphWriter engine,
        ref WireWriter writer,
        object value,
        Type declaredType)
    {
        var surface = new CompositeWriter(engine, writer);
        formatter.Write(ref surface, value, declaredType);
        writer = surface._values;
    }

    /// <summary>Writes one framed child value under the declared type.</summary>
    public void WriteValue(object? value, Type declaredType) =>
        _engine.WriteValue(ref _values, value, declaredType);

    /// <summary>Writes a presence flag.</summary>
    public void WriteFlag(bool value) => _values.WriteBoolean(value);

    /// <summary>Validates a count against its limit and the element budget, then writes it.</summary>
    public ElementCount WriteCount(int count, CountKind kind, string what) =>
        _values.WriteCount(count, kind, what);

    /// <summary>
    /// Validates an array shape as a whole — every dimension, the overflow-safe product and the
    /// element budget — then writes the rank and the dimensions.
    /// </summary>
    public ElementCount WriteShape(int[] lengths, string what)
    {
        var total = ElementCount.ValidateShape(lengths, _values.Operation, what);

        _values.WriteInt32(lengths.Length);
        foreach (int length in lengths)
            _values.WriteInt32(length);

        return total;
    }
}
