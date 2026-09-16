using System.Text.Json;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class SystemTextJsonAdapter : IBenchmarkSerializer
{
    public string Name => "System.Text.Json";
    public bool IsSupported => true;
    public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    public T Deserialize<T>(byte[] payload) => JsonSerializer.Deserialize<T>(payload)!;
}
