namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public interface IBenchmarkSerializer
{
    string Name { get; }
    bool IsSupported { get; }
    byte[] Serialize<T>(T value);
    T Deserialize<T>(byte[] payload);
}
