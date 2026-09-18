namespace ViShap.Viper.Pipeline;

/// <summary>
/// One wire format. A pipeline owns framing and phase ordering; it receives the operation from the
/// public API and never invents limits, a budget or a key of its own.
/// </summary>
internal interface IFormatPipeline
{
    int Version { get; }

    void Write<T>(Stream destination, T data, SerializationOperation operation);

    T? Read<T>(Stream source, SerializationOperation operation);

    T Read<T>(Stream source, T existingInstance, SerializationOperation operation) where T : class;
}
