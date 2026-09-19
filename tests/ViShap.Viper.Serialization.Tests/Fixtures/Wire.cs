namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Hand-builds V1 frames so a test can present a payload no writer would ever produce, and can assert
/// the exact bytes a writer does produce. The layout mirrors <c>BinaryFormatHeaderV1</c>.
/// </summary>
internal static class Wire
{
    public const int Magic = 0x52455342;

    /// <summary>Byte offset of the <c>PreserveReferences</c> flag in a header with no optional strings.</summary>
    public const int PreserveReferencesOffset = 15;

    /// <summary>Length of a header with no optional strings and no checksum.</summary>
    public const int PlainHeaderLength = 29;

    /// <summary>Byte offset of <c>UncompressedLength</c> in a header with no optional strings.</summary>
    public const int UncompressedLengthOffset = 16;

    /// <summary>Byte offset of <c>CompressedLength</c> in a header with no optional strings.</summary>
    public const int CompressedLengthOffset = 20;

    /// <summary>Byte offset of <c>OnDiskLength</c> in a header with no optional strings.</summary>
    public const int OnDiskLengthOffset = 24;

    /// <summary>Byte offset of <c>checksumLength</c> in a header with no optional strings.</summary>
    public const int ChecksumLengthOffset = 28;

    /// <summary>
    /// A V1 header decoded by a reader written for the tests alone, so an assertion about the
    /// envelope never borrows the decoder it is checking.
    /// </summary>
    /// <param name="HeaderLength">Bytes the header occupies, so the payload starts at this offset.</param>
    internal sealed record ParsedHeader(
        int Version,
        byte Compression,
        string? CustomCompressionName,
        byte ChecksumAlgorithm,
        string? CustomChecksumName,
        byte Encryption,
        string? CustomEncryptionName,
        string? KeyId,
        bool PreserveReferences,
        int UncompressedLength,
        int CompressedLength,
        int OnDiskLength,
        byte[] Checksum,
        int HeaderLength);

    /// <summary>Decodes the V1 header at the start of <paramref name="frame"/>.</summary>
    public static ParsedHeader ReadHeader(byte[] frame)
    {
        using var buffer = new MemoryStream(frame, writable: false);
        using var reader = new BinaryReader(buffer);

        int magic = reader.ReadInt32();
        if (magic != Magic)
            throw new InvalidOperationException($"Not a V1 frame: magic {magic:X8}.");

        int version = reader.ReadInt32();
        byte compression = reader.ReadByte();
        string? customCompression = ReadOptional(reader);
        byte checksumAlgorithm = reader.ReadByte();
        string? customChecksum = ReadOptional(reader);
        byte encryption = reader.ReadByte();
        string? customEncryption = ReadOptional(reader);
        string? keyId = ReadOptional(reader);
        bool preserveReferences = reader.ReadBoolean();
        int uncompressedLength = reader.ReadInt32();
        int compressedLength = reader.ReadInt32();
        int onDiskLength = reader.ReadInt32();
        byte[] checksum = reader.ReadBytes(reader.ReadByte());

        return new ParsedHeader(
            version, compression, customCompression,
            checksumAlgorithm, customChecksum,
            encryption, customEncryption,
            keyId, preserveReferences,
            uncompressedLength, compressedLength, onDiskLength,
            checksum, (int)buffer.Position);
    }

    private static string? ReadOptional(BinaryReader reader) =>
        reader.ReadBoolean() ? reader.ReadString() : null;

    /// <summary>
    /// Loads a committed compatibility fixture from <c>Fixtures/Wire</c>. Those files are authored by
    /// hand from §22 and copied to the output directory, so a reader test never checks the writer
    /// against bytes the same writer produced.
    /// </summary>
    public static byte[] Fixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wire", fileName));

    public static byte[] Payload(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        write(writer);
        writer.Flush();
        return buffer.ToArray();
    }

    /// <summary>
    /// A V1 header with no compression, checksum or encryption, declaring
    /// <paramref name="payloadLength"/> for all three phases.
    /// </summary>
    public static byte[] Header(int payloadLength, bool preserveReferences = false) =>
        Payload(writer =>
        {
            writer.Write(Magic);
            writer.Write(1);
            writer.Write((byte)0); writer.Write(false);   // compression + custom name
            writer.Write((byte)0); writer.Write(false);   // checksum + custom name
            writer.Write((byte)0); writer.Write(false);   // encryption + custom name
            writer.Write(false);                          // key id
            writer.Write(preserveReferences);
            writer.Write(payloadLength);                  // uncompressed
            writer.Write(payloadLength);                  // compressed
            writer.Write(payloadLength);                  // on disk
            writer.Write((byte)0);                        // checksum length
        });

    /// <summary>Wraps <paramref name="body"/> in a V1 frame, optionally lying about the lengths.</summary>
    public static byte[] Frame(byte[] body, int? declaredLength = null, bool preserveReferences = false) =>
        [.. Header(declaredLength ?? body.Length, preserveReferences), .. body];

    /// <summary>
    /// A V1 frame with every header field chosen by the caller, for the cases the convenience
    /// builders above cannot express: a declared algorithm the body does not honour, a checksum that
    /// does not match, or a version this build does not know.
    /// </summary>
    public static byte[] FrameWith(
        byte[] body,
        int version = 1,
        byte compression = 0,
        string? customCompressionName = null,
        byte checksumAlgorithm = 0,
        string? customChecksumName = null,
        byte encryption = 0,
        string? customEncryptionName = null,
        string? keyId = null,
        bool preserveReferences = false,
        int? uncompressedLength = null,
        int? compressedLength = null,
        int? onDiskLength = null,
        byte[]? checksum = null)
    {
        byte[] checksumBytes = checksum ?? [];

        return Payload(writer =>
        {
            writer.Write(Magic);
            writer.Write(version);
            writer.Write(compression); WriteOptional(writer, customCompressionName);
            writer.Write(checksumAlgorithm); WriteOptional(writer, customChecksumName);
            writer.Write(encryption); WriteOptional(writer, customEncryptionName);
            WriteOptional(writer, keyId);

            writer.Write(preserveReferences);
            writer.Write(uncompressedLength ?? body.Length);
            writer.Write(compressedLength ?? body.Length);
            writer.Write(onDiskLength ?? body.Length);
            writer.Write((byte)checksumBytes.Length);
            writer.Write(checksumBytes);
            writer.Write(body);
        });
    }

    /// <summary>
    /// Writes an optional header string: a present flag, then the string when present. An empty
    /// string is written as present with a zero length, which no writer produces but a reader must
    /// still handle.
    /// </summary>
    private static void WriteOptional(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
            writer.Write(value);
    }

    /// <summary>A payload of <paramref name="depth"/> nested single-element collections.</summary>
    public static byte[] NestedCollections(int depth) =>
        Frame(Payload(writer =>
        {
            writer.Write(true);
            for (int i = 0; i < depth; i++)
            {
                writer.Write(1);        // collection count
                writer.Write(true);     // child is non-null
            }

            writer.Write(0);            // innermost collection is empty
        }));

    /// <summary>A keyed object declaring <paramref name="fieldCount"/> fields of one <c>int32</c> each.</summary>
    public static byte[] KeyedFields(int fieldCount) =>
        Frame(Payload(writer =>
        {
            writer.Write(true);
            writer.Write7BitEncodedInt(fieldCount);
            for (int key = 0; key < fieldCount; key++)
            {
                writer.Write7BitEncodedInt(key);
                writer.Write(4);
                writer.Write(0);
            }
        }));

    /// <summary>
    /// A V1 header whose custom compression name declares <paramref name="declaredLength"/> bytes
    /// while only <paramref name="actualBytes"/> of it are present. Nothing after the name is
    /// written, because no conforming reader should get that far.
    /// </summary>
    public static byte[] FrameWithOversizedCustomName(int declaredLength, int actualBytes) =>
        Payload(writer =>
        {
            writer.Write(Magic);
            writer.Write(1);
            writer.Write((byte)255);              // CompressionAlgorithm.Custom
            writer.Write(true);                   // the custom name is present
            writer.Write7BitEncodedInt(declaredLength);
            writer.Write(new byte[actualBytes]);
        });

    /// <summary>
    /// A complete V1 header declaring <paramref name="declaredChecksumLength"/> checksum bytes while
    /// only <paramref name="actualBytes"/> follow.
    /// </summary>
    public static byte[] FrameWithOversizedChecksum(int declaredChecksumLength, int actualBytes) =>
        Payload(writer =>
        {
            writer.Write(Magic);
            writer.Write(1);
            writer.Write((byte)0); writer.Write(false);
            writer.Write((byte)0); writer.Write(false);
            writer.Write((byte)0); writer.Write(false);
            writer.Write(false);                  // key id
            writer.Write(false);                  // preserve references
            writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write((byte)declaredChecksumLength);
            writer.Write(new byte[actualBytes]);
        });
}
