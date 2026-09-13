namespace ViShap.Viper.Codec;

internal interface IFormatCodecFactory
{
    int Version { get; }
    IFormatCodec Create(BinarySerializerOptions options);
}