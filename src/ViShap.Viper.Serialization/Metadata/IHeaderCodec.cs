namespace ViShap.Viper.Metadata;

internal interface IHeaderCodec
{
    int Version { get; }
    BinaryHeaderInfo ReadHeaderInfo(BinaryReader reader);
}