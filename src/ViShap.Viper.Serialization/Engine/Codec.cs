namespace ViShap.Viper.Engine;

/// <summary>The form a value is encoded in, in the sense of the wire format's shapes.</summary>
internal enum CodecShape
{
    Scalar,
    Sequence,
    Map,
    Composite,
    Object
}

/// <summary>
/// The engine's codec for one declared type: the whole value, framing included — the null flag, the
/// reference frame, the depth scope and the node charge — then the shape. A codec is built once per
/// type and holds no state of an operation; everything an operation owns is reached through the
/// reader or writer it is handed.
/// </summary>
/// <typeparam name="T">The declared type the codec encodes.</typeparam>
internal abstract class Codec<T>
{
    public abstract CodecShape Shape { get; }

    /// <summary>
    /// The fewest bytes a value of this type occupies on the wire. A collection whose declared count
    /// the remaining bytes could hold at this size receives its whole capacity at once.
    /// </summary>
    public virtual int MinimumWireSize => 1;

    public abstract void Write(ref WireWriter writer, T value);

    public abstract T Read(ref WireReader reader);
}

/// <summary>
/// The codec of <typeparamref name="T"/>, resolved once, the first time the type is used: the codec a
/// context offered for it, else the built-in codec of a scalar or a delegate, else — while the
/// reflection path is enabled — the one <see cref="FormatterRegistry"/> builds, and otherwise one that
/// reports the type unavailable. Finding it afterwards is a static field read.
/// </summary>
internal static class FormatterCache<T>
{
    public static readonly Codec<T> Instance =
        OfferedCodec<T>.Codec
        ?? BuiltInCodecs.Resolve<T>()
        ?? (ReflectionPath.IsEnabled ? FormatterRegistry.Resolve<T>() : new UnavailableCodec<T>());
}

/// <summary>
/// The codec a context offered for <typeparamref name="T"/>, written once, by the first context that
/// offers the type, before the type's <see cref="FormatterCache{T}"/> is first read. Every offer of a
/// type is the codec the rules of <see cref="TypeRules"/> give that type, so which context offers it
/// first does not change what any operation writes or reads.
/// </summary>
internal static class OfferedCodec<T>
{
    private static Codec<T>? _codec;

    public static Codec<T>? Codec => Volatile.Read(ref _codec);

    public static void Offer(Codec<T> codec) => Interlocked.CompareExchange(ref _codec, codec, null);
}
