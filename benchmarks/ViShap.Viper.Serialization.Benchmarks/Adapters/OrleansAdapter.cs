using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class OrleansAdapter : IBenchmarkSerializer, IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly Serializer _serializer;
    public OrleansAdapter()
    {
        var services=new ServiceCollection(); services.AddSerializer(); _provider=services.BuildServiceProvider(); _serializer=_provider.GetRequiredService<Serializer>();
    }
    public string Name=>"Orleans.Serialization";
    public bool IsSupported=>true;
    public byte[] Serialize<T>(T value)=>_serializer.SerializeToArray(value);
    public T Deserialize<T>(byte[] payload)=>_serializer.Deserialize<T>(payload);
    public void Dispose()=>_provider.Dispose();
}
