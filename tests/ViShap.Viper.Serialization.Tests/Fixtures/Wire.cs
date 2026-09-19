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
        byte checksumAlgorithm = 0,
        byte encryption = 0,
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
            writer.Write(compression); writer.Write(false);
            writer.Write(checksumAlgorithm); writer.Write(false);
            writer.Write(encryption); writer.Write(false);

            writer.Write(keyId is not null);
            if (keyId is not null)
                writer.Write(keyId);

            writer.Write(preserveReferences);
            writer.Write(uncompressedLength ?? body.Length);
            writer.Write(compressedLength ?? body.Length);
            writer.Write(onDiskLength ?? body.Length);
            writer.Write((byte)checksumBytes.Length);
            writer.Write(checksumBytes);
            writer.Write(body);
        });
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
