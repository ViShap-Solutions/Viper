using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace ViShap.Viper;

/// <summary>
/// Serializes objects to a compact binary form and reads them back.
/// </summary>
/// <remarks>
/// <para>
/// An instance is immutable once constructed and safe to share across threads; each call gets its own
/// resource accounting. Create one per configuration and reuse it — the type caches per-type metadata,
/// so a long-lived instance reuses what a fresh one per call would rebuild.
/// </para>
/// <para>
/// Every write builds the whole frame in the serializer's own pooled buffers before the first byte
/// reaches the destination, so a value that cannot be encoded leaves the destination untouched. Every
/// read takes its bytes from memory — a span, a sequence, or a stream or pipe read exactly as far as
/// the frame extends — so a source does not need to seek.
/// </para>
/// <para>
/// Version 1 frames are self-describing: the header records the format version, the algorithms used
/// and the frame's length, so a reader configured differently still knows how to unwrap the data and
/// where it ends. Version 0 is the compact alternative for a protocol that already frames its
/// messages: a bare payload with no header at all. Nothing in it identifies it, so a reader accepts
/// one only when configured with
/// <see cref="BinarySerializerOptionsBuilder.AllowV0Fallback(bool)"/>, and reads it synchronously
/// from bytes the protocol has already delimited.
/// </para>
/// <para>
/// Deserialization of untrusted input is bounded by <see cref="Security.SerializationLimits"/>. A
/// payload that exceeds a limit raises <see cref="Exceptions.BinaryLimitException"/> before the work
/// it asked for is performed. Limits apply to each frame, never to a connection.
/// </para>
/// <example>
/// <code>
/// var serializer = new BinarySerializer();
/// byte[] bytes = serializer.Serialize(new Customer { Name = "Ada", Age = 36 });
/// Customer? restored = serializer.Deserialize&lt;Customer&gt;(bytes);
/// </code>
/// </example>
/// </remarks>
public sealed class BinarySerializer
{
    private readonly BinarySerializerOptions _options;
    private readonly FormatRouter _router;

    /// <summary>Creates a serializer.</summary>
    /// <param name="options">
    /// Configuration built with <see cref="BinarySerializerOptions.Configure"/>, or
    /// <see langword="null"/> for <see cref="BinarySerializerOptions.Default"/>: format version 1,
    /// no compression, no checksum, no encryption.
    /// </param>
    /// <exception cref="Exceptions.BinaryConfigurationException">The configured limits are invalid.</exception>
    public BinarySerializer(BinarySerializerOptions? options = null)
    {
        _options = options ?? BinarySerializerOptions.Default;
        ArgumentNullException.ThrowIfNull(_options.Limits);
        _options.Limits.Validate();

        var pipelines = new Dictionary<int, IFormatPipeline>
        {
            [BinaryFormatHeaderV1.Version] = new V1FormatPipeline(
                _options.Compression,
                _options.Checksum,
                _options.Encryption,
                _options.KeyId,
                _options.Catalog)
        };

        if (_options.AllowV0Fallback || _options.WriteVersion == V0FormatPipeline.Version)
            pipelines[V0FormatPipeline.Version] = new V0FormatPipeline();

        _router = new FormatRouter(pipelines, _options.AllowV0Fallback);
    }

    // --- write ------------------------------------------------------------------------------------

    /// <summary>Writes <paramref name="value"/> to <paramref name="destination"/> and advances it.</summary>
    /// <typeparam name="T">
    /// The declared type. It decides the layout, so write and read must use the same one. A value
    /// whose runtime type differs needs a <see cref="BinaryUnionAttribute"/> declaration.
    /// </typeparam>
    /// <param name="destination">
    /// The writer to append to — an <see cref="ArrayBufferWriter{T}"/>, a
    /// <see cref="System.IO.Pipelines.PipeWriter"/>, or any other. The whole frame is encoded before
    /// the first byte is copied into it, so a value that cannot be encoded advances nothing. The
    /// writer is not flushed.
    /// </param>
    /// <param name="value">The value to write. May be <see langword="null"/> for reference types.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The type or the object graph cannot be encoded.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">
    /// The writer handed out an empty span, which a buffer writer must not do. Part of the frame may
    /// already be in it.
    /// </exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public void Serialize<T>(IBufferWriter<byte> destination, T value)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var frame = Encode(value);
        frame.WriteTo(destination);
    }

    /// <summary>Writes <paramref name="value"/> to a new byte array.</summary>
    /// <typeparam name="T">The declared type; see <see cref="Serialize{T}(IBufferWriter{byte}, T)"/>.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <returns>The encoded frame, in an array of exactly its length.</returns>
    /// <exception cref="Exceptions.BinaryTypeException">The type or the object graph cannot be encoded.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public byte[] Serialize<T>(T value)
    {
        using var frame = Encode(value);
        return frame.ToArray();
    }

    /// <summary>
    /// Writes <paramref name="value"/> into an array rented from the shared pool, which the returned
    /// payload owns until it is disposed.
    /// </summary>
    /// <typeparam name="T">The declared type; see <see cref="Serialize{T}(IBufferWriter{byte}, T)"/>.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <returns>The encoded frame. Dispose it once its bytes have been used, to return the array.</returns>
    /// <exception cref="Exceptions.BinaryTypeException">The type or the object graph cannot be encoded.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <example>
    /// <code>
    /// using PooledPayload payload = serializer.SerializePooled(order);
    /// await socket.SendAsync(payload.Memory, cancellationToken);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public PooledPayload SerializePooled<T>(T value)
    {
        using var frame = Encode(value);

        int length = checked((int)frame.Length);
        byte[] array = ArrayPool<byte>.Shared.Rent(length);
        frame.CopyTo(array);
        return new PooledPayload(array, length);
    }

    /// <summary>Writes <paramref name="value"/> to <paramref name="destination"/> and flushes it.</summary>
    /// <typeparam name="T">The declared type; see <see cref="Serialize{T}(IBufferWriter{byte}, T)"/>.</typeparam>
    /// <param name="destination">
    /// The stream to append to. It is left open, and its position is not reset; only the bytes this
    /// call produces count against <see cref="Security.SerializationLimits.MaxWireBytes"/>. It does not
    /// need to be seekable. The whole frame is encoded before the first byte is written, so a value
    /// that cannot be encoded leaves the stream as it was.
    /// </param>
    /// <param name="value">The value to write. May be <see langword="null"/> for reference types.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The type or the object graph cannot be encoded.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The destination stream failed.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public void Serialize<T>(Stream destination, T value)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var frame = Encode(value);
        frame.WriteTo(destination);
    }

    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="destination"/> and flushes it, awaiting the
    /// stream.
    /// </summary>
    /// <remarks>
    /// The frame is encoded synchronously in the serializer's own buffer; only the output is awaited.
    /// A cancellation observed before the output starts leaves the stream untouched. One observed
    /// while the bytes are being written may leave part of the frame in the stream, as with any
    /// cancelled write.
    /// </remarks>
    /// <typeparam name="T">The declared type; see <see cref="Serialize{T}(IBufferWriter{byte}, T)"/>.</typeparam>
    /// <param name="destination">The stream to append to; see <see cref="Serialize{T}(Stream, T)"/>.</param>
    /// <param name="value">The value to write. May be <see langword="null"/> for reference types.</param>
    /// <param name="cancellationToken">Cancels the wait for the stream.</param>
    /// <returns>A task that completes when the frame has been written and flushed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The type or the object graph cannot be encoded.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The destination stream failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public ValueTask SerializeAsync<T>(Stream destination, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return WriteAsync(destination, value, cancellationToken);
    }

    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="destination"/> and flushes it, awaiting the
    /// pipe.
    /// </summary>
    /// <remarks>
    /// The frame is encoded synchronously in the serializer's own buffer and copied into the pipe
    /// whole; only the flush is awaited. A cancellation observed before the copy leaves the pipe
    /// untouched. One observed during the flush leaves the frame written but not flushed.
    /// </remarks>
    /// <typeparam name="T">The declared type; see <see cref="Serialize{T}(IBufferWriter{byte}, T)"/>.</typeparam>
    /// <param name="destination">The pipe to write to. It is not completed.</param>
    /// <param name="value">The value to write. May be <see langword="null"/> for reference types.</param>
    /// <param name="cancellationToken">Cancels the wait for the pipe.</param>
    /// <returns>A task that completes when the frame has been written and flushed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The type or the object graph cannot be encoded.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">
    /// The pipe failed, or handed out an empty span to write into.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public ValueTask SerializeAsync<T>(PipeWriter destination, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return WriteAsync(destination, value, cancellationToken);
    }

    // --- read -------------------------------------------------------------------------------------

    /// <summary>Reads a value from <paramref name="source"/>, which must hold exactly one frame.</summary>
    /// <remarks>
    /// An array converts to a span, so <c>Deserialize&lt;T&gt;(bytes)</c> reads a byte array. A
    /// <see langword="null"/> array becomes an empty span and is rejected like any empty input.
    /// </remarks>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">One version 1 frame, or one version 0 payload, and nothing after it.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// <paramref name="source"/> is empty, malformed or truncated, or holds bytes after the payload.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The payload does not fit the requested type.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public T? Deserialize<T>(ReadOnlySpan<byte> source) =>
        ReadExactly<T>(source, target: default);

    /// <summary>
    /// Reads a value from the payload at the start of <paramref name="source"/> and reports where it
    /// ends, so payloads placed back to back can be read one after another.
    /// </summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">Bytes that start with a version 1 frame or a version 0 payload.</param>
    /// <param name="bytesConsumed">
    /// The number of bytes the frame occupies — for version 0, the bytes up to the end of the root
    /// value. Bytes after it are not read.
    /// </param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    /// <exception cref="Exceptions.BinaryFormatException"><paramref name="source"/> is empty, malformed or truncated.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The payload does not fit the requested type.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public T? Deserialize<T>(ReadOnlySpan<byte> source, out int bytesConsumed)
    {
        var value = ReadFirst<T>(source, target: default, out long consumed);
        bytesConsumed = (int)consumed;
        return (T?)value;
    }

    /// <summary>Reads a value from <paramref name="source"/>, which must hold exactly one frame.</summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">
    /// One version 1 frame, or one version 0 payload, and nothing after it, in one segment or many.
    /// </param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// <paramref name="source"/> is empty, malformed or truncated, or holds bytes after the payload.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The payload does not fit the requested type.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public T? Deserialize<T>(ReadOnlySequence<byte> source) =>
        ReadExactly<T>(source, target: default);

    /// <summary>
    /// Reads a value from the payload at the start of <paramref name="source"/> and reports where it
    /// ends — the position a <see cref="PipeReader"/> is advanced to.
    /// </summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">Bytes that start with a version 1 frame or a version 0 payload.</param>
    /// <param name="consumed">
    /// The position just after the frame — for version 0, just after the root value. Bytes after it
    /// are not read.
    /// </param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    /// <exception cref="Exceptions.BinaryFormatException"><paramref name="source"/> is empty, malformed or truncated.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The payload does not fit the requested type.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public T? Deserialize<T>(ReadOnlySequence<byte> source, out SequencePosition consumed)
    {
        var value = ReadFirst<T>(source, target: default, out long length);
        consumed = source.GetPosition(length);
        return (T?)value;
    }

    /// <summary>Reads one value from <paramref name="source"/>.</summary>
    /// <remarks>
    /// A version 1 frame is read exactly: the header gives its length, so the stream is asked for no
    /// byte past it and is left where the frame ends, whether it can seek or not. A version 0 payload
    /// carries no length; it is read ahead and the stream is moved back to where the root value ends,
    /// which needs a stream that can seek. After a failure the position is undefined.
    /// </remarks>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">A readable stream positioned at the start of a frame. It is left open.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// The stream holds a version 0 payload and cannot seek.
    /// </exception>
    /// <exception cref="Exceptions.BinaryFormatException">The stream is empty, or the data is malformed or truncated.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The payload does not fit the requested type.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The stream failed.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public T? Deserialize<T>(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var state = BeginOperation();
        return Found(FrameReader.Read<T>(_router, source, target: default, ref state));
    }

    /// <summary>Reads one version 1 frame from <paramref name="source"/>, awaiting its bytes.</summary>
    /// <remarks>
    /// <para>
    /// The header gives the frame's length, so the frame is awaited whole and then decoded
    /// synchronously; the stream is asked for no byte past it. The token is observed while bytes are
    /// awaited; a frame already in memory is decoded to the end, bounded by the limits. Bytes taken
    /// from the stream are not given back, so after a failure or a cancellation its position is
    /// undefined and it cannot be used to read further frames.
    /// </para>
    /// <para>
    /// Asynchronous reading accepts version 1 only; a version 0 payload raises
    /// <see cref="NotSupportedException"/>. V0 carries neither a magic number nor a length: it is a
    /// codec for protocols that already frame their messages — a length prefix, a message type, a
    /// channel. The protocol knows where a message ends, so the caller already holds one message's
    /// bytes and reads them synchronously. Waiting asynchronously is for a reader that does not know
    /// where the message ends; with V0 the protocol knows, not Viper.
    /// </para>
    /// <example>
    /// <code>
    /// // V1 from a socket: Viper knows the frame boundary — the header carries the length
    /// Order? order = await serializer.DeserializeAsync&lt;Order&gt;(networkStream, cancellationToken);
    ///
    /// // V0 inside your own protocol: the protocol knows the frame boundary
    /// var compact = new BinarySerializer(BinarySerializerOptions.Configure()
    ///     .WithVersion(0).AllowV0Fallback().Build());
    ///
    /// while (true)
    /// {
    ///     ReadResult read = await pipe.ReadAsync(cancellationToken);
    ///     ReadOnlySequence&lt;byte&gt; buffer = read.Buffer;
    ///
    ///     // the protocol: a 4-byte little-endian length, then the V0 payload
    ///     while (TryReadFrame(ref buffer, out ReadOnlySequence&lt;byte&gt; frame))
    ///     {
    ///         Order? message = compact.Deserialize&lt;Order&gt;(frame);   // synchronous: the frame is in memory
    ///         Handle(message);
    ///     }
    ///
    ///     pipe.AdvanceTo(buffer.Start, buffer.End);
    ///     if (read.IsCompleted) break;
    /// }
    ///
    /// static bool TryReadFrame(ref ReadOnlySequence&lt;byte&gt; buffer, out ReadOnlySequence&lt;byte&gt; frame)
    /// {
    ///     var reader = new SequenceReader&lt;byte&gt;(buffer);
    ///     if (!reader.TryReadLittleEndian(out int length) || reader.Remaining &lt; length)
    ///     {
    ///         frame = default;
    ///         return false;
    ///     }
    ///
    ///     frame = buffer.Slice(reader.Position, length);
    ///     buffer = buffer.Slice(frame.End);
    ///     return true;
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">A readable stream positioned at the start of a frame. It is left open.</param>
    /// <param name="cancellationToken">Cancels the wait for bytes.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="NotSupportedException">The stream holds a version 0 payload.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">The stream is empty, or the data is malformed or truncated.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The payload does not fit the requested type.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The stream failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReadAsync<T>(source, cancellationToken);
    }

    /// <summary>Reads one version 1 frame from <paramref name="source"/>, awaiting its bytes.</summary>
    /// <remarks>
    /// <para>
    /// The header gives the frame's length, so the frame is awaited whole and then decoded
    /// synchronously, and exactly the frame is consumed: bytes after it stay in the pipe. The token is
    /// observed while bytes are awaited; a frame already in memory is decoded to the end, bounded by
    /// the limits. On a failure or a cancellation nothing is consumed — the pipe is advanced to the
    /// start of the frame, with what was seen marked examined.
    /// </para>
    /// <para>
    /// Asynchronous reading accepts version 1 only; a version 0 payload raises
    /// <see cref="NotSupportedException"/>. V0 carries neither a magic number nor a length: it is a
    /// codec for protocols that already frame their messages — a length prefix, a message type, a
    /// channel. The protocol knows where a message ends, so the caller already holds one message's
    /// bytes and reads them synchronously. Waiting asynchronously is for a reader that does not know
    /// where the message ends; with V0 the protocol knows, not Viper.
    /// </para>
    /// <example>
    /// <code>
    /// // V1 from a socket: Viper knows the frame boundary — the header carries the length
    /// Order? order = await serializer.DeserializeAsync&lt;Order&gt;(pipeReader, cancellationToken);
    ///
    /// // V0 inside your own protocol: the protocol knows the frame boundary
    /// var compact = new BinarySerializer(BinarySerializerOptions.Configure()
    ///     .WithVersion(0).AllowV0Fallback().Build());
    ///
    /// while (true)
    /// {
    ///     ReadResult read = await pipe.ReadAsync(cancellationToken);
    ///     ReadOnlySequence&lt;byte&gt; buffer = read.Buffer;
    ///
    ///     // the protocol: a 4-byte little-endian length, then the V0 payload
    ///     while (TryReadFrame(ref buffer, out ReadOnlySequence&lt;byte&gt; frame))
    ///     {
    ///         Order? message = compact.Deserialize&lt;Order&gt;(frame);   // synchronous: the frame is in memory
    ///         Handle(message);
    ///     }
    ///
    ///     pipe.AdvanceTo(buffer.Start, buffer.End);
    ///     if (read.IsCompleted) break;
    /// }
    ///
    /// static bool TryReadFrame(ref ReadOnlySequence&lt;byte&gt; buffer, out ReadOnlySequence&lt;byte&gt; frame)
    /// {
    ///     var reader = new SequenceReader&lt;byte&gt;(buffer);
    ///     if (!reader.TryReadLittleEndian(out int length) || reader.Remaining &lt; length)
    ///     {
    ///         frame = default;
    ///         return false;
    ///     }
    ///
    ///     frame = buffer.Slice(reader.Position, length);
    ///     buffer = buffer.Slice(frame.End);
    ///     return true;
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">The pipe to read from. It is not completed.</param>
    /// <param name="cancellationToken">Cancels the wait for bytes.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="NotSupportedException">The pipe holds a version 0 payload.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">The pipe completed empty, or the data is malformed or truncated.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The payload does not fit the requested type.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The pipe failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public ValueTask<T?> DeserializeAsync<T>(PipeReader source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReadAsync<T>(source, cancellationToken);
    }

    /// <summary>
    /// Reads version 1 frames from <paramref name="source"/> one after another until it ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each frame is its own operation, with its own limits and budgets: a stream of frames is
    /// unbounded while every frame stays bounded. The stream ending exactly between two frames
    /// completes the enumeration; ending inside a frame raises
    /// <see cref="Exceptions.BinaryFormatException"/> once the complete frames before it have been
    /// yielded. Cancellation behaves as in <see cref="DeserializeAsync{T}(Stream, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// A version 0 payload raises <see cref="NotSupportedException"/>: it carries no length, so the
    /// protocol that carries it, not the serializer, knows where each one ends.
    /// </para>
    /// <example>
    /// <code>
    /// await foreach (Order? order in serializer.DeserializeAsyncEnumerable&lt;Order&gt;(networkStream, cancellationToken))
    ///     Handle(order);
    /// </code>
    /// </example>
    /// </remarks>
    /// <typeparam name="T">The declared type every frame was written with.</typeparam>
    /// <param name="source">A readable stream positioned at the start of a frame. It is left open.</param>
    /// <param name="cancellationToken">Cancels the wait for bytes.</param>
    /// <returns>The value of each frame, in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReadFramesAsync<T>(source, cancellationToken);
    }

    /// <summary>
    /// Reads version 1 frames from <paramref name="source"/> one after another until the pipe
    /// completes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each frame is its own operation, with its own limits and budgets: a stream of frames is
    /// unbounded while every frame stays bounded. Each frame is consumed from the pipe once it has
    /// been read. The pipe completing exactly between two frames completes the enumeration;
    /// completing inside a frame raises <see cref="Exceptions.BinaryFormatException"/> once the
    /// complete frames before it have been yielded. A cancellation leaves a frame that was started
    /// unconsumed in the pipe.
    /// </para>
    /// <para>
    /// A version 0 payload raises <see cref="NotSupportedException"/>: it carries no length, so the
    /// protocol that carries it, not the serializer, knows where each one ends.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The declared type every frame was written with.</typeparam>
    /// <param name="source">The pipe to read from. It is not completed.</param>
    /// <param name="cancellationToken">Cancels the wait for bytes.</param>
    /// <returns>The value of each frame, in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(PipeReader source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReadFramesAsync<T>(source, cancellationToken);
    }

    // --- populate an existing instance ------------------------------------------------------------

    /// <summary>
    /// Reads a payload into an object you already have instead of allocating a new one.
    /// <paramref name="source"/> must hold exactly one frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the root is populated: its members are overwritten with the values in the payload, and
    /// every object below it is created afresh. Under a keyed contract a member absent from the
    /// payload keeps its current value. An input that cannot be read leaves the target as far as the
    /// read got, and an empty one leaves it untouched.
    /// </para>
    /// <para>
    /// A struct is read with <c>value = serializer.Deserialize&lt;T&gt;(bytes)</c>, which behaves the
    /// same way.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">
    /// A member-encoded class. A type with a dedicated encoding — a collection, a dictionary, an
    /// array, a string — is rejected, because its payload is not a member layout.
    /// </typeparam>
    /// <param name="source">One version 1 frame, or one version 0 payload, and nothing after it.</param>
    /// <param name="target">The instance to populate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// <paramref name="source"/> is empty, malformed or truncated, or holds bytes after the payload;
    /// or its root is null or refers back to another object.
    /// </exception>
    /// <exception cref="Exceptions.BinaryTypeException">
    /// <typeparamref name="T"/> is not member-encoded, or the payload holds a different runtime type.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public void Populate<T>(ReadOnlySpan<byte> source, T target) where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ReadExactly(source, target);
    }

    /// <summary>
    /// Reads the payload at the start of <paramref name="source"/> into an object you already have,
    /// and reports where the payload ends.
    /// </summary>
    /// <remarks>See <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/> for what is populated.</remarks>
    /// <typeparam name="T">A member-encoded class; see <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/>.</typeparam>
    /// <param name="source">Bytes that start with a version 1 frame or a version 0 payload.</param>
    /// <param name="target">The instance to populate.</param>
    /// <param name="bytesConsumed">The number of bytes the frame occupies. Bytes after it are not read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// <paramref name="source"/> is empty, malformed or truncated, or its root is null or refers back
    /// to another object.
    /// </exception>
    /// <exception cref="Exceptions.BinaryTypeException">
    /// <typeparamref name="T"/> is not member-encoded, or the payload holds a different runtime type.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public void Populate<T>(ReadOnlySpan<byte> source, T target, out int bytesConsumed) where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ReadFirst(source, target, out long consumed);
        bytesConsumed = (int)consumed;
    }

    /// <summary>
    /// Reads a payload into an object you already have. <paramref name="source"/> must hold exactly
    /// one frame.
    /// </summary>
    /// <remarks>See <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/> for what is populated.</remarks>
    /// <typeparam name="T">A member-encoded class; see <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/>.</typeparam>
    /// <param name="source">
    /// One version 1 frame, or one version 0 payload, and nothing after it, in one segment or many.
    /// </param>
    /// <param name="target">The instance to populate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// <paramref name="source"/> is empty, malformed or truncated, or holds bytes after the payload;
    /// or its root is null or refers back to another object.
    /// </exception>
    /// <exception cref="Exceptions.BinaryTypeException">
    /// <typeparamref name="T"/> is not member-encoded, or the payload holds a different runtime type.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public void Populate<T>(ReadOnlySequence<byte> source, T target) where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ReadExactly(source, target);
    }

    /// <summary>
    /// Reads the payload at the start of <paramref name="source"/> into an object you already have,
    /// and reports where the payload ends.
    /// </summary>
    /// <remarks>See <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/> for what is populated.</remarks>
    /// <typeparam name="T">A member-encoded class; see <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/>.</typeparam>
    /// <param name="source">Bytes that start with a version 1 frame or a version 0 payload.</param>
    /// <param name="target">The instance to populate.</param>
    /// <param name="consumed">The position just after the frame. Bytes after it are not read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// <paramref name="source"/> is empty, malformed or truncated, or its root is null or refers back
    /// to another object.
    /// </exception>
    /// <exception cref="Exceptions.BinaryTypeException">
    /// <typeparamref name="T"/> is not member-encoded, or the payload holds a different runtime type.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public void Populate<T>(ReadOnlySequence<byte> source, T target, out SequencePosition consumed) where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ReadFirst(source, target, out long length);
        consumed = source.GetPosition(length);
    }

    /// <summary>Reads one payload from <paramref name="source"/> into an object you already have.</summary>
    /// <remarks>
    /// See <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/> for what is populated, and
    /// <see cref="Deserialize{T}(Stream)"/> for how far the stream is read.
    /// </remarks>
    /// <typeparam name="T">A member-encoded class; see <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/>.</typeparam>
    /// <param name="source">A readable stream positioned at the start of a frame. It is left open.</param>
    /// <param name="target">The instance to populate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="target"/> is null.</exception>
    /// <exception cref="NotSupportedException">The stream holds a version 0 payload and cannot seek.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// The stream is empty, the data is malformed or truncated, or its root is null or refers back to
    /// another object.
    /// </exception>
    /// <exception cref="Exceptions.BinaryTypeException">
    /// <typeparamref name="T"/> is not member-encoded, or the payload holds a different runtime type.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The stream failed.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public void Populate<T>(Stream source, T target) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var state = BeginOperation();
        Found(FrameReader.Read(_router, source, target, ref state));
    }

    /// <summary>
    /// Reads one version 1 frame from <paramref name="source"/> into an object you already have,
    /// awaiting its bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/> for what is populated. The frame is awaited
    /// whole and decoded synchronously; the token is observed while bytes are awaited. Bytes taken
    /// from the stream are not given back, so after a failure or a cancellation its position is
    /// undefined.
    /// </para>
    /// <para>
    /// Asynchronous reading accepts version 1 only; a version 0 payload raises
    /// <see cref="NotSupportedException"/>. V0 carries neither a magic number nor a length: it is a
    /// codec for protocols that already frame their messages — a length prefix, a message type, a
    /// channel. The protocol knows where a message ends, so the caller already holds one message's
    /// bytes and reads them synchronously. Waiting asynchronously is for a reader that does not know
    /// where the message ends; with V0 the protocol knows, not Viper.
    /// </para>
    /// <example>
    /// <code>
    /// // V1 from a socket: Viper knows the frame boundary — the header carries the length
    /// await serializer.PopulateAsync(networkStream, order, cancellationToken);
    ///
    /// // V0 inside your own protocol: the protocol knows the frame boundary
    /// var compact = new BinarySerializer(BinarySerializerOptions.Configure()
    ///     .WithVersion(0).AllowV0Fallback().Build());
    ///
    /// while (true)
    /// {
    ///     ReadResult read = await pipe.ReadAsync(cancellationToken);
    ///     ReadOnlySequence&lt;byte&gt; buffer = read.Buffer;
    ///
    ///     // the protocol: a 4-byte little-endian length, then the V0 payload
    ///     while (TryReadFrame(ref buffer, out ReadOnlySequence&lt;byte&gt; frame))
    ///     {
    ///         compact.Populate(frame, order);   // synchronous: the frame is in memory
    ///         Handle(order);
    ///     }
    ///
    ///     pipe.AdvanceTo(buffer.Start, buffer.End);
    ///     if (read.IsCompleted) break;
    /// }
    ///
    /// static bool TryReadFrame(ref ReadOnlySequence&lt;byte&gt; buffer, out ReadOnlySequence&lt;byte&gt; frame)
    /// {
    ///     var reader = new SequenceReader&lt;byte&gt;(buffer);
    ///     if (!reader.TryReadLittleEndian(out int length) || reader.Remaining &lt; length)
    ///     {
    ///         frame = default;
    ///         return false;
    ///     }
    ///
    ///     frame = buffer.Slice(reader.Position, length);
    ///     buffer = buffer.Slice(frame.End);
    ///     return true;
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    /// <typeparam name="T">A member-encoded class; see <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/>.</typeparam>
    /// <param name="source">A readable stream positioned at the start of a frame. It is left open.</param>
    /// <param name="target">The instance to populate.</param>
    /// <param name="cancellationToken">Cancels the wait for bytes.</param>
    /// <returns>A task that completes when the target has been populated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="target"/> is null.</exception>
    /// <exception cref="NotSupportedException">The stream holds a version 0 payload.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// The stream is empty, the data is malformed or truncated, or its root is null or refers back to
    /// another object.
    /// </exception>
    /// <exception cref="Exceptions.BinaryTypeException">
    /// <typeparamref name="T"/> is not member-encoded, or the payload holds a different runtime type.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The stream failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public ValueTask PopulateAsync<T>(Stream source, T target, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return PopulateFrameAsync(source, target, cancellationToken);
    }

    /// <summary>
    /// Reads one version 1 frame from <paramref name="source"/> into an object you already have,
    /// awaiting its bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/> for what is populated. The frame is awaited
    /// whole and decoded synchronously, and exactly the frame is consumed. On a failure or a
    /// cancellation nothing is consumed — the pipe is advanced to the start of the frame, with what
    /// was seen marked examined.
    /// </para>
    /// <para>
    /// Asynchronous reading accepts version 1 only; a version 0 payload raises
    /// <see cref="NotSupportedException"/>. V0 carries neither a magic number nor a length: it is a
    /// codec for protocols that already frame their messages — a length prefix, a message type, a
    /// channel. The protocol knows where a message ends, so the caller already holds one message's
    /// bytes and reads them synchronously. Waiting asynchronously is for a reader that does not know
    /// where the message ends; with V0 the protocol knows, not Viper.
    /// </para>
    /// <example>
    /// <code>
    /// // V1 from a socket: Viper knows the frame boundary — the header carries the length
    /// await serializer.PopulateAsync(pipeReader, order, cancellationToken);
    ///
    /// // V0 inside your own protocol: the protocol knows the frame boundary
    /// var compact = new BinarySerializer(BinarySerializerOptions.Configure()
    ///     .WithVersion(0).AllowV0Fallback().Build());
    ///
    /// while (true)
    /// {
    ///     ReadResult read = await pipe.ReadAsync(cancellationToken);
    ///     ReadOnlySequence&lt;byte&gt; buffer = read.Buffer;
    ///
    ///     // the protocol: a 4-byte little-endian length, then the V0 payload
    ///     while (TryReadFrame(ref buffer, out ReadOnlySequence&lt;byte&gt; frame))
    ///     {
    ///         compact.Populate(frame, order);   // synchronous: the frame is in memory
    ///         Handle(order);
    ///     }
    ///
    ///     pipe.AdvanceTo(buffer.Start, buffer.End);
    ///     if (read.IsCompleted) break;
    /// }
    ///
    /// static bool TryReadFrame(ref ReadOnlySequence&lt;byte&gt; buffer, out ReadOnlySequence&lt;byte&gt; frame)
    /// {
    ///     var reader = new SequenceReader&lt;byte&gt;(buffer);
    ///     if (!reader.TryReadLittleEndian(out int length) || reader.Remaining &lt; length)
    ///     {
    ///         frame = default;
    ///         return false;
    ///     }
    ///
    ///     frame = buffer.Slice(reader.Position, length);
    ///     buffer = buffer.Slice(frame.End);
    ///     return true;
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    /// <typeparam name="T">A member-encoded class; see <see cref="Populate{T}(ReadOnlySpan{byte}, T)"/>.</typeparam>
    /// <param name="source">The pipe to read from. It is not completed.</param>
    /// <param name="target">The instance to populate.</param>
    /// <param name="cancellationToken">Cancels the wait for bytes.</param>
    /// <returns>A task that completes when the target has been populated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="target"/> is null.</exception>
    /// <exception cref="NotSupportedException">The pipe holds a version 0 payload.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// The pipe completed empty, the data is malformed or truncated, or its root is null or refers
    /// back to another object.
    /// </exception>
    /// <exception cref="Exceptions.BinaryTypeException">
    /// <typeparamref name="T"/> is not member-encoded, or the payload holds a different runtime type.
    /// </exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryFormatNotSupportedException">The format version or an algorithm is not supported.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed, or a required protection is absent.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The pipe failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public ValueTask PopulateAsync<T>(PipeReader source, T target, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return PopulateFrameAsync(source, target, cancellationToken);
    }

    // --- the operation behind every entry point ---------------------------------------------------

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private EncodedFrame Encode<T>(T value) =>
        Encode(value, BeginOperation());

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private EncodedFrame Encode<T>(T value, OperationState state) =>
        _router.ForWriting(_options.WriteVersion).Write(value, ref state);

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask WriteAsync<T>(Stream destination, T value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var frame = Encode(value);
        cancellationToken.ThrowIfCancellationRequested();
        await frame.WriteToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask WriteAsync<T>(PipeWriter destination, T value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var frame = Encode(value);
        cancellationToken.ThrowIfCancellationRequested();
        await frame.WriteToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the frame at the start of <paramref name="source"/>, which must be the whole of it.</summary>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private T? ReadExactly<T>(ReadOnlySpan<byte> source, T? target)
    {
        var value = ReadFirst(source, target, out long consumed);
        RequireEnd(source.Length, consumed);
        return value;
    }

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private T? ReadExactly<T>(ReadOnlySequence<byte> source, T? target)
    {
        var value = ReadFirst(source, target, out long consumed);
        RequireEnd(source.Length, consumed);
        return value;
    }

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private T? ReadFirst<T>(ReadOnlySpan<byte> source, T? target, out long consumed)
    {
        if (source.IsEmpty)
            throw EmptyPayload();

        var state = BeginOperation();
        return _router.ForReading(source).Read(source, target, ref state, out consumed);
    }

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private T? ReadFirst<T>(ReadOnlySequence<byte> source, T? target, out long consumed)
    {
        if (source.IsEmpty)
            throw EmptyPayload();

        Span<byte> prefix = stackalloc byte[FormatRouter.LongestPrefix];
        var identified = source.FirstSpan.Length >= prefix.Length
            ? source.FirstSpan
            : prefix[..CopyPrefix(source, prefix)];

        var state = BeginOperation();
        return _router.ForReading(identified).Read(source, target, ref state, out consumed);

        static int CopyPrefix(ReadOnlySequence<byte> source, Span<byte> prefix)
        {
            int length = (int)Math.Min(source.Length, prefix.Length);
            source.Slice(0, length).CopyTo(prefix);
            return length;
        }
    }

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<T?> ReadAsync<T>(Stream source, CancellationToken cancellationToken) =>
        Found(await FrameReader.ReadAsync<T>(
            _router, source, target: default, BeginOperation(), cancellationToken).ConfigureAwait(false));

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<T?> ReadAsync<T>(PipeReader source, CancellationToken cancellationToken) =>
        Found(await FrameReader.ReadAsync<T>(
            _router, source, target: default, BeginOperation(), cancellationToken).ConfigureAwait(false));

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PopulateFrameAsync<T>(Stream source, T target, CancellationToken cancellationToken) =>
        Found(await FrameReader.ReadAsync(
            _router, source, target, BeginOperation(), cancellationToken).ConfigureAwait(false));

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PopulateFrameAsync<T>(PipeReader source, T target, CancellationToken cancellationToken) =>
        Found(await FrameReader.ReadAsync(
            _router, source, target, BeginOperation(), cancellationToken).ConfigureAwait(false));

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private async IAsyncEnumerable<T?> ReadFramesAsync<T>(
        Stream source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await FrameReader.ReadAsync<T>(
                _router, source, target: default, BeginOperation(), cancellationToken).ConfigureAwait(false);

            if (!frame.Found)
                yield break;

            yield return frame.Value;
        }
    }

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private async IAsyncEnumerable<T?> ReadFramesAsync<T>(
        PipeReader source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await FrameReader.ReadAsync<T>(
                _router, source, target: default, BeginOperation(), cancellationToken).ConfigureAwait(false);

            if (!frame.Found)
                yield break;

            yield return frame.Value;
        }
    }

    /// <summary>The value of a frame a source delivered; a source that delivered no byte held no payload.</summary>
    private static T? Found<T>(FrameReader.Frame<T> frame) =>
        frame.Found ? frame.Value : throw EmptyPayload();

    private static void RequireEnd(long length, long consumed)
    {
        if (consumed != length)
            throw new BinaryFormatException(
                $"The source holds {length - consumed} byte(s) after the end of the payload. Read " +
                "payloads placed back to back with an overload that reports the bytes consumed.");
    }

    private static BinaryFormatException EmptyPayload() =>
        new("The payload is empty. No wire format encodes a value in zero bytes.");

    private OperationState BeginOperation() =>
        new(_options.Limits,
            _options.Keys,
            _options.PreserveReferences,
            _options.RequireEncryption,
            _options.RequireChecksum);
}
