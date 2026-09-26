namespace ViShap.Viper.Formatters;

/// <summary>
/// Describes how one family of types is encoded. A formatter never sees a limit, a budget, or a
/// stream: it is handed checked primitives (<see cref="ValueReader"/>/<see cref="ValueWriter"/>) or,
/// for containers, only the builder callbacks the engine drives.
/// </summary>
internal interface ITypeFormatter
{
    bool CanHandle(Type declaredType);
}

/// <summary>
/// A self-contained value with no children and no data-driven allocation: primitives, times, GUIDs,
/// strings, blobs. Everything it can read is bounded by a fixed size or by a checked primitive.
/// </summary>
internal interface IScalarFormatter : ITypeFormatter
{
    void Write(ValueWriter writer, object value, Type declaredType);
    object Read(ValueReader reader, Type declaredType);
}

/// <summary>
/// A sequence of elements. The formatter supplies the element type and a builder; the <em>engine</em>
/// owns the count, the loop, the depth scope, the node budget and the identity registration, so a
/// sequence formatter cannot forget any of them.
/// </summary>
internal interface ISequenceFormatter : ITypeFormatter
{
    CountKind CountKind => CountKind.Collection;

    /// <summary>Label used in limit and format messages, e.g. "Collection count".</summary>
    string CountName => "Collection count";

    Type ElementType(Type declaredType);

    /// <summary>Elements are written in reverse (LIFO containers that enumerate top-down).</summary>
    bool ReverseOnWrite => false;

    /// <summary>
    /// <c>true</c> when <see cref="CreateBuilder"/> already returns the final instance, so identity
    /// can be registered before the children are read and cycles through it can close.
    /// </summary>
    bool BuilderIsInstance => true;

    /// <summary>O(1) element count when the value exposes one; <c>null</c> forces bounded materialization.</summary>
    /// <remarks>
    /// The engine uses it twice: to write a count without materializing the sequence, and to check
    /// that reading produced as many elements as the payload declared.
    /// </remarks>
    int? CountOf(object value) => CollectionCountCache.CountOf(value);

    IEnumerable<object?> Enumerate(object value, Type declaredType);

    object CreateBuilder(Type declaredType, int capacityHint);

    void Add(object builder, object? element, Type declaredType);

    object Complete(object builder, Type declaredType) => builder;
}

/// <summary>
/// A sequence of key/value entries. Same division of labour as <see cref="ISequenceFormatter"/>.
/// </summary>
internal interface IMapFormatter : ITypeFormatter
{
    CountKind CountKind => CountKind.Dictionary;

    string CountName => "Dictionary entry count";

    (Type Key, Type Value) EntryTypes(Type declaredType);

    bool BuilderIsInstance => true;

    int? CountOf(object value) => CollectionCountCache.CountOf(value);

    IEnumerable<(object? Key, object? Value)> Enumerate(object value, Type declaredType);

    object CreateBuilder(Type declaredType, int capacityHint);

    void Add(object builder, object? key, object? value, Type declaredType);

    object Complete(object builder, Type declaredType) => builder;
}

/// <summary>
/// A value with a fixed, type-determined child layout (tuples, key/value pairs, lazy values) or an
/// irregular shape (array rank). The engine has already charged the depth scope, the node budget and
/// identity before calling.
/// <para>
/// The formatter receives a <see cref="CompositeReader"/> or <see cref="CompositeWriter"/>, not the
/// engine and not the payload primitives. Neither surface offers a raw integer, so a count exists
/// only as a validated <see cref="ElementCount"/> and an array shape only as a validated
/// <see cref="ArrayShape"/>: a loop over an unchecked number from the wire is not expressible here,
/// which is what keeps that rule a construction rather than a convention.
/// </para>
/// </summary>
internal interface ICompositeFormatter : ITypeFormatter
{
    void Write(CompositeWriter writer, object value, Type declaredType);
    object Read(CompositeReader reader, Type declaredType);
}
