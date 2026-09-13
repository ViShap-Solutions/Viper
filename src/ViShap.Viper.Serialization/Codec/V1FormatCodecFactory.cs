namespace ViShap.Viper.Codec;

internal sealed class V1FormatCodecFactory : IFormatCodecFactory
{
    public int Version => 1;

    public IFormatCodec Create(BinarySerializerOptions options) =>
        new V1FormatCodec(options.Compressor, options.Checksum, options.Encryptor, options.PreserveReferences);
}