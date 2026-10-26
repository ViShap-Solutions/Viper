namespace ViShap.Viper.Engine;

/// <summary>
/// An observer of a read: the engine's codecs report to it, and nothing else does. It is
/// <see langword="null"/> in the state of every serializer call and is set only by the diagnostics,
/// which read a frame for a person. It receives offsets, names, kinds and values — each value through
/// a generic call, so the engine boxes nothing to report it — and never a reader or a byte, and it
/// changes nothing a read does: a read with an observer consumes, checks and returns exactly what the
/// same read without one would.
/// </summary>
internal interface IWireTrace
{
    /// <summary>Names the next value the engine reads: a member, an element, an entry, a composite item.</summary>
    void Label(string name);

    /// <summary>Names the next value the engine reads by its position in a sequence: <c>[index]</c>.</summary>
    void LabelIndex(int index);

    /// <summary>Names the key of the map entry at <paramref name="index"/>: <c>[index].Key</c>.</summary>
    void LabelMapKey(int index);

    /// <summary>Names the <paramref name="item"/>th child of a composite, counted from 1: <c>Item1</c>.</summary>
    void LabelItem(int item);

    /// <summary>
    /// Names the value of a map entry after the key it was read with, when the key rendered as a
    /// scalar: <c>{key}</c>; otherwise <c>[index].Value</c>.
    /// </summary>
    void LabelMapValue(int index);

    /// <summary>A value of <paramref name="declaredType"/> begins at <paramref name="offset"/> of the payload.</summary>
    void Begin(Type declaredType, long offset);

    /// <summary>
    /// The value begun last carries only its null flag, and the value of the underlying type that
    /// follows is the same value: the next <see cref="Begin"/> continues the current value rather than
    /// opening a child of it.
    /// </summary>
    void Continue();

    /// <summary>The value begun last is null.</summary>
    void Null();

    /// <summary>The value begun last carries a reference frame: a first occurrence of <paramref name="id"/>, or a back reference to it that ends the value.</summary>
    void Reference(int id, bool back);

    /// <summary>The value begun last has <paramref name="kind"/> and <paramref name="count"/> elements, entries, members or fields.</summary>
    void Shape(TraceShape kind, int count);

    /// <summary>The value begun last carries union tag <paramref name="tag"/>, naming <paramref name="runtimeType"/>.</summary>
    void Union(byte tag, Type runtimeType);

    /// <summary>The scalar value begun last is <paramref name="value"/>.</summary>
    void Value<T>(T value);

    /// <summary>A keyed field of <paramref name="key"/>, declaring <paramref name="length"/> bytes, begins at <paramref name="offset"/>.</summary>
    void Field(int key, int length, long offset);

    /// <summary>The keyed field begun last ends at <paramref name="offset"/>; <paramref name="known"/> says whether the contract read it or it was skipped.</summary>
    void EndField(bool known, long offset);

    /// <summary>The value begun last ends at <paramref name="offset"/>.</summary>
    void End(long offset);
}

/// <summary>The shape of a value the engine reports to an <see cref="IWireTrace"/>.</summary>
internal enum TraceShape
{
    Sequence,
    Map,
    Object,
    KeyedObject,
    Composite
}
