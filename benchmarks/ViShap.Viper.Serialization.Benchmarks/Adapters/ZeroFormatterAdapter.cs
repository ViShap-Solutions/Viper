using ZeroFormatter;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class ZeroFormatterAdapter : IBenchmarkSerializer
{
    public string Name => "ZeroFormatter";
    public bool IsSupported => true;
    public byte[] Serialize<T>(T value) => ZeroFormatterSerializer.Serialize(value);
    public T Deserialize<T>(byte[] payload) => ZeroFormatterSerializer.Deserialize<T>(payload);
}
