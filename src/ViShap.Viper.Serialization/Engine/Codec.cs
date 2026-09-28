using System.Diagnostics.CodeAnalysis;

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
/// The codec of <typeparamref name="T"/>, resolved once by <see cref="FormatterRegistry"/> the first
/// time the type is used. Finding it afterwards is a static field read.
/// <para>
/// The engine is entered only through <see cref="Graph"/>, whose entries carry the requirements of the
/// reflection path, so every codec resolved here is resolved on behalf of a caller that stated them.
/// </para>
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The engine is entered only through Graph, which requires unreferenced code.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The engine is entered only through Graph, which requires dynamic code.")]
internal static class FormatterCache<T>
{
    public static readonly Codec<T> Instance = FormatterRegistry.Resolve<T>();
}
