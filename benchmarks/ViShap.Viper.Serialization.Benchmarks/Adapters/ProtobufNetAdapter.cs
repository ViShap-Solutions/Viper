using ProtoBuf;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class ProtobufNetAdapter : IBenchmarkSerializer
{
    public string Name => "protobuf-net";
    public bool IsSupported => true;
    public byte[] Serialize<T>(T value) { using var ms=new MemoryStream(); Serializer.Serialize(ms,value); return ms.ToArray(); }
    public T Deserialize<T>(byte[] payload) { using var ms=new MemoryStream(payload); return Serializer.Deserialize<T>(ms); }
}
