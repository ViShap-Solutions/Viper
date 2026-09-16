using System.Text.Json;
using System.Xml.Serialization;
using MessagePack;
using ProtoBuf;
using Orleans.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ViShap.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class ViperStreamAdapter : IStreamBenchmarkSerializer
{
    private readonly BinarySerializer _serializer=new(); public string Name=>"Viper";
    public void Serialize<T>(Stream destination,T value)=>_serializer.Serialize(destination,value);
    public T Deserialize<T>(Stream source)=>_serializer.Deserialize<T>(source)!;
}
public sealed class SystemTextJsonStreamAdapter : IStreamBenchmarkSerializer
{
    public string Name=>"System.Text.Json";
    public void Serialize<T>(Stream destination,T value)=>JsonSerializer.Serialize(destination,value);
    public T Deserialize<T>(Stream source)=>JsonSerializer.Deserialize<T>(source)!;
}
public sealed class ProtobufNetStreamAdapter : IStreamBenchmarkSerializer
{
    public string Name=>"protobuf-net";
    public void Serialize<T>(Stream destination,T value)=>ProtoBuf.Serializer.Serialize(destination,value);
    public T Deserialize<T>(Stream source)=>ProtoBuf.Serializer.Deserialize<T>(source);
}
public sealed class MessagePackStreamAdapter : IStreamBenchmarkSerializer
{
    public string Name=>"MessagePack for C#";
    public void Serialize<T>(Stream destination,T value)=>MessagePackSerializer.Serialize(destination,value);
    public T Deserialize<T>(Stream source)=>MessagePackSerializer.Deserialize<T>(source);
}
public sealed class OrleansStreamAdapter : IStreamBenchmarkSerializer, IDisposable
{
    private readonly ServiceProvider _provider; private readonly Orleans.Serialization.Serializer _serializer;
    public OrleansStreamAdapter(){var services=new Microsoft.Extensions.DependencyInjection.ServiceCollection();services.AddSerializer();_provider=services.BuildServiceProvider();_serializer=_provider.GetRequiredService<Orleans.Serialization.Serializer>();}
    public string Name=>"Orleans.Serialization"; public void Serialize<T>(Stream destination,T value)=>_serializer.Serialize(value,destination); public T Deserialize<T>(Stream source)=>_serializer.Deserialize<T>(source); public void Dispose()=>_provider.Dispose();
}
public sealed class XmlSerializerStreamAdapter : IStreamBenchmarkSerializer
{
    private readonly XmlSerializer _serializer=new(typeof(ViShap.Viper.Serialization.Benchmarks.Models.BenchmarkPayload)); public string Name=>"XmlSerializer";
    public void Serialize<T>(Stream destination,T value)=>_serializer.Serialize(destination,value); public T Deserialize<T>(Stream source)=>(T)_serializer.Deserialize(source)!;
}
