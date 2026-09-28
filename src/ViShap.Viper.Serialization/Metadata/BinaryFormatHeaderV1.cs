using System.Runtime.CompilerServices;
using System.Text;

namespace ViShap.Viper.Metadata;

/// <summary>
/// V1 envelope metadata: the magic number, the version, the payload mode, the list of service records
/// and the length of the bytes that follow. The header owns its own invariants: it admits exactly one
/// encoding of every field — minimal integers, records in ascending number, each number at most once,
/// every body read exactly — and it is at most <see cref="MaxLength"/> bytes long. Authenticated
/// encryption binds the header's exact bytes as associated data, so no byte of it can be altered
/// without breaking the tag.
/// </summary>
/// <remarks>
/// <code>
/// magic          4 bytes   0x52455342, little-endian
/// version        varint    1
/// payload mode   varint    bit 0: references; every other bit reserved and zero
/// service count  varint
/// services       records   kind varint ((number &lt;&lt; 1) | critical) · length varint · body
/// onDiskLength   varint    the bytes after the header
/// </code>
/// Services, in ascending number: 1 checksum (<c>id · [name] · hash</c>), 2 compression
/// (<c>id · [name] · uncompressedLength</c>), 3 encryption (<c>id · [name] · keyId</c>). All three are
/// critical. An absent phase is an absent record.
/// </remarks>
internal readonly record struct BinaryFormatHeaderV1(
    bool PreserveReferences,
    ChecksumAlgorithm ChecksumAlgorithm,
    string? CustomChecksumName,
    byte[] Checksum,
    CompressionAlgorithm Compression,
    string? CustomCompressionName,
    int UncompressedLength,
    EncryptionAlgorithm Encryption,
    string? CustomEncryptionName,
    string? KeyId,
    int OnDiskLength)
{
    public const int Version = 1;

    /// <summary>
    /// The most bytes a header occupies, from the first byte of the magic to the last byte of
    /// <see cref="OnDiskLength"/>: a fixed bound of the format, not a policy. Reading a header never
    /// needs more of the source than this.
    /// </summary>
    public const int MaxLength = 4096;

    /// <summary>The service number of the checksum, an annotation.</summary>
    public const int ChecksumService = 1;

    /// <summary>The service number of compression, a transform.</summary>
    public const int CompressionService = 2;

    /// <summary>The service number of encryption, a transform.</summary>
    public const int EncryptionService = 3;

    /// <summary>The payload-mode bit that says the payload carries reference frames.</summary>
    private const int ReferencesMode = 1;

    /// <summary>The value every algorithm enum gives <c>Custom</c>, which is followed by a name.</summary>
    private const int CustomId = 255;

    private const int MagicLength = sizeof(int);

    /// <summary>
    /// Finds how long the header at the start of <paramref name="prefix"/> is, so that a source which
    /// must not be read past the frame can be asked for exactly the header's bytes.
    /// </summary>
    /// <param name="prefix">The first bytes of a frame, at least as far as they have arrived.</param>
    /// <param name="length">
    /// When the method returns <see langword="true"/>, the header's length — or the length of
    /// <paramref name="prefix"/> when its bytes are not a well-formed header, which
    /// <see cref="ReadFrom"/> then reports. When it returns <see langword="false"/>, the number of
    /// bytes, counted from the start, that must be present before the length can be found: never more
    /// than any header beginning with these bytes occupies, so asking the source for them never
    /// reaches past the frame.
    /// </param>
    /// <returns>Whether the length is known.</returns>
    public static bool TryMeasure(ReadOnlySpan<byte> prefix, out int length)
    {
        if (prefix.Length < MagicLength)
            return Need(MagicLength + 4, out length);

        int position = MagicLength;

        for (int field = 0; field < 3; field++)
        {
            var status = Next(prefix, ref position, out int value, out length);
            if (status != SevenBitStatus.Complete)
                return status == SevenBitStatus.Incomplete ? Need(length, out length) : Malformed(prefix, out length);

            if (field == 2)
            {
                for (int record = 0; record < value; record++)
                {
                    status = Next(prefix, ref position, out _, out length);
                    if (status != SevenBitStatus.Complete)
                        return status == SevenBitStatus.Incomplete ? Need(length, out length) : Malformed(prefix, out length);

                    status = Next(prefix, ref position, out int bodyLength, out length);
                    if (status != SevenBitStatus.Complete)
                        return status == SevenBitStatus.Incomplete ? Need(length, out length) : Malformed(prefix, out length);

                    if (bodyLength > MaxLength - position)
                        return Malformed(prefix, out length);

                    position += bodyLength;
                    if (position >= prefix.Length)
                        return Need(position + 1, out length);
                }
            }
        }

        var onDisk = Next(prefix, ref position, out _, out length);
        if (onDisk != SevenBitStatus.Complete)
            return onDisk == SevenBitStatus.Incomplete ? Need(length, out length) : Malformed(prefix, out length);

        length = position;
        return true;

        static SevenBitStatus Next(ReadOnlySpan<byte> prefix, ref int position, out int value, out int need)
        {
            var status = WireReader.TryDecode7BitEncodedInt(prefix[position..], out value, out int used);
            need = 0;

            if (status == SevenBitStatus.Complete)
            {
                position += used;
                if (position > MaxLength)
                    return SevenBitStatus.Malformed;
            }
            else if (status == SevenBitStatus.Incomplete)
            {
                need = position + used + 1;
                if (need > MaxLength)
                    return SevenBitStatus.Malformed;
            }

            return status;
        }

        static bool Need(int required, out int length)
        {
            length = required;
            return false;
        }

        static bool Malformed(ReadOnlySpan<byte> prefix, out int length)
        {
            length = prefix.Length;
            return true;
        }
    }

    /// <summary>Writes the header, whose bytes are also the associated data of an encrypted frame.</summary>
    /// <exception cref="BinaryConfigurationException">A custom algorithm name or the key id cannot be represented.</exception>
    public void WriteTo(ref WireWriter writer)
    {
        writer.WriteInt32(BinaryFormatConstants.Magic);
        writer.Write7BitEncodedInt(Version);
        writer.Write7BitEncodedInt(PreserveReferences ? ReferencesMode : 0);

        int services = (ChecksumAlgorithm != ChecksumAlgorithm.None ? 1 : 0)
                       + (Compression != CompressionAlgorithm.None ? 1 : 0)
                       + (Encryption != EncryptionAlgorithm.None ? 1 : 0);
        writer.Write7BitEncodedInt(services);

        if (ChecksumAlgorithm != ChecksumAlgorithm.None)
        {
            if (Checksum.Length is < 1 or > byte.MaxValue)
                throw new BinaryConfigurationException(
                    $"Checksum length {Checksum.Length} cannot be represented by the V1 header: a " +
                    "checksum is between 1 and 255 bytes.");

            BeginService(ref writer, ChecksumService,
                AlgorithmLength((byte)ChecksumAlgorithm, CustomChecksumName, nameof(CustomChecksumName)) + Checksum.Length);
            WriteAlgorithm(ref writer, (byte)ChecksumAlgorithm, CustomChecksumName, nameof(CustomChecksumName));
            writer.Write(Checksum);
        }

        if (Compression != CompressionAlgorithm.None)
        {
            BeginService(ref writer, CompressionService,
                AlgorithmLength((byte)Compression, CustomCompressionName, nameof(CustomCompressionName))
                + SevenBitLength(UncompressedLength));
            WriteAlgorithm(ref writer, (byte)Compression, CustomCompressionName, nameof(CustomCompressionName));
            writer.Write7BitEncodedInt(UncompressedLength);
        }

        if (Encryption != EncryptionAlgorithm.None)
        {
            BeginService(ref writer, EncryptionService,
                AlgorithmLength((byte)Encryption, CustomEncryptionName, nameof(CustomEncryptionName))
                + OptionalStringLength(KeyId, nameof(KeyId)));
            WriteAlgorithm(ref writer, (byte)Encryption, CustomEncryptionName, nameof(CustomEncryptionName));
            writer.WriteOptionalString(KeyId, BinaryFormatConstants.MaxHeaderStringBytes, nameof(KeyId));
        }

        writer.Write7BitEncodedInt(OnDiskLength);
    }

    /// <summary>
    /// Reads and checks a header. The reader is over the frame's first bytes, at most
    /// <see cref="MaxLength"/> of them, so the header's end is where the reader stops.
    /// </summary>
    public static BinaryFormatHeaderV1 ReadFrom(ref WireReader reader)
    {
        int magic = reader.ReadInt32();
        if (magic != BinaryFormatConstants.Magic)
            throw new BinaryFormatException(
                "Not a recognized BinarySerializer stream (magic number mismatch).");

        int formatVersion = reader.Read7BitEncodedInt("format version");
        if (formatVersion != Version)
            throw new BinaryFormatNotSupportedException(
                $"Expected format version {Version}, but found {formatVersion}.");

        int mode = reader.Read7BitEncodedInt("payload mode");
        if ((mode & ~ReferencesMode) != 0)
            throw new BinaryFormatNotSupportedException(
                $"Payload mode {mode} sets a bit this build does not know; only bit 0 (references) is defined.");

        var header = new BinaryFormatHeaderV1(
            PreserveReferences: (mode & ReferencesMode) != 0,
            ChecksumAlgorithm.None, null, [],
            CompressionAlgorithm.None, null, 0,
            EncryptionAlgorithm.None, null, null,
            OnDiskLength: 0);

        int services = reader.Read7BitEncodedInt("service count");
        int previous = 0;

        for (int record = 0; record < services; record++)
        {
            int kind = reader.Read7BitEncodedInt("service kind");
            int number = kind >> 1;
            bool critical = (kind & 1) == 1;

            if (number == 0)
                throw new BinaryFormatException("Service number 0 is reserved and never names a service.");

            if (number == previous)
                throw new BinaryFormatException($"Service {number} appears more than once in the header.");

            if (number < previous)
                throw new BinaryFormatException(
                    $"Service {number} follows service {previous}; services must appear in ascending number.");

            previous = number;

            int length = reader.Read7BitEncodedInt($"service {number} length");
            long headerLeft = MaxLength - reader.Consumed;
            if (length > headerLeft)
                throw new BinaryFormatException(
                    $"Service {number} declares a body of {length} byte(s), which does not fit the " +
                    $"{headerLeft} byte(s) left of the {MaxLength}-byte header bound.");

            var body = reader.Slice(length, $"service {number} body");

            switch (number)
            {
                case ChecksumService:
                    RequireCritical(number, critical);
                    header = ReadChecksum(ref body, header);
                    break;
                case CompressionService:
                    RequireCritical(number, critical);
                    header = ReadCompression(ref body, header);
                    break;
                case EncryptionService:
                    RequireCritical(number, critical);
                    header = ReadEncryption(ref body, header);
                    break;
                default:
                    if (critical)
                        throw new BinaryFormatNotSupportedException(
                            $"Service {number} is critical and unknown to this build.");

                    body.Skip(body.Remaining, $"service {number} body");
                    break;
            }

            if (body.Remaining != 0)
                throw new BinaryFormatException(
                    $"Service {number} body holds {body.Remaining} byte(s) after its last field.");
        }

        int onDiskLength = reader.Read7BitEncodedInt("onDiskLength");
        header = header with { OnDiskLength = onDiskLength };

        header.CheckLengths(reader.State.Limits);
        return header;
    }

    /// <summary>
    /// The header's checks of its declared lengths against the limits, before anything is allocated
    /// for them. <see cref="OnDiskLength"/> is bounded by <c>MaxEncryptedBytes</c>; without encryption
    /// it is also the compressed payload, bounded by <c>MaxCompressedBytes</c>, and without compression
    /// as well the payload itself, bounded by <c>MaxPayloadBytes</c>. The uncompressed length is the
    /// one declared length no delivered bytes back, so it is bounded by <c>MaxPayloadBytes</c> and by
    /// the decompression ratio against <see cref="OnDiskLength"/> — exactly without encryption, and as
    /// an upper bound with it, since a ciphertext is never shorter than its plaintext.
    /// </summary>
    private void CheckLengths(SerializationLimits limits)
    {
        CheckLength(OnDiskLength, limits.MaxEncryptedBytes, nameof(OnDiskLength), nameof(limits.MaxEncryptedBytes));

        if (Encryption == EncryptionAlgorithm.None)
            CheckStored(OnDiskLength, nameof(OnDiskLength), limits);

        if (Compression == CompressionAlgorithm.None)
            return;

        CheckLength(UncompressedLength, limits.MaxPayloadBytes, nameof(UncompressedLength), nameof(limits.MaxPayloadBytes));
        CheckExpansion(UncompressedLength, OnDiskLength, limits.MaxDecompressionRatio);
    }

    /// <summary>
    /// Checks the plaintext of an encrypted frame once it is known, as the header checks the stored
    /// bytes of a frame without encryption: the compressed payload, bounded by
    /// <c>MaxCompressedBytes</c>, and without compression the payload itself, bounded by
    /// <c>MaxPayloadBytes</c>; with compression the ratio is enforced exactly against it. This runs
    /// before decompression allocates.
    /// </summary>
    /// <exception cref="BinaryLimitException">The plaintext exceeds its limit or the ratio.</exception>
    public void CheckPlaintext(int plaintextLength, SerializationLimits limits)
    {
        CheckStored(plaintextLength, "Plaintext length", limits);

        if (Compression != CompressionAlgorithm.None)
            CheckExpansion(UncompressedLength, plaintextLength, limits.MaxDecompressionRatio);
    }

    /// <summary>The bytes phases below encryption produced: the compressed payload, and without compression the payload.</summary>
    private void CheckStored(int length, string what, SerializationLimits limits)
    {
        CheckLength(length, limits.MaxCompressedBytes, what, nameof(limits.MaxCompressedBytes));

        if (Compression == CompressionAlgorithm.None)
            CheckLength(length, limits.MaxPayloadBytes, what, nameof(limits.MaxPayloadBytes));
    }

    /// <summary>The header as the public inspection surface reports it.</summary>
    public BinaryHeaderInfo ToInfo(int headerLength) =>
        new(Version,
            PreserveReferences,
            Compression, CustomCompressionName,
            Compression == CompressionAlgorithm.None ? null : UncompressedLength,
            ChecksumAlgorithm, CustomChecksumName,
            Checksum,
            Encryption, CustomEncryptionName, KeyId,
            headerLength,
            OnDiskLength);

    private static BinaryFormatHeaderV1 ReadChecksum(ref WireReader body, BinaryFormatHeaderV1 header)
    {
        var id = ReadId<ChecksumAlgorithm>(ref body, "checksum");
        string? name = ReadName(ref body, (byte)id, "checksum");

        long hashLength = body.Remaining;
        if (hashLength is < 1 or > byte.MaxValue)
            throw new BinaryFormatException(
                $"The checksum service holds a hash of {hashLength} byte(s); a checksum is between 1 and 255 bytes.");

        byte[] hash = body.ReadBytes((int)hashLength, "Checksum");
        return header with { ChecksumAlgorithm = id, CustomChecksumName = name, Checksum = hash };
    }

    private static BinaryFormatHeaderV1 ReadCompression(ref WireReader body, BinaryFormatHeaderV1 header)
    {
        var id = ReadId<CompressionAlgorithm>(ref body, "compression");
        string? name = ReadName(ref body, (byte)id, "compression");
        int uncompressedLength = body.Read7BitEncodedInt("uncompressedLength");

        return header with { Compression = id, CustomCompressionName = name, UncompressedLength = uncompressedLength };
    }

    private static BinaryFormatHeaderV1 ReadEncryption(ref WireReader body, BinaryFormatHeaderV1 header)
    {
        var id = ReadId<EncryptionAlgorithm>(ref body, "encryption");
        string? name = ReadName(ref body, (byte)id, "encryption");
        string? keyId = body.ReadOptionalString(BinaryFormatConstants.MaxHeaderStringBytes, nameof(KeyId));

        return header with { Encryption = id, CustomEncryptionName = name, KeyId = keyId };
    }

    private static void RequireCritical(int number, bool critical)
    {
        if (!critical)
            throw new BinaryFormatException(
                $"Service {number} is critical, but its record marks it as one that may be skipped.");
    }

    /// <summary>
    /// Reads an algorithm id. <c>None</c> is never recorded — an absent phase is an absent record — and
    /// an id this build does not define names an algorithm it cannot run.
    /// </summary>
    private static TEnum ReadId<TEnum>(ref WireReader body, string what) where TEnum : unmanaged, Enum
    {
        int raw = body.Read7BitEncodedInt($"{what} algorithm id");
        if (raw == 0)
            throw new BinaryFormatException($"The {what} service names no algorithm (id None).");

        if (raw > byte.MaxValue)
            throw new BinaryFormatNotSupportedException($"Unknown {what} algorithm: {raw}.");

        byte narrow = (byte)raw;
        var value = Unsafe.As<byte, TEnum>(ref narrow);
        if (!Enum.IsDefined(value))
            throw new BinaryFormatNotSupportedException($"Unknown {what} algorithm: {raw}.");

        return value;
    }

    /// <summary>Reads the name that follows a custom algorithm's id: 1 to 256 UTF-8 bytes.</summary>
    private static string? ReadName(ref WireReader body, byte id, string what)
    {
        if (id != CustomId)
            return null;

        string name = body.ReadString(BinaryFormatConstants.MaxHeaderStringBytes, $"Custom {what} name");
        if (name.Length == 0)
            throw new BinaryFormatException($"The custom {what} algorithm carries an empty name.");

        return name;
    }

    private static void BeginService(ref WireWriter writer, int number, int bodyLength)
    {
        writer.Write7BitEncodedInt((number << 1) | 1);
        writer.Write7BitEncodedInt(bodyLength);
    }

    private static void WriteAlgorithm(ref WireWriter writer, byte id, string? name, string what)
    {
        writer.Write7BitEncodedInt(id);
        if (id == CustomId)
            writer.WriteString(name!, BinaryFormatConstants.MaxHeaderStringBytes, what);
    }

    /// <summary>The bytes of an algorithm's id and, for a custom algorithm, its name.</summary>
    /// <exception cref="BinaryConfigurationException">A custom algorithm has no name, or one too long for the header.</exception>
    private static int AlgorithmLength(byte id, string? name, string what)
    {
        if (id != CustomId)
            return SevenBitLength(id);

        if (string.IsNullOrEmpty(name))
            throw new BinaryConfigurationException($"{what} is required for a custom algorithm and cannot be empty.");

        int nameBytes = HeaderStringBytes(name, what);
        return SevenBitLength(id) + SevenBitLength(nameBytes) + nameBytes;
    }

    private static int OptionalStringLength(string? value, string what)
    {
        if (value is null)
            return 1;

        int bytes = HeaderStringBytes(value, what);
        return SevenBitLength(bytes + 1) + bytes;
    }

    private static int HeaderStringBytes(string value, string what)
    {
        int bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes > BinaryFormatConstants.MaxHeaderStringBytes)
            throw new BinaryConfigurationException(
                $"{what} encodes to {bytes} byte(s), but this field admits at most " +
                $"{BinaryFormatConstants.MaxHeaderStringBytes}.");

        return bytes;
    }

    private static int SevenBitLength(int value)
    {
        int length = 1;
        for (uint remaining = (uint)value; remaining >= 0x80; remaining >>= 7)
            length++;

        return length;
    }

    /// <summary>
    /// Bounds the one declared length the wire cannot bound on its own. Every other phase length is
    /// backed by bytes that must physically arrive; the uncompressed length is not, because
    /// compression may legitimately expand. Relating it to the compressed bytes that do arrive is
    /// what keeps the decompression buffer proportional to the payload actually delivered.
    /// </summary>
    private static void CheckExpansion(int uncompressedLength, int compressedLength, int maximumRatio)
    {
        long ceiling = (long)compressedLength * maximumRatio;
        if (uncompressedLength > ceiling)
            throw new BinaryLimitException(
                $"{nameof(UncompressedLength)} {uncompressedLength} exceeds {compressedLength} " +
                $"compressed byte(s) by more than the configured factor of {maximumRatio} " +
                $"({nameof(SerializationLimits.MaxDecompressionRatio)}).");
    }

    private static void CheckLength(int value, long maximum, string what, string limit)
    {
        if (value > maximum)
            throw new BinaryLimitException(
                $"{what} {value} exceeds the configured maximum of {maximum} ({limit}).");
    }
}
