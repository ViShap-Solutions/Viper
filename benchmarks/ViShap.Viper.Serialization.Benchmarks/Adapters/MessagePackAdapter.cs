using MessagePack;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

public sealed class MessagePackAdapter : IBenchmarkSerializer
{
    public string Name => "MessagePack for C#";
    public bool IsSupported => true;
    public byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value);
    public T Deserialize<T>(byte[] payload) => MessagePackSerializer.Deserialize<T>(payload);
}
