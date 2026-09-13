namespace ViShap.Viper.Codec;

internal interface IFormatCodec
{
    int Version { get; }

    void Serialize<T>(Stream destination, T data);
    T? Deserialize<T>(Stream source);
    T? Deserialize<T>(Stream source, T existingInstance) where T : class;
    void Deserialize<T>(Stream source, ref T existingInstance) where T : struct;
}