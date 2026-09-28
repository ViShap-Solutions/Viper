using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Pipeline;

/// <summary>
/// Reads one frame from a source that delivers bytes over time — a stream or a pipe — by asking it
/// for exactly the bytes the frame needs and no more: the identifying prefix, then as much of the
/// header as the bytes so far say it has, then the rest of the frame. A source is therefore never
/// read past the frame, whether it can seek or not, and the frame is decoded from memory once it is
/// there. Waiting happens only here, at the edge; decoding a frame already in memory is synchronous
/// and is bounded by the limits rather than interrupted.
/// </summary>
internal static class FrameReader
{
    /// <summary>The most bytes any header occupies, and so the most a frame's extent needs to be known.</summary>
    private const int LongestHeader = BinaryFormatHeaderV1.MaxLength;

    /// <summary>
    /// Why an asynchronous read refuses a V0 payload: the frame carries neither a magic number nor a
    /// length, so only the caller's protocol knows where it ends.
    /// </summary>
    public const string AsynchronousV0 =
        "Asynchronous reading accepts version 1 frames only. A version 0 payload carries neither a " +
        "magic number nor a length, so the protocol that carries it knows where it ends, not the " +
        "serializer: read the payload's bytes with your protocol and pass them to a synchronous " +
        "Deserialize overload.";

    /// <summary>Why a V0 payload cannot be read from a stream that cannot seek.</summary>
    public const string NonSeekableV0 =
        "A version 0 payload carries no length, so reading one from a stream means reading ahead " +
        "and moving back to where the payload ends, which needs a stream that can seek. Read the " +
        "payload's bytes with the protocol that carries it and pass them to a Deserialize overload " +
        "that takes a span or a sequence.";

    /// <summary>Reads one frame from <paramref name="source"/>, leaving it where the frame ends.</summary>
    /// <returns>The frame's value, or <see cref="Frame{T}.None"/> when the source held no byte at all.</returns>
    public static Frame<T> Read<T>(
        FormatRouter router,
        Stream source,
        T? target,
        ref OperationState state)
    {
        bool seekable = source.CanSeek;
        long start = seekable ? StreamSource.Position(source) : 0;
        long available = seekable ? StreamSource.Remaining(source) : long.MaxValue;

        var scan = new FrameScan(router);
        var buffer = new FrameBuffer();
        try
        {
            var extent = FrameExtent.NeedMore(FormatRouter.PrefixLength);
            do
            {
                int wanted = (int)extent.Length;
                bool backed = seekable && wanted <= available;
                while (buffer.Length < wanted)
                {
                    int read = StreamSource.Read(source, buffer.Free(wanted, backed).Span);
                    if (read == 0)
                        break;

                    buffer.Advance(read);
                }

                if (buffer.Length == 0)
                    return Frame<T>.None;

                extent = scan.Next(buffer.Span, sourceEnded: buffer.Length < wanted, available, ref state);
            }
            while (extent.Kind == FrameExtentKind.NeedMore);

            if (extent.Kind == FrameExtentKind.Undeclared)
            {
                if (!seekable)
                    throw new NotSupportedException(NonSeekableV0);

                int readAhead = Buffered(Math.Min(extent.Length, available));
                while (buffer.Length < readAhead)
                {
                    int read = StreamSource.Read(source, buffer.Free(readAhead, backed: true).Span);
                    if (read == 0)
                        break;

                    buffer.Advance(read);
                }

                var root = scan.Pipeline.Read(buffer.Span, target, ref state, out long consumed);

                StreamSource.Seek(source, start + consumed);
                return new Frame<T>(root);
            }

            int length = Buffered(extent.Length);
            bool frameBacked = seekable && length <= available;
            while (buffer.Length < length)
            {
                int read = StreamSource.Read(source, buffer.Free(length, frameBacked).Span);
                if (read == 0)
                    break;

                buffer.Advance(read);
            }

            return new Frame<T>(scan.Pipeline.Read(buffer.Span, target, ref state, out _));
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Reads one version 1 frame from <paramref name="source"/>, awaiting its bytes. The bytes taken
    /// are not given back: after a failure or a cancellation the stream's position is undefined.
    /// </summary>
    /// <returns>The frame's value, or <see cref="Frame{T}.None"/> when the source ended before a byte arrived.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<Frame<T>> ReadAsync<T>(
        FormatRouter router,
        Stream source,
        T? target,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var scan = new FrameScan(router);
        var buffer = new FrameBuffer();
        try
        {
            var extent = FrameExtent.NeedMore(FormatRouter.PrefixLength);
            while (true)
            {
                int wanted = extent.Kind == FrameExtentKind.Known ? Buffered(extent.Length) : (int)extent.Length;
                while (buffer.Length < wanted)
                {
                    int read = await StreamSource.ReadAsync(
                        source, buffer.Free(wanted, backed: false), cancellationToken).ConfigureAwait(false);

                    if (read == 0)
                        break;

                    buffer.Advance(read);
                }

                if (buffer.Length == 0)
                    return Frame<T>.None;

                if (extent.Kind == FrameExtentKind.Known)
                    return new Frame<T>(scan.Pipeline.Read(buffer.Span, target, ref state, out _));

                extent = scan.Next(buffer.Span, sourceEnded: buffer.Length < wanted, available: long.MaxValue, ref state);
                if (extent.Kind == FrameExtentKind.Undeclared)
                    throw new NotSupportedException(AsynchronousV0);
            }
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Reads one version 1 frame from <paramref name="source"/>, awaiting its bytes, and consumes
    /// exactly the frame. On a failure or a cancellation nothing is consumed: the pipe is advanced to
    /// where the frame starts, with everything seen marked examined.
    /// </summary>
    /// <returns>The frame's value, or <see cref="Frame{T}.None"/> when the pipe completed with nothing left in it.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<Frame<T>> ReadAsync<T>(
        FormatRouter router,
        PipeReader source,
        T? target,
        OperationState state,
        CancellationToken cancellationToken)
    {
        var scan = new FrameScan(router);
        while (true)
        {
            ReadResult result = await StreamSource.ReadAsync(source, cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffered = result.Buffer;
            bool finished = false;
            try
            {
                if (result.IsCanceled)
                    throw new OperationCanceledException("A pending read of the pipe was canceled.");

                if (buffered.IsEmpty && result.IsCompleted)
                {
                    finished = true;
                    source.AdvanceTo(buffered.End);
                    return Frame<T>.None;
                }

                var extent = Measure(ref scan, buffered, result.IsCompleted, ref state);
                if (extent.Kind == FrameExtentKind.Undeclared)
                    throw new NotSupportedException(AsynchronousV0);

                if (extent.Kind == FrameExtentKind.Known && (buffered.Length >= extent.Length || result.IsCompleted))
                {
                    var frame = buffered.Slice(0, Math.Min(buffered.Length, extent.Length));
                    var value = scan.Pipeline.Read(frame, target, ref state, out long length);

                    finished = true;
                    source.AdvanceTo(buffered.GetPosition(length));
                    return new Frame<T>(value);
                }
            }
            finally
            {
                if (!finished)
                    source.AdvanceTo(buffered.Start, buffered.End);
            }
        }
    }

    /// <summary>Measures the frame at the start of a pipe's bytes from as much of them as a header can occupy.</summary>
    private static FrameExtent Measure(
        ref FrameScan scan,
        ReadOnlySequence<byte> buffered,
        bool completed,
        ref OperationState state)
    {
        int take = (int)Math.Min(buffered.Length, LongestHeader);
        bool whole = take == buffered.Length;
        long available = completed ? buffered.Length : long.MaxValue;

        if (buffered.FirstSpan.Length >= take)
            return scan.Next(buffered.FirstSpan[..take], completed && whole, available, ref state);

        Span<byte> prefix = stackalloc byte[LongestHeader];
        buffered.Slice(0, take).CopyTo(prefix);
        return scan.Next(prefix[..take], completed && whole, available, ref state);
    }

    /// <summary>A frame length a buffer can hold; a longer one exceeds any budget a buffer can honour.</summary>
    private static int Buffered(long length) =>
        length <= Array.MaxLength
            ? (int)length
            : throw new BinaryLimitException(
                $"A frame of {length} bytes is longer than the {Array.MaxLength} bytes a buffer can hold.");

    /// <summary>The outcome of reading one frame: its value, or nothing because the source held no byte.</summary>
    public readonly struct Frame<T>(T? value)
    {
        /// <summary>No frame: the source ended before its first byte.</summary>
        public static Frame<T> None => default;

        public bool Found { get; } = true;

        public T? Value { get; } = value;
    }

    /// <summary>
    /// The frame's identity and extent, established from its first bytes: the pipeline its prefix
    /// selects, and then what that pipeline says about how far the frame extends.
    /// </summary>
    private struct FrameScan(FormatRouter router)
    {
        private IFormatPipeline? _pipeline;

        public readonly IFormatPipeline Pipeline => _pipeline!;

        public FrameExtent Next(ReadOnlySpan<byte> buffered, bool sourceEnded, long available, ref OperationState state)
        {
            if (_pipeline is null)
            {
                int needs = FormatRouter.Needs(buffered);
                if (buffered.Length < needs && !sourceEnded)
                    return FrameExtent.NeedMore(needs);

                _pipeline = router.ForReading(buffered);
            }

            return _pipeline.Measure(buffered, sourceEnded, available, ref state);
        }
    }
}
