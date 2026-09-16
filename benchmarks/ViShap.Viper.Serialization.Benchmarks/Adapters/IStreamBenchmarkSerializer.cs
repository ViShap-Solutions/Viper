namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public interface IStreamBenchmarkSerializer
{
    string Name { get; }
    void Serialize<T>(Stream destination, T value);
    T Deserialize<T>(Stream source);
}
