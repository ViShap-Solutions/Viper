using System.Buffers;

namespace ViShap.Viper.Pipeline;

/// <summary>
/// One wire format. A pipeline owns framing and phase ordering; it receives the operation from the
/// public API and never invents limits, a budget or a key of its own. It builds a whole frame in the
/// serializer's own buffers and hands it back, so the caller decides where the bytes go and nothing
/// reaches a destination before the frame is complete. It reads from memory only: a source that
/// delivers bytes over time is first buffered as far as <see cref="Measure"/> says the frame extends.
/// </summary>
internal interface IFormatPipeline
{
    int Version { get; }

    /// <summary>Encodes <paramref name="data"/> as one frame, which the caller copies out and disposes.</summary>
    EncodedFrame Write<T>(T data, SerializationOperation operation);

    /// <summary>
    /// How far the frame at the start of <paramref name="buffered"/> extends, as far as these bytes
    /// tell. The header's checks that need no payload run here, so a source is never asked for bytes a
    /// frame could not legitimately hold.
    /// </summary>
    /// <param name="buffered">The first bytes of the frame, as far as they have arrived.</param>
    /// <param name="sourceEnded">Whether the source can deliver no more bytes than these.</param>
    /// <param name="available">
    /// The bytes the source can still deliver from the frame's start, or <see cref="long.MaxValue"/>
    /// when the source cannot tell.
    /// </param>
    /// <param name="operation">The operation reading the frame.</param>
    FrameExtent Measure(ReadOnlySpan<byte> buffered, bool sourceEnded, long available, SerializationOperation operation);

    /// <summary>
    /// Reads one value from bytes that start with a frame, and reports how many of them the frame
    /// occupies; bytes after it are not read.
    /// </summary>
    object? Read(
        ReadOnlySpan<byte> source,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation,
        out long consumed);

    /// <summary>
    /// Reads one value from segmented bytes that start with a frame, and reports how many of them the
    /// frame occupies; bytes after it are not read.
    /// </summary>
    object? Read(
        ReadOnlySequence<byte> source,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation,
        out long consumed);
}

/// <summary>
/// What the first bytes of a frame say about its extent: more bytes are needed before it is known,
/// the frame's whole length, or — for a format whose frame declares no length — that its end is found
/// only by decoding it, within a read-ahead bound.
/// </summary>
internal readonly struct FrameExtent
{
    private FrameExtent(FrameExtentKind kind, long length)
    {
        Kind = kind;
        Length = length;
    }

    public FrameExtentKind Kind { get; }

    /// <summary>
    /// The bytes needed from the frame's start (<see cref="FrameExtentKind.NeedMore"/>), the frame's
    /// length (<see cref="FrameExtentKind.Known"/>), or the most bytes to read ahead
    /// (<see cref="FrameExtentKind.Undeclared"/>).
    /// </summary>
    public long Length { get; }

    public static FrameExtent NeedMore(long required) => new(FrameExtentKind.NeedMore, required);

    public static FrameExtent Known(long length) => new(FrameExtentKind.Known, length);

    public static FrameExtent Undeclared(long readAhead) => new(FrameExtentKind.Undeclared, readAhead);
}

internal enum FrameExtentKind
{
    NeedMore,
    Known,
    Undeclared
}
