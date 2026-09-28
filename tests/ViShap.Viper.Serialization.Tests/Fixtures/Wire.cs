namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Hand-builds V1 frames and payloads so a test can present bytes no writer would ever produce, and
/// can assert the exact bytes a writer does produce. The layout is the one §22 documents, spelled out
/// here with its own encoder and decoder, so an assertion about the envelope never borrows the code it
/// is checking.
/// </summary>
internal static class Wire
{
    public const int Magic = 0x52455342;

    /// <summary>Byte offset of the payload mode in a version 1 header: after the magic and the one-byte version.</summary>
    public const int ModeOffset = 5;

    /// <summary>Byte offset of the service count in a version 1 header.</summary>
    public const int ServiceCountOffset = 6;

    /// <summary>The service numbers of §22.6.</summary>
    public const int ChecksumService = 1;

    public const int CompressionService = 2;

    public const int EncryptionService = 3;

    /// <summary>One service record of a decoded header: its number, criticality and where its body lies.</summary>
    /// <param name="KindOffset">Offset of the record's kind in the frame.</param>
    /// <param name="BodyOffset">Offset of the record's body in the frame.</param>
    internal sealed record ParsedService(int Number, bool Critical, int KindOffset, int BodyOffset, int BodyLength);

    /// <summary>
    /// A V1 header decoded by a reader written for the tests alone.
    /// </summary>
    /// <param name="OnDiskLengthOffset">Offset of the <c>onDiskLength</c> integer in the frame.</param>
    /// <param name="HeaderLength">Bytes the header occupies, so the stored payload starts at this offset.</param>
    internal sealed record ParsedHeader(
        int Version,
        int Mode,
        IReadOnlyList<ParsedService> Services,
        byte ChecksumAlgorithm,
        string? CustomChecksumName,
        byte[] Checksum,
        byte Compression,
        string? CustomCompressionName,
        int? UncompressedLength,
        byte Encryption,
        string? CustomEncryptionName,
        string? KeyId,
        int OnDiskLength,
        int OnDiskLengthOffset,
        int HeaderLength)
    {
        public bool PreserveReferences => (Mode & 1) == 1;

        /// <summary>The record of <paramref name="number"/>, which the header must carry.</summary>
        public ParsedService Service(int number) => Services.Single(service => service.Number == number);
    }

    /// <summary>Decodes the V1 header at the start of <paramref name="frame"/>.</summary>
    public static ParsedHeader ReadHeader(byte[] frame)
    {
        int position = 0;
        int magic = BitConverter.ToInt32(frame, 0);
        if (magic != Magic)
            throw new InvalidOperationException($"Not a V1 frame: magic {magic:X8}.");

        position += 4;
        int version = ReadVarint(frame, ref position);
        int mode = ReadVarint(frame, ref position);
        int count = ReadVarint(frame, ref position);

        var services = new List<ParsedService>();
        byte checksumAlgorithm = 0, compression = 0, encryption = 0;
        string? checksumName = null, compressionName = null, encryptionName = null, keyId = null;
        byte[] checksum = [];
        int? uncompressed = null;

        for (int record = 0; record < count; record++)
        {
            int kindOffset = position;
            int kind = ReadVarint(frame, ref position);
            int length = ReadVarint(frame, ref position);
            int bodyOffset = position;
            services.Add(new ParsedService(kind >> 1, (kind & 1) == 1, kindOffset, bodyOffset, length));

            int cursor = bodyOffset;
            switch (kind >> 1)
            {
                case ChecksumService:
                    checksumAlgorithm = (byte)ReadVarint(frame, ref cursor);
                    checksumName = checksumAlgorithm == 255 ? ReadString(frame, ref cursor) : null;
                    checksum = frame[cursor..(bodyOffset + length)];
                    break;
                case CompressionService:
                    compression = (byte)ReadVarint(frame, ref cursor);
                    compressionName = compression == 255 ? ReadString(frame, ref cursor) : null;
                    uncompressed = ReadVarint(frame, ref cursor);
                    break;
                case EncryptionService:
                    encryption = (byte)ReadVarint(frame, ref cursor);
                    encryptionName = encryption == 255 ? ReadString(frame, ref cursor) : null;
                    int folded = ReadVarint(frame, ref cursor);
                    keyId = folded == 0 ? null : System.Text.Encoding.UTF8.GetString(frame, cursor, folded - 1);
                    break;
            }

            position = bodyOffset + length;
        }

        int onDiskOffset = position;
        int onDisk = ReadVarint(frame, ref position);

        return new ParsedHeader(
            version, mode, services,
            checksumAlgorithm, checksumName, checksum,
            compression, compressionName, uncompressed,
            encryption, encryptionName, keyId,
            onDisk, onDiskOffset, position);
    }

    /// <summary>The bytes a frame stores after its header.</summary>
    public static byte[] Body(byte[] frame) => frame[ReadHeader(frame).HeaderLength..];

    /// <summary>
    /// <paramref name="frame"/> with its header re-encoded around other declared lengths: the
    /// compression record's uncompressed length, the on-disk length, or both. Every other record is
    /// kept byte for byte, and the stored bytes are unchanged.
    /// </summary>
    public static byte[] WithLengths(byte[] frame, int? uncompressedLength = null, int? onDiskLength = null)
    {
        var header = ReadHeader(frame);
        var services = header.Services
            .Select(service =>
                service.Number == CompressionService && uncompressedLength is { } length
                    ? CompressionRecord(header.Compression, length, header.CustomCompressionName)
                    : frame[service.KindOffset..(service.BodyOffset + service.BodyLength)])
            .ToArray();

        return FrameWith(
            frame[header.HeaderLength..],
            header.Version,
            header.Mode,
            services,
            onDiskLength: onDiskLength ?? header.OnDiskLength);
    }

    private static int ReadVarint(byte[] bytes, ref int position)
    {
        int result = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte current = bytes[position++];
            result |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return result;
        }
    }

    private static string ReadString(byte[] bytes, ref int position)
    {
        int length = ReadVarint(bytes, ref position);
        string value = System.Text.Encoding.UTF8.GetString(bytes, position, length);
        position += length;
        return value;
    }

    /// <summary>
    /// Loads a committed compatibility fixture from <c>Fixtures/Wire</c>. Those files are copied to the
    /// output directory and read, never rewritten, by the tests.
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

    /// <summary>The minimal 7-bit encoding of <paramref name="value"/> (§22.1).</summary>
    public static byte[] Varint(int value) =>
        Payload(writer => writer.Write7BitEncodedInt(value));

    /// <summary>
    /// A V1 header with no service, declaring <paramref name="onDiskLength"/> bytes after it:
    /// magic, version 1, the payload mode, a service count of zero, then the length.
    /// </summary>
    public static byte[] Header(int onDiskLength, bool preserveReferences = false) =>
        [.. BitConverter.GetBytes(Magic), 0x01, (byte)(preserveReferences ? 1 : 0), 0x00, .. Varint(onDiskLength)];

    /// <summary>Wraps <paramref name="body"/> in a V1 frame with no service, optionally lying about its length.</summary>
    public static byte[] Frame(byte[] body, int? declaredLength = null, bool preserveReferences = false) =>
        [.. Header(declaredLength ?? body.Length, preserveReferences), .. body];

    /// <summary>A service record: <c>(number &lt;&lt; 1) | critical</c>, the body length, then the body.</summary>
    public static byte[] Service(int number, bool critical, byte[] body, int? declaredLength = null) =>
        [.. Varint((number << 1) | (critical ? 1 : 0)), .. Varint(declaredLength ?? body.Length), .. body];

    /// <summary>An algorithm's id and, for <c>Custom</c> (255), its name.</summary>
    public static byte[] Algorithm(byte id, string? name = null) =>
        id == 255
            ? [.. Varint(id), .. Varint(System.Text.Encoding.UTF8.GetByteCount(name ?? string.Empty)), .. System.Text.Encoding.UTF8.GetBytes(name ?? string.Empty)]
            : Varint(id);

    /// <summary>The checksum record: <c>id · [name] · hash</c>.</summary>
    public static byte[] ChecksumRecord(byte id, byte[] hash, string? name = null) =>
        Service(ChecksumService, critical: true, [.. Algorithm(id, name), .. hash]);

    /// <summary>The compression record: <c>id · [name] · uncompressedLength</c>.</summary>
    public static byte[] CompressionRecord(byte id, int uncompressedLength, string? name = null) =>
        Service(CompressionService, critical: true, [.. Algorithm(id, name), .. Varint(uncompressedLength)]);

    /// <summary>The encryption record: <c>id · [name] · keyId</c>, the key id folded with its null.</summary>
    public static byte[] EncryptionRecord(byte id, string? keyId = null, string? name = null) =>
        Service(EncryptionService, critical: true, [.. Algorithm(id, name), .. OptionalString(keyId)]);

    /// <summary>A string that may be absent: zero, or its UTF-8 length plus one, then the bytes.</summary>
    public static byte[] OptionalString(string? value) =>
        value is null
            ? [0]
            : [.. Varint(System.Text.Encoding.UTF8.GetByteCount(value) + 1), .. System.Text.Encoding.UTF8.GetBytes(value)];

    /// <summary>
    /// A V1 frame with every header field chosen by the caller: the version, the payload mode, the
    /// service records as raw bytes and how many the header claims, and the declared length.
    /// </summary>
    public static byte[] FrameWith(
        byte[] body,
        int version = 1,
        int mode = 0,
        byte[][]? services = null,
        int? serviceCount = null,
        int? onDiskLength = null)
    {
        services ??= [];
        return
        [
            .. BitConverter.GetBytes(Magic),
            .. Varint(version),
            .. Varint(mode),
            .. Varint(serviceCount ?? services.Length),
            .. services.SelectMany(service => service),
            .. Varint(onDiskLength ?? body.Length),
            .. body
        ];
    }

    /// <summary>
    /// A frame holding one non-null container: the count plus one, which carries the container's null,
    /// then <paramref name="int32Values"/> four-byte values as its body. A dictionary entry carries two
    /// of them, which is why the body is measured in values rather than in elements.
    /// </summary>
    public static byte[] Container(int declaredCount, int int32Values) =>
        Frame(Payload(writer =>
        {
            writer.Write7BitEncodedInt(declaredCount + 1);
            for (int value = 0; value < int32Values; value++)
                writer.Write(value);
        }));

    /// <summary>
    /// A frame holding one non-null string that declares <paramref name="declaredByteLength"/> UTF-8
    /// bytes, written as that length plus one, while only <paramref name="content"/> follows.
    /// </summary>
    public static byte[] StringValue(int declaredByteLength, params byte[] content) =>
        Frame([.. Varint(declaredByteLength + 1), .. content]);

    /// <summary>
    /// A frame holding one non-null <see cref="System.Collections.BitArray"/>: the bit count plus one,
    /// then a blob of <paramref name="dataBytes"/> bytes (§22.4).
    /// </summary>
    public static byte[] BitArrayValue(int declaredBits, int dataBytes) =>
        Frame([.. Varint(declaredBits + 1), .. Varint(dataBytes), .. new byte[dataBytes]]);

    /// <summary>
    /// A frame holding one non-null array of rank greater than one: the rank plus one, one length per
    /// dimension, then <paramref name="int32Elements"/> elements in row-major order.
    /// </summary>
    public static byte[] MultiDimensionalArray(int[] lengths, int int32Elements, int? declaredRank = null) =>
        Frame(Payload(writer =>
        {
            writer.Write7BitEncodedInt((declaredRank ?? lengths.Length) + 1);
            foreach (int length in lengths)
                writer.Write7BitEncodedInt(length);

            for (int element = 0; element < int32Elements; element++)
                writer.Write(element);
        }));

    /// <summary>
    /// A payload of <paramref name="depth"/> nested single-element collections: each count is one,
    /// written as two because it carries the collection's null, and the innermost is empty.
    /// </summary>
    public static byte[] NestedCollections(int depth) =>
        Frame([.. Enumerable.Repeat((byte)0x02, depth), 0x01]);

    /// <summary>The flag byte a positional object, a union or a <see cref="Nullable{T}"/> carries when present (§22.2).</summary>
    public static readonly byte[] NotNull = [1];

    /// <summary>One field of a hand-built keyed object.</summary>
    /// <param name="Key">The 7-bit encoded key.</param>
    /// <param name="Payload">The field's bytes.</param>
    /// <param name="DeclaredLength">The length the field claims, when it is not the real one.</param>
    internal readonly record struct KeyedField(int Key, byte[] Payload, int? DeclaredLength = null);

    /// <summary>
    /// The body of a keyed object: the field count — plus one when it carries the object's null, as it
    /// does for a keyed class that is not reference-framed — then each field as key, <c>int32</c>
    /// declared length and payload. Both counts can lie, which is why the builder takes them separately.
    /// </summary>
    public static byte[] KeyedBody(IEnumerable<KeyedField> fields, int? declaredFieldCount = null, bool nullFolded = true)
    {
        var materialized = fields.ToArray();

        return Payload(writer =>
        {
            writer.Write7BitEncodedInt((declaredFieldCount ?? materialized.Length) + (nullFolded ? 1 : 0));
            foreach (var field in materialized)
            {
                writer.Write7BitEncodedInt(field.Key);
                writer.Write(field.DeclaredLength ?? field.Payload.Length);
                writer.Write(field.Payload);
            }
        });
    }

    /// <summary>
    /// A reference frame (§22.2): <c>((id &lt;&lt; 1) | back) + 1</c> — a first occurrence when
    /// <paramref name="back"/> is false, a back reference otherwise.
    /// </summary>
    public static byte[] ReferenceFrame(int id, bool back) =>
        Varint(((id << 1) | (back ? 1 : 0)) + 1);

    /// <summary>The reference frame of null: a single zero.</summary>
    public static readonly byte[] NullReference = [0];

    /// <summary>A keyed class declaring <paramref name="fieldCount"/> fields of one <c>int32</c> each.</summary>
    public static byte[] KeyedFields(int fieldCount) =>
        Frame(Payload(writer =>
        {
            writer.Write7BitEncodedInt(fieldCount + 1);
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
    public static byte[] FrameWithOversizedCustomName(int declaredLength, int actualBytes)
    {
        byte[] body = [.. Varint(255), .. Varint(declaredLength), .. new byte[actualBytes]];
        return
        [
            .. BitConverter.GetBytes(Magic), 0x01, 0x00, 0x01,
            .. Varint((CompressionService << 1) | 1), .. Varint(body.Length), .. body
        ];
    }
}
