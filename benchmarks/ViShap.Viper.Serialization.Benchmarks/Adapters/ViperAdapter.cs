using ViShap.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class ViperAdapter : IBenchmarkSerializer
{
    private readonly BinarySerializer _serializer = new();
    public string Name => "ViShap.Viper.BinarySerializer";
    public bool IsSupported => true;
    public byte[] Serialize<T>(T value) => _serializer.Serialize(value);
    public T Deserialize<T>(byte[] payload) => _serializer.Deserialize<T>(payload)!;
}
