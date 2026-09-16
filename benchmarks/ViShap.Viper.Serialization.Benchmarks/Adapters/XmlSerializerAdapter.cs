using System.Xml.Serialization;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class XmlSerializerAdapter : IBenchmarkSerializer
{
    private readonly XmlSerializer _serializer = new(typeof(ViShap.Viper.Serialization.Benchmarks.Models.BenchmarkPayload));
    public string Name => "System.Xml.Serialization.XmlSerializer";
    public bool IsSupported => true;
    public byte[] Serialize<T>(T value) { using var ms=new MemoryStream(); _serializer.Serialize(ms,value); return ms.ToArray(); }
    public T Deserialize<T>(byte[] payload) { using var ms=new MemoryStream(payload); return (T)_serializer.Deserialize(ms)!; }
}
