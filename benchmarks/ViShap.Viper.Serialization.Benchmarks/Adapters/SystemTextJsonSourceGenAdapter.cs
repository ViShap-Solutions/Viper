using System.Text.Json;
using System.Text.Json.Serialization;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BenchmarkPayload))]
[JsonSerializable(typeof(BenchmarkItem))]
[JsonSerializable(typeof(BenchmarkNode))]
[JsonSerializable(typeof(BenchmarkScalar))]
public partial class BenchmarkJsonContext : JsonSerializerContext { }

public sealed class SystemTextJsonSourceGenAdapter : IBenchmarkSerializer
{
    public string Name => "System.Text.Json.SourceGen";
    public bool IsSupported => true;
    public byte[] Serialize<T>(T value) => value switch
    {
        BenchmarkPayload payload => JsonSerializer.SerializeToUtf8Bytes(payload, BenchmarkJsonContext.Default.BenchmarkPayload),
        _ => throw new NotSupportedException($"Source-generated JSON adapter does not represent {typeof(T)}")
    };
    public T Deserialize<T>(byte[] payload) => typeof(T)==typeof(BenchmarkPayload)
        ? (T)(object)(JsonSerializer.Deserialize(payload, BenchmarkJsonContext.Default.BenchmarkPayload) ?? throw new JsonException("Null benchmark payload"))
        : throw new NotSupportedException($"Source-generated JSON adapter does not represent {typeof(T)}");
}
