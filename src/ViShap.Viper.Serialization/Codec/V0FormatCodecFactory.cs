namespace ViShap.Viper.Codec;

internal sealed class V0FormatCodecFactory : IFormatCodecFactory
{
    public int Version => 0;

    public IFormatCodec Create(BinarySerializerOptions options)
    {
        options.Limits.Validate();
        
        return new V0FormatCodec(options.Limits);
    }
}