using MemoryPack;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class MemoryPackAdapter : IBenchmarkSerializer
{
    public string Name => "MemoryPack";
    public bool IsSupported => true;
    public byte[] Serialize<T>(T value) => MemoryPackSerializer.Serialize(value);
    public T Deserialize<T>(byte[] payload) => MemoryPackSerializer.Deserialize<T>(payload)!;
}
