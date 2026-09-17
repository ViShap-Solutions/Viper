using System.Security.Cryptography;
using System.Text;

namespace ViShap.Viper.Codec;

internal sealed class V1FormatCodec : IFormatCodec
{
    private const bool SupportsKeyedContracts = true;

    private readonly ICompressor _compressor;
    private readonly IChecksumCalculator _checksum;
    private readonly IEncryptor _encryptor;
    private readonly bool _preserveReferences;
    private readonly DeserializationLimits _limits;

    public V1FormatCodec(
        ICompressor compressor,
        IChecksumCalculator checksum,
        IEncryptor encryptor,
        bool preserveReferences = false,
        DeserializationLimits? limits = null)
    {
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _checksum = checksum ?? throw new ArgumentNullException(nameof(checksum));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _preserveReferences = preserveReferences;
        _limits = limits ?? DeserializationLimits.Default;

        _limits.Validate();
    }

    public int Version => 1;

    public void Serialize<T>(Stream destination, T data)
    {
        ArgumentNullException.ThrowIfNull(destination);

        byte[] rawPayload = SerializePayload(data, _preserveReferences, _limits);

        byte[] checksumBytes = _checksum.Compute(rawPayload);
        byte[] compressedPayload = _compressor.Compress(rawPayload);
        byte[] onDiskPayload = _encryptor.Encrypt(compressedPayload);

        var header =
            new BinaryFormatHeaderV1(
                _compressor.DefaultKind,
                _compressor.DefaultCustomName,
                _checksum.DefaultKind,
                _checksum.DefaultCustomName,
                _encryptor.DefaultKind,
                _encryptor.DefaultCustomName,
                _encryptor.DefaultKeyId,
                _preserveReferences,
                rawPayload.Length,
                compressedPayload.Length,
                onDiskPayload.Length,
                checksumBytes);

        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);

        header.WriteTo(writer);
        writer.Write(onDiskPayload);
        writer.Flush();
    }

    public T? Deserialize<T>(Stream source)
    {
        var (header, rawPayload) = ReadAndUnwrap(source);

        return DeserializePayload<T>(rawPayload, header.PreserveReferences, _limits);
    }

    public T? Deserialize<T>(Stream source, T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);

        var (header, rawPayload) = ReadAndUnwrap(source);

        using var ms = new MemoryStream(rawPayload);

        using var payloadReader =
            new BinaryReader(
                ms,
                Encoding.UTF8,
                leaveOpen: true);

        return new BinaryPayloadReader(
                payloadReader,
                header.PreserveReferences,
                _limits,
                keyedContracts: SupportsKeyedContracts)
            .Deserialize(existingInstance);
    }

    public void Deserialize<T>(Stream source, ref T existingInstance) where T : struct
    {
        var (header, rawPayload) = ReadAndUnwrap(source);

        using var ms = new MemoryStream(rawPayload);

        using var payloadReader =
            new BinaryReader(
                ms,
                Encoding.UTF8,
                leaveOpen: true);

        new BinaryPayloadReader(
                payloadReader,
                header.PreserveReferences,
                _limits,
                keyedContracts: SupportsKeyedContracts)
            .Deserialize(ref existingInstance);
    }

    private (BinaryFormatHeaderV1 Header, byte[] RawPayload) ReadAndUnwrap(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var reader =
            new BinaryReader(
                source,
                Encoding.UTF8,
                leaveOpen: true);

        var header = BinaryFormatHeaderV1.ReadFrom(reader, _limits);

        byte[] onDiskPayload =
            reader.ReadBytes(header.OnDiskLength);

        if (onDiskPayload.Length != header.OnDiskLength)
        {
            throw new BinaryFormatException(
                $"Payload ended early. Expected {header.OnDiskLength} bytes, " +
                $"got {onDiskPayload.Length}.");
        }

        if (header.KeyId is not null &&
            _encryptor.DefaultKeyId is not null &&
            header.KeyId != _encryptor.DefaultKeyId)
        {
            throw new BinaryEncryptionKeyException(
                $"This data is marked as encrypted with key '{header.KeyId}', " +
                $"but the configured encryptor is set up for key '{_encryptor.DefaultKeyId}'.");
        }

        byte[] compressedPayload;

        try
        {
            compressedPayload =
                _encryptor.Decrypt(
                    header.Encryption,
                    header.CustomEncryptionName,
                    onDiskPayload,
                    header.CompressedLength);
        }
        catch (CryptographicException ex)
        {
            throw new BinaryIntegrityException(
                "Decryption failed: wrong key or tampered payload.",
                ex);
        }

        if (compressedPayload.Length != header.CompressedLength)
        {
            throw new BinaryFormatException(
                $"Decryption produced {compressedPayload.Length} bytes, " +
                $"expected {header.CompressedLength}.");
        }

        byte[] rawPayload =
            _compressor.Decompress(
                header.Compression,
                header.CustomCompressionName,
                compressedPayload,
                header.UncompressedLength);

        if (rawPayload.Length != header.UncompressedLength)
        {
            throw new BinaryFormatException(
                $"Decompression produced {rawPayload.Length} bytes, " +
                $"expected {header.UncompressedLength}.");
        }

        _checksum.Verify(
            header.ChecksumAlgorithm,
            header.CustomChecksumName,
            rawPayload,
            header.Checksum);

        return (header, rawPayload);
    }

    private static byte[] SerializePayload<T>(
        T data,
        bool preserveReferences,
        DeserializationLimits limits)
    {
        using var ms = new MemoryStream();

        using var writer =
            new BinaryWriter(
                ms,
                Encoding.UTF8,
                leaveOpen: true);

        new BinaryPayloadWriter(
                writer,
                preserveReferences,
                limits,
                keyedContracts: SupportsKeyedContracts)
            .Serialize(data);

        writer.Flush();

        return ms.ToArray();
    }

    private static T? DeserializePayload<T>(
        byte[] rawPayload,
        bool preserveReferences,
        DeserializationLimits limits)
    {
        using var ms = new MemoryStream(rawPayload);

        using var reader =
            new BinaryReader(
                ms,
                Encoding.UTF8,
                leaveOpen: true);

        return new BinaryPayloadReader(
                reader,
                preserveReferences,
                limits,
                keyedContracts: SupportsKeyedContracts)
            .Deserialize<T>();
    }
}