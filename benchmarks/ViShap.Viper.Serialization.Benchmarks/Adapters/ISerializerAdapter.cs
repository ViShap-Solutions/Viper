namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

/// <summary>A serializer measured through its buffered entry points.</summary>
internal interface IBufferedSerializer
{
    string Name { get; }

    byte[] Serialize<T>(T value);

    T? Deserialize<T>(byte[] payload);
}

/// <summary>A serializer measured through its stream entry points.</summary>
internal interface IStreamingSerializer
{
    string Name { get; }

    void Serialize<T>(Stream destination, T value);

    T? Deserialize<T>(Stream source);
}
