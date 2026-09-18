namespace ViShap.Viper.Metadata;

internal sealed class V1HeaderCodec : IHeaderCodec
{
    public int Version => BinaryFormatHeaderV1.Version;

    public BinaryHeaderInfo ReadHeaderInfo(BinaryReader reader, SerializationLimits limits)
    {
        var header = BinaryFormatHeaderV1.ReadFrom(reader, limits);
        return new BinaryHeaderInfo(
            BinaryFormatHeaderV1.Version,
            header.Compression,
            header.CustomCompressionName,
            header.ChecksumAlgorithm,
            header.CustomChecksumName,
            header.Encryption,
            header.CustomEncryptionName,
            header.KeyId);
    }
}