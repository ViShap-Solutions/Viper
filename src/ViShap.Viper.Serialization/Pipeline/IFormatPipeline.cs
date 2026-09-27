namespace ViShap.Viper.Pipeline;

/// <summary>
/// One wire format. A pipeline owns framing and phase ordering; it receives the operation from the
/// public API and never invents limits, a budget or a key of its own. It builds a whole frame in the
/// serializer's own buffers and hands it back, so the caller decides where the bytes go and nothing
/// reaches a destination before the frame is complete.
/// </summary>
internal interface IFormatPipeline
{
    int Version { get; }

    /// <summary>Encodes <paramref name="data"/> as one frame, which the caller copies out and disposes.</summary>
    EncodedFrame Write<T>(T data, SerializationOperation operation);

    /// <summary>
    /// Reads one value from a seekable stream positioned at a frame, leaving it where the decoded
    /// bytes end.
    /// </summary>
    object? Read(Stream source, Type declaredType, object? existingInstance, SerializationOperation operation);

    /// <summary>Reads one value from bytes that start with a frame.</summary>
    object? Read(ReadOnlySpan<byte> source, Type declaredType, object? existingInstance, SerializationOperation operation);
}
