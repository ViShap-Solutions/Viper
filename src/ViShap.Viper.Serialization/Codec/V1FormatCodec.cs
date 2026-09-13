using System.Security.Cryptography;
using System.Text;

namespace ViShap.Viper.Codec;

internal sealed class V1FormatCodec(
    ICompressor compressor,
    IChecksumCalculator checksum,
    IEncryptor encryptor,
    bool preserveReferences = false) : IFormatCodec
{
    public int Version => 1;

    public void Serialize<T>(Stream destination, T data)
    {
        ArgumentNullException.ThrowIfNull(destination);

        byte[] rawPayload = SerializePayload(data, preserveReferences);
        byte[] checksumBytes = checksum.Compute(rawPayload);
        byte[] compressedPayload = compressor.Compress(rawPayload);
        byte[] onDiskPayload = encryptor.Encrypt(compressedPayload);

        var header = new BinaryFormatHeaderV1(
            compressor.DefaultKind, compressor.DefaultCustomName,
            checksum.DefaultKind, checksum.DefaultCustomName,
            encryptor.DefaultKind, encryptor.DefaultCustomName,
            encryptor.DefaultKeyId,
            preserveReferences,
            rawPayload.Length, compressedPayload.Length, onDiskPayload.Length,
            checksumBytes);

        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        header.WriteTo(writer);
        writer.Write(onDiskPayload);
        writer.Flush();
    }

    public T? Deserialize<T>(Stream source)
    {
        var (header, rawPayload) = ReadAndUnwrap(source);
        return DeserializePayload<T>(rawPayload, header.PreserveReferences);
    }

    public T? Deserialize<T>(Stream source, T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);
        var (header, rawPayload) = ReadAndUnwrap(source);

        using var ms = new MemoryStream(rawPayload);
        using var payloadReader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return new BinaryPayloadReader(payloadReader, preserveReferences: header.PreserveReferences)
            .Deserialize(existingInstance);
    }
    
    public void Deserialize<T>(Stream source, ref T existingInstance) where T : struct
    {
        var (header, rawPayload) = ReadAndUnwrap(source);
        using var ms = new MemoryStream(rawPayload);
        using var payloadReader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        new BinaryPayloadReader(payloadReader, preserveReferences: header.PreserveReferences)
                .Deserialize(ref existingInstance);
    }

    private (BinaryFormatHeaderV1 Header, byte[] RawPayload) ReadAndUnwrap(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        var header = BinaryFormatHeaderV1.ReadFrom(reader);

        byte[] onDiskPayload = reader.ReadBytes(header.OnDiskLength);
        if (onDiskPayload.Length != header.OnDiskLength)
            throw new BinaryFormatException($"Payload ended early. Expected {header.OnDiskLength} bytes, got {onDiskPayload.Length}.");
        
        if (header.KeyId is not null && encryptor.DefaultKeyId is not null && header.KeyId != encryptor.DefaultKeyId)
            throw new BinaryIntegrityException(
                $"This data is marked as encrypted with key '{header.KeyId}', but the configured encryptor is set up for key '{encryptor.DefaultKeyId}'.");

        byte[] compressedPayload;
        try
        {
            compressedPayload = encryptor.Decrypt(header.Encryption, header.CustomEncryptionName, onDiskPayload, header.CompressedLength);
        }
        catch (CryptographicException ex)
        {
            throw new BinaryIntegrityException("Decryption failed: wrong key or tampered payload.", ex);
        }

        byte[] rawPayload = compressor.Decompress(header.Compression, header.CustomCompressionName, compressedPayload, header.UncompressedLength);

        checksum.Verify(header.ChecksumAlgorithm, header.CustomChecksumName, rawPayload, header.Checksum);

        return (header, rawPayload);
    }

    private static byte[] SerializePayload<T>(T data, bool preserveReferences)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        new BinaryPayloadWriter(writer, preserveReferences).Serialize(data);
        writer.Flush();
        return ms.ToArray();
    }

    private static T? DeserializePayload<T>(byte[] rawPayload, bool preserveReferences)
    {
        using var ms = new MemoryStream(rawPayload);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return new BinaryPayloadReader(reader, preserveReferences).Deserialize<T>();
    }
}