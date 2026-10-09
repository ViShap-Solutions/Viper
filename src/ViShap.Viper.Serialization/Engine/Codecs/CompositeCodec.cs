namespace ViShap.Viper.Engine;

/// <summary>The validated shape of a multi-dimensional array: its dimensions and their product.</summary>
internal readonly struct ArrayShape(int[] lengths, ElementCount total)
{
    public int[] Lengths { get; } = lengths;

    public ElementCount Total { get; } = total;
}

/// <summary>
/// The engine's codec for a composite. A composite cannot exist before its children are read, so its
/// identity is registered as pending and replaced by the finished value.
/// </summary>
internal sealed class CompositeCodec<T>(ICompositeFormatter<T> formatter) : StructuralCodec<T>
{
    public override CodecShape Shape => CodecShape.Composite;

    protected override bool FoldsNull(ref OperationState state) => formatter.BeginsWithShape;

    protected override void WriteBody(ref WireWriter writer, T value, bool nullFolded) =>
        CompositeWriter.Encode(formatter, ref writer, value, nullFolded);

    protected override T ReadBody(ref WireReader reader, int referenceId, bool nullFolded)
    {
        if (referenceId >= 0)
            Register(ref reader, referenceId, ReadReferenceTable.Pending);

        reader.State.Trace?.Shape(TraceShape.Composite, 0);
        var value = CompositeReader.Decode(formatter, ref reader, nullFolded);

        if (referenceId >= 0)
            Complete(ref reader, referenceId, value!);

        return value;
    }
}

/// <summary>
/// Everything a composite formatter may do on the way in, and nothing else.
/// <para>
/// A composite has a fixed, type-determined child layout, so the engine cannot drive it the way it
/// drives a sequence. What it can do is withhold the means to write an unchecked loop: this surface
/// exposes no raw integer read. An array shape can only be obtained as a validated
/// <see cref="ArrayShape"/>, and the elements behind it are read by the engine's own loop. The
/// payload primitives are not reachable from here at all.
/// </para>
/// <para>
/// Only <see cref="Decode"/> creates a surface: it lends the engine's reader to the surface for the
/// length of one formatter call and takes it back, advanced, when the formatter returns.
/// </para>
/// </summary>
internal ref struct CompositeReader
{
    private WireReader _values;
    private bool _nullFolded;
    private int _items;

    private CompositeReader(WireReader values, bool nullFolded)
    {
        _values = values;
        _nullFolded = nullFolded;
    }

    /// <summary>
    /// Reads one composite value through <paramref name="formatter"/>. When
    /// <paramref name="nullFolded"/>, the array shape it begins with carries the value's null, and its
    /// rank was written one higher.
    /// </summary>
    public static T Decode<T>(ICompositeFormatter<T> formatter, ref WireReader reader, bool nullFolded)
    {
        var surface = new CompositeReader(reader, nullFolded);
        var value = formatter.Read(ref surface);
        reader = surface._values;
        return value;
    }

    /// <summary>Reads one framed child value of the declared type.</summary>
    public TValue ReadValue<TValue>()
    {
        _items++;
        _values.State.Trace?.LabelItem(_items);
        return FormatterCache<TValue>.Instance.Read(ref _values);
    }

    /// <summary>
    /// Reads an array shape: the rank, which must match the declared type, then one length per
    /// dimension, validated as a whole before any of it is used.
    /// </summary>
    public ArrayShape ReadShape(int expectedRank, string what)
    {
        int rank = _values.ReadFolded(_nullFolded, "Array rank");
        _nullFolded = false;

        if (rank != expectedRank)
            throw new BinaryFormatException(
                $"Array rank {rank} does not match the declared array rank {expectedRank}.");

        var lengths = new int[rank];
        for (int dimension = 0; dimension < rank; dimension++)
            lengths[dimension] = _values.Read7BitEncodedInt("Array dimension length");

        return new ArrayShape(lengths, ElementCount.ValidateShape(lengths, ref _values.State, what));
    }

    /// <summary>Reads the elements a validated shape declares, in order, into an array of exactly that length.</summary>
    public TElement[] ReadElements<TElement>(ElementCount count) =>
        Elements.ReadArray<TElement>(ref _values, count);
}

/// <summary>The write-side mirror of <see cref="CompositeReader"/>, with the same restrictions.</summary>
internal ref struct CompositeWriter
{
    private WireWriter _values;
    private bool _nullFolded;

    private CompositeWriter(WireWriter values, bool nullFolded)
    {
        _values = values;
        _nullFolded = nullFolded;
    }

    /// <summary>
    /// Writes one composite value through <paramref name="formatter"/>. When
    /// <paramref name="nullFolded"/>, the array shape it begins with carries the value's null, and its
    /// rank is written one higher.
    /// </summary>
    public static void Encode<T>(ICompositeFormatter<T> formatter, ref WireWriter writer, T value, bool nullFolded)
    {
        var surface = new CompositeWriter(writer, nullFolded);
        formatter.Write(ref surface, value);
        writer = surface._values;
    }

    /// <summary>Writes one framed child value under the declared type.</summary>
    public void WriteValue<TValue>(TValue value) => FormatterCache<TValue>.Instance.Write(ref _values, value);

    /// <summary>
    /// Validates an array shape as a whole — every dimension, the overflow-safe product and the
    /// element budget — then writes the rank and the dimensions.
    /// </summary>
    public ElementCount WriteShape(int[] lengths, string what)
    {
        var total = ElementCount.ValidateShape(lengths, ref _values.State, what);

        _values.WriteFolded(lengths.Length, _nullFolded, "Array rank");
        _nullFolded = false;

        foreach (int length in lengths)
            _values.Write7BitEncodedInt(length);

        return total;
    }

    /// <summary>Writes the elements a shape declared, each as a framed value.</summary>
    public void WriteElements<TElement>(ReadOnlySpan<TElement> elements) =>
        Elements.Write(ref _values, elements);
}
