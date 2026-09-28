using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Pipeline;

/// <summary>
/// One finished frame, held in the serializer's own pooled buffers until it is copied to its
/// destination: an optional header followed by a body, which is either the payload as the engine
/// wrote it, the output of the last phase, or — for an encrypted frame — a <see cref="SealedBody"/>
/// that is encrypted straight into the destination. A pipeline builds the whole frame before anything
/// reaches a destination, so a failure while encoding leaves every destination untouched, and the
/// frame is checked against the wire budget before it leaves.
/// <para>
/// The frame owns its buffers; disposing it clears them and returns them to their pools.
/// </para>
/// </summary>
internal readonly struct EncodedFrame : IDisposable
{
    private readonly PayloadBuffer? _header;
    private readonly PayloadBuffer? _payload;
    private readonly RentedBytes _body;
    private readonly SealedBody _sealed;

    private EncodedFrame(PayloadBuffer? header, PayloadBuffer? payload, RentedBytes body, SealedBody sealedBody)
    {
        _header = header;
        _payload = payload;
        _body = body;
        _sealed = sealedBody;
    }

    /// <summary>
    /// A frame of <paramref name="header"/> and <paramref name="payload"/>, or of
    /// <paramref name="payload"/> alone when there is no header. The frame owns both once it is
    /// returned; when this throws, they still belong to the caller.
    /// </summary>
    /// <exception cref="BinaryLimitException">The frame is longer than <paramref name="maxWireBytes"/>.</exception>
    public static EncodedFrame Of(PayloadBuffer? header, PayloadBuffer payload, long maxWireBytes) =>
        Checked(new EncodedFrame(header, payload, default, default), maxWireBytes);

    /// <summary>
    /// A frame of <paramref name="header"/> and a phase output. The frame owns both once it is
    /// returned; when this throws, they still belong to the caller.
    /// </summary>
    /// <exception cref="BinaryLimitException">The frame is longer than <paramref name="maxWireBytes"/>.</exception>
    public static EncodedFrame Of(PayloadBuffer header, RentedBytes body, long maxWireBytes) =>
        Checked(new EncodedFrame(header, payload: null, body, default), maxWireBytes);

    /// <summary>
    /// A frame of <paramref name="header"/> and an encrypted body, whose ciphertext is produced where
    /// the frame is written. The frame owns both once it is returned; when this throws, they still
    /// belong to the caller.
    /// </summary>
    /// <exception cref="BinaryLimitException">The frame is longer than <paramref name="maxWireBytes"/>.</exception>
    public static EncodedFrame Of(PayloadBuffer header, SealedBody body, long maxWireBytes) =>
        Checked(new EncodedFrame(header, payload: null, default, body), maxWireBytes);

    /// <summary>The number of bytes in the frame.</summary>
    public long Length => (_header?.Length ?? 0) + (_payload?.Length ?? 0) + _body.Length + _sealed.Length;

    /// <summary>Copies the frame into a new array of exactly its length.</summary>
    public byte[] ToArray()
    {
        var result = GC.AllocateUninitializedArray<byte>(checked((int)Length));
        CopyTo(result);
        return result;
    }

    /// <summary>Copies the frame into the first <see cref="Length"/> bytes of <paramref name="destination"/>.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (_header is not null)
        {
            _header.CopyTo(destination);
            destination = destination[(int)_header.Length..];
        }

        if (_payload is not null)
        {
            _payload.CopyTo(destination);
            destination = destination[(int)_payload.Length..];
        }

        _body.Span.CopyTo(destination);

        if (_sealed.Exists)
            _sealed.SealInto(destination[_body.Length..]);
    }

    /// <summary>Writes the frame to <paramref name="destination"/> and flushes it.</summary>
    /// <exception cref="BinaryStreamException">The destination failed; the cause is preserved.</exception>
    public void WriteTo(Stream destination)
    {
        if (_sealed.Exists)
        {
            using var whole = Whole();
            Write(destination, whole.Span);
            return;
        }

        try
        {
            _header?.WriteTo(destination);
            _payload?.WriteTo(destination);
            destination.Write(_body.Span);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to write wire data to the underlying stream.", ex);
        }

        try
        {
            destination.Flush();
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to flush wire data to the underlying stream.", ex);
        }
    }

    /// <summary>
    /// Writes the frame to <paramref name="destination"/> and flushes it, awaiting the stream. A
    /// cancellation observed while the bytes are being written may leave part of the frame in it.
    /// </summary>
    /// <exception cref="BinaryStreamException">The destination failed; the cause is preserved.</exception>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask WriteToAsync(Stream destination, CancellationToken cancellationToken)
    {
        if (_sealed.Exists)
        {
            using var whole = Whole();
            await WriteAsync(destination, whole.Memory, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            if (_header is not null)
                await _header.WriteToAsync(destination, cancellationToken).ConfigureAwait(false);

            if (_payload is not null)
                await _payload.WriteToAsync(destination, cancellationToken).ConfigureAwait(false);

            await destination.WriteAsync(_body.Memory, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to write wire data to the underlying stream.", ex);
        }

        try
        {
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to flush wire data to the underlying stream.", ex);
        }
    }

    /// <summary>
    /// Copies the frame into <paramref name="destination"/> and flushes it, awaiting the pipe. A
    /// cancellation observed during the flush leaves the frame written but not flushed.
    /// </summary>
    /// <exception cref="BinaryStreamException">The pipe failed; the cause is preserved.</exception>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask WriteToAsync(PipeWriter destination, CancellationToken cancellationToken)
    {
        WriteTo(destination);

        try
        {
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to flush wire data to the underlying pipe.", ex);
        }
    }

    /// <summary>
    /// Copies the frame into <paramref name="destination"/> and advances it. An encrypted frame is
    /// encrypted straight into the span the writer hands out, and the writer is advanced only once the
    /// whole frame is in it; a writer that hands out less than the whole frame receives it through a
    /// pooled buffer instead.
    /// </summary>
    /// <exception cref="BinaryStreamException">The writer handed out an empty span.</exception>
    public void WriteTo(IBufferWriter<byte> destination)
    {
        if (_sealed.Exists)
        {
            int length = checked((int)Length);
            var span = destination.GetSpan(length);
            if (span.Length >= length)
            {
                CopyTo(span);
                destination.Advance(length);
                return;
            }

            using var whole = Whole();
            PayloadBuffer.CopyInto(destination, whole.Span);
            return;
        }

        _header?.WriteTo(destination);
        _payload?.WriteTo(destination);
        PayloadBuffer.CopyInto(destination, _body.Span);
    }

    /// <summary>Clears every buffer of the frame and returns it to its pool.</summary>
    public void Dispose()
    {
        _header?.Dispose();
        _payload?.Dispose();
        _body.Dispose();
        _sealed.Dispose();
    }

    /// <summary>The whole frame in one pooled buffer, for a destination that is written from memory.</summary>
    private RentedBytes Whole()
    {
        int length = checked((int)Length);
        byte[] array = RentedBytes.RentArray(length);
        try
        {
            CopyTo(array);
            return RentedBytes.Adopt(array, length);
        }
        catch
        {
            RentedBytes.ReturnArray(array);
            throw;
        }
    }

    /// <exception cref="BinaryStreamException">The destination failed; the cause is preserved.</exception>
    private static void Write(Stream destination, ReadOnlySpan<byte> frame)
    {
        try
        {
            destination.Write(frame);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to write wire data to the underlying stream.", ex);
        }

        try
        {
            destination.Flush();
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to flush wire data to the underlying stream.", ex);
        }
    }

    /// <exception cref="BinaryStreamException">The destination failed; the cause is preserved.</exception>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private static async ValueTask WriteAsync(
        Stream destination,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        try
        {
            await destination.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to write wire data to the underlying stream.", ex);
        }

        try
        {
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to flush wire data to the underlying stream.", ex);
        }
    }

    private static EncodedFrame Checked(EncodedFrame frame, long maxWireBytes) =>
        frame.Length <= maxWireBytes
            ? frame
            : throw new BinaryLimitException($"The wire byte budget of {maxWireBytes} would be exceeded.");
}
