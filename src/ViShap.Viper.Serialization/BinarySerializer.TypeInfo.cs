using System.Buffers;
using System.IO.Pipelines;

namespace ViShap.Viper;

// The entry points that take the type information of a context: the same operations as their
// counterparts without it, with the declared type's contracts and encodings coming from the context,
// so none of them carries a trimming or native AOT requirement.
public sealed partial class BinarySerializer
{
    // --- write ------------------------------------------------------------------------------------

    /// <inheritdoc cref="Serialize{T}(IBufferWriter{byte}, T)"/>
    /// <param name="destination">The writer to append to; see <see cref="Serialize{T}(IBufferWriter{byte}, T)"/>.</param>
    /// <param name="value">The value to write. May be <see langword="null"/> for reference types.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the value
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public void Serialize<T>(IBufferWriter<byte> destination, T value, BinaryTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Require(typeInfo);

        using var frame = Encode(value);
        frame.WriteTo(destination);
    }

    /// <inheritdoc cref="Serialize{T}(T)"/>
    /// <param name="value">The value to write.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the value
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public byte[] Serialize<T>(T value, BinaryTypeInfo<T> typeInfo)
    {
        Require(typeInfo);

        using var frame = Encode(value);
        return frame.ToArray();
    }

    /// <inheritdoc cref="SerializePooled{T}(T)"/>
    /// <param name="value">The value to write.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the value
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public PooledPayload SerializePooled<T>(T value, BinaryTypeInfo<T> typeInfo)
    {
        Require(typeInfo);

        using var frame = Encode(value);

        int length = checked((int)frame.Length);
        byte[] array = ArrayPool<byte>.Shared.Rent(length);
        frame.CopyTo(array);
        return new PooledPayload(array, length);
    }

    /// <inheritdoc cref="Serialize{T}(Stream, T)"/>
    /// <param name="destination">The stream to write to; see <see cref="Serialize{T}(Stream, T)"/>.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the value
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public void Serialize<T>(Stream destination, T value, BinaryTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Require(typeInfo);

        using var frame = Encode(value);
        frame.WriteTo(destination);
    }

    /// <inheritdoc cref="SerializeAsync{T}(Stream, T, CancellationToken)"/>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="cancellationToken">Cancels the write to the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the value
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public ValueTask SerializeAsync<T>(
        Stream destination,
        T value,
        BinaryTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Require(typeInfo);
        return WriteAsync(destination, value, cancellationToken);
    }

    /// <inheritdoc cref="SerializeAsync{T}(PipeWriter, T, CancellationToken)"/>
    /// <param name="destination">The pipe to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="cancellationToken">Cancels the flush of the pipe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the value
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public ValueTask SerializeAsync<T>(
        PipeWriter destination,
        T value,
        BinaryTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Require(typeInfo);
        return WriteAsync(destination, value, cancellationToken);
    }

    // --- read -------------------------------------------------------------------------------------

    /// <inheritdoc cref="Deserialize{T}(ReadOnlySpan{byte})"/>
    /// <param name="source">Exactly one version 1 frame, or one version 0 payload.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public T? Deserialize<T>(ReadOnlySpan<byte> source, BinaryTypeInfo<T> typeInfo)
    {
        Require(typeInfo);
        return ReadExactly<T>(source, target: default);
    }

    /// <inheritdoc cref="Deserialize{T}(ReadOnlySpan{byte}, out int)"/>
    /// <param name="source">Bytes that start with a version 1 frame or a version 0 payload.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="bytesConsumed">The number of bytes the frame occupies.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public T? Deserialize<T>(ReadOnlySpan<byte> source, BinaryTypeInfo<T> typeInfo, out int bytesConsumed)
    {
        Require(typeInfo);
        var value = ReadFirst<T>(source, target: default, out long consumed);
        bytesConsumed = (int)consumed;
        return value;
    }

    /// <inheritdoc cref="Deserialize{T}(ReadOnlySequence{byte})"/>
    /// <param name="source">Exactly one version 1 frame, or one version 0 payload.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public T? Deserialize<T>(ReadOnlySequence<byte> source, BinaryTypeInfo<T> typeInfo)
    {
        Require(typeInfo);
        return ReadExactly<T>(source, target: default);
    }

    /// <inheritdoc cref="Deserialize{T}(ReadOnlySequence{byte}, out SequencePosition)"/>
    /// <param name="source">Bytes that start with a version 1 frame or a version 0 payload.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="consumed">The position just after the frame.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public T? Deserialize<T>(ReadOnlySequence<byte> source, BinaryTypeInfo<T> typeInfo, out SequencePosition consumed)
    {
        Require(typeInfo);
        var value = ReadFirst<T>(source, target: default, out long length);
        consumed = source.GetPosition(length);
        return value;
    }

    /// <inheritdoc cref="Deserialize{T}(Stream)"/>
    /// <param name="source">The stream to read one frame from.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public T? Deserialize<T>(Stream source, BinaryTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(typeInfo);

        var state = BeginOperation();
        return Found(FrameReader.Read<T>(_router, source, target: default, ref state));
    }

    /// <inheritdoc cref="DeserializeAsync{T}(Stream, CancellationToken)"/>
    /// <param name="source">The stream to read one frame from.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="cancellationToken">Cancels the read from the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public ValueTask<T?> DeserializeAsync<T>(
        Stream source,
        BinaryTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(typeInfo);
        return ReadAsync<T>(source, cancellationToken);
    }

    /// <inheritdoc cref="DeserializeAsync{T}(PipeReader, CancellationToken)"/>
    /// <param name="source">The pipe to read one frame from.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="cancellationToken">Cancels the read from the pipe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public ValueTask<T?> DeserializeAsync<T>(
        PipeReader source,
        BinaryTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(typeInfo);
        return ReadAsync<T>(source, cancellationToken);
    }

    /// <inheritdoc cref="DeserializeAsyncEnumerable{T}(Stream, CancellationToken)"/>
    /// <param name="source">The stream to read frames from, until it ends.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="cancellationToken">Cancels the reads from the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type a payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(
        Stream source,
        BinaryTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(typeInfo);
        return ReadFramesAsync<T>(source, cancellationToken);
    }

    /// <inheritdoc cref="DeserializeAsyncEnumerable{T}(PipeReader, CancellationToken)"/>
    /// <param name="source">The pipe to read frames from, until it completes.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="cancellationToken">Cancels the reads from the pipe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type a payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(
        PipeReader source,
        BinaryTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(typeInfo);
        return ReadFramesAsync<T>(source, cancellationToken);
    }

    // --- populate an existing instance ------------------------------------------------------------

    /// <inheritdoc cref="Populate{T}(ReadOnlySpan{byte}, T)"/>
    /// <param name="source">Exactly one version 1 frame, or one version 0 payload.</param>
    /// <param name="target">The instance to read the members into.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public void Populate<T>(ReadOnlySpan<byte> source, T target, BinaryTypeInfo<T> typeInfo)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        Require(typeInfo);
        ReadExactly(source, target);
    }

    /// <inheritdoc cref="Populate{T}(ReadOnlySpan{byte}, T, out int)"/>
    /// <param name="source">Bytes that start with a version 1 frame or a version 0 payload.</param>
    /// <param name="target">The instance to read the members into.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="bytesConsumed">The number of bytes the frame occupies.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public void Populate<T>(ReadOnlySpan<byte> source, T target, BinaryTypeInfo<T> typeInfo, out int bytesConsumed)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        Require(typeInfo);
        ReadFirst(source, target, out long consumed);
        bytesConsumed = (int)consumed;
    }

    /// <inheritdoc cref="Populate{T}(ReadOnlySequence{byte}, T)"/>
    /// <param name="source">Exactly one version 1 frame, or one version 0 payload.</param>
    /// <param name="target">The instance to read the members into.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public void Populate<T>(ReadOnlySequence<byte> source, T target, BinaryTypeInfo<T> typeInfo)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        Require(typeInfo);
        ReadExactly(source, target);
    }

    /// <inheritdoc cref="Populate{T}(ReadOnlySequence{byte}, T, out SequencePosition)"/>
    /// <param name="source">Bytes that start with a version 1 frame or a version 0 payload.</param>
    /// <param name="target">The instance to read the members into.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="consumed">The position just after the frame.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public void Populate<T>(ReadOnlySequence<byte> source, T target, BinaryTypeInfo<T> typeInfo, out SequencePosition consumed)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        Require(typeInfo);
        ReadFirst(source, target, out long length);
        consumed = source.GetPosition(length);
    }

    /// <inheritdoc cref="Populate{T}(Stream, T)"/>
    /// <param name="source">The stream to read one frame from.</param>
    /// <param name="target">The instance to read the members into.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="target"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public void Populate<T>(Stream source, T target, BinaryTypeInfo<T> typeInfo)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Require(typeInfo);

        var state = BeginOperation();
        Found(FrameReader.Read(_router, source, target, ref state));
    }

    /// <inheritdoc cref="PopulateAsync{T}(Stream, T, CancellationToken)"/>
    /// <param name="source">The stream to read one frame from.</param>
    /// <param name="target">The instance to read the members into.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="cancellationToken">Cancels the read from the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="target"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public ValueTask PopulateAsync<T>(
        Stream source,
        T target,
        BinaryTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Require(typeInfo);
        return PopulateFrameAsync(source, target, cancellationToken);
    }

    /// <inheritdoc cref="PopulateAsync{T}(PipeReader, T, CancellationToken)"/>
    /// <param name="source">The pipe to read one frame from.</param>
    /// <param name="target">The instance to read the members into.</param>
    /// <param name="typeInfo">The type information of <typeparamref name="T"/>, from the context the options were built with.</param>
    /// <param name="cancellationToken">Cancels the read from the pipe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="target"/> or <paramref name="typeInfo"/> is null.</exception>
    /// <exception cref="Exceptions.BinaryConfigurationException">
    /// <paramref name="typeInfo"/> belongs to another context than the options', or a type the payload
    /// reaches is not described by it while the reflection path is off.
    /// </exception>
    public ValueTask PopulateAsync<T>(
        PipeReader source,
        T target,
        BinaryTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Require(typeInfo);
        return PopulateFrameAsync(source, target, cancellationToken);
    }

    /// <summary>Refuses missing type information, or type information of another context than the options'.</summary>
    private void Require<T>(BinaryTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        typeInfo.Require(_options);
    }
}
