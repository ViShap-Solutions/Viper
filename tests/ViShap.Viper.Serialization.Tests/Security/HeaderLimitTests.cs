namespace ViShap.Viper.Serialization.Tests.Security;

public sealed class HeaderLimitTests
{
    private static DeserializationLimits Limits => new(){MaxDepth=4,MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=2,MaxStringLength=8,MaxByteBlobLength=8,MaxTotalElements=5,MaxMessageBytes=32};

    [Fact] public void HDR04_OptionalHeaderNamesRespectStringLimit()
    {
        var exact=Header(compression:CompressionAlgorithm.Custom,customCompression:"12345678"); using(var r=new BinaryReader(new MemoryStream(exact))) Assert.Equal("12345678",BinaryFormatHeaderV1.ReadFrom(r,Limits).CustomCompressionName);
        var above=Header(compression:CompressionAlgorithm.Custom,customCompression:"123456789"); using(var r=new BinaryReader(new MemoryStream(above))) Assert.Throws<BinaryFormatException>(()=>BinaryFormatHeaderV1.ReadFrom(r,Limits));
    }

    [Fact] public void HDR04_KeyIdBoundaryIsEnforced()
    {
        var exact=Header(encryption:EncryptionAlgorithm.Aes256Gcm,keyId:"12345678"); using(var r=new BinaryReader(new MemoryStream(exact))) Assert.Equal("12345678",BinaryFormatHeaderV1.ReadFrom(r,Limits).KeyId);
        var above=Header(encryption:EncryptionAlgorithm.Aes256Gcm,keyId:"123456789"); using(var r=new BinaryReader(new MemoryStream(above))) Assert.Throws<BinaryFormatException>(()=>BinaryFormatHeaderV1.ReadFrom(r,Limits));
    }

    [Fact] public void MessageLengthBoundaryIsValidatedBeforePayloadAllocation()
    {
        foreach(var value in new[]{0,32,-1,33,int.MaxValue})
        {
            var b=Header(uncompressed:value,compressed:value,onDisk:value); using var r=new BinaryReader(new MemoryStream(b));
            if(value>=0&&value<=32) BinaryFormatHeaderV1.ReadFrom(r,Limits); else Assert.Throws<BinaryFormatException>(()=>BinaryFormatHeaderV1.ReadFrom(r,Limits));
        }
    }

    [Fact] public void HDR05_AllUnknownEnumBytesAreRejected()
    {
        foreach(var mutate in new Action<byte[]>[]{b=>b[8]=254,b=>b[10]=254,b=>b[12]=254}) { var b=Header();mutate(b);using var r=new BinaryReader(new MemoryStream(b));Assert.Throws<BinaryFormatNotSupportedException>(()=>BinaryFormatHeaderV1.ReadFrom(r,Limits)); }
    }

    private static byte[] Header(CompressionAlgorithm compression=CompressionAlgorithm.None,string? customCompression=null,ChecksumAlgorithm checksum=ChecksumAlgorithm.None,string? customChecksum=null,EncryptionAlgorithm encryption=EncryptionAlgorithm.None,string? customEncryption=null,string? keyId=null,int uncompressed=0,int compressed=0,int onDisk=0)
    {
        using var ms=new MemoryStream();using var w=new BinaryWriter(ms,Encoding.UTF8,true);w.Write(0x52455342);w.Write(1);w.Write((byte)compression);WriteOptional(w,customCompression);w.Write((byte)checksum);WriteOptional(w,customChecksum);w.Write((byte)encryption);WriteOptional(w,customEncryption);WriteOptional(w,keyId);w.Write(false);w.Write(uncompressed);w.Write(compressed);w.Write(onDisk);w.Write((byte)0);w.Flush();return ms.ToArray();
    }
    private static void WriteOptional(BinaryWriter w,string? value){w.Write(value is not null);if(value is not null)w.Write(value);}
}
