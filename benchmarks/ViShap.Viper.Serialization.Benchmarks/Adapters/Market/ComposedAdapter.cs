using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// A composed baseline of Benchmark-Plan §17: what an application builds around a serializer that has no
/// envelope of its own — the library's payload, CRC-32 over it, optionally Brotli, AES-256-GCM, and a
/// hand-written header carrying what the V1 header carries for the same configuration (COMP-02). Each
/// part is the platform's own and used in its fastest documented form, so the baseline is the strongest
/// honest version of the alternative, not a strawman.
/// </summary>
/// <remarks>
/// <para>The frame, in the order the bytes appear:</para>
/// <code>
/// magic 4 · version varint · flags varint (bit 0: compressed) · crc32 4 ·
/// [uncompressedLength varint] · keyId (varint length + 1, bytes) · onDiskLength varint ·
/// nonce 12 · ciphertext · tag 16
/// </code>
/// <para>
/// The order of the phases is the contract's: checksum over the serialized payload, compress, encrypt
/// (Contract §10.1, SEC-07). What it deliberately does not do, and the report says so (COMP-04): the header
/// is not associated data, so it is not authenticated; it names no algorithm, so nothing can be negotiated;
/// and it enforces no limit or budget on what it reads beyond the lengths it declares.
/// </para>
/// <para>
/// Viper's phases are matched where they are configurable: Brotli at quality 4 with a 4 MiB window,
/// which is <c>BrotliCompression</c>'s default, and the fixed key and key id of the encrypted profiles. The
/// composed baseline then takes every advantage an application may take: the one-shot Brotli calls, one
/// <see cref="AesGcm"/> kept for the adapter's life, one reused buffer for the library's payload, and
/// pooled intermediates — so the only allocation per write is the frame it returns.
/// </para>
/// </remarks>
internal sealed class ComposedAdapter : MarketAdapter
{
    private const uint Magic = 0x504D4F43;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int BrotliQuality = 4;
    private const int BrotliWindow = 22;

    private static readonly byte[] KeyId = Encoding.UTF8.GetBytes(ViperProfiles.KeyId);

    private readonly MarketAdapter _inner;
    private readonly IBufferWriterSerializer _writer;
    private readonly ISpanDeserializer? _span;
    private readonly AesGcm _aes = new(ViperProfiles.Key, TagSize);
    private readonly ArrayBufferWriter<byte> _payload = new(64 * 1024);

    private ComposedAdapter(MarketAdapter inner, bool compressed)
        : base(inner.Library, inner.Version, ConfigurationName(inner, compressed), inner.Family, Tier.T4)
    {
        _inner = inner;
        _writer = inner as IBufferWriterSerializer
            ?? throw new ArgumentException($"{inner.Name} has no buffer entry point to compose over.", nameof(inner));
        _span = inner as ISpanDeserializer;
        Compressed = compressed;
    }

    /// <summary>The configurations §17 names: MemoryPack, MessagePack-CSharp and protobuf-net, each with and without Brotli.</summary>
    internal static IEnumerable<ComposedAdapter> Over(IEnumerable<MarketAdapter> inners) =>
        inners.SelectMany(inner => new[] { new ComposedAdapter(inner, compressed: true), new ComposedAdapter(inner, compressed: false) });

    internal override Rationale Rationale => new(
        $"Composed over {_inner.Name}: its own best mode (above), then System.IO.Hashing.Crc32, " +
        (Compressed ? "BrotliEncoder.TryCompress, " : string.Empty) +
        "and System.Security.Cryptography.AesGcm, behind a hand-written header",
        (Compressed ? $"Brotli quality {BrotliQuality}, window {BrotliWindow} — BrotliCompression's defaults; " : string.Empty) +
        $"AES-256-GCM with a 12-byte random nonce and a 16-byte tag, the fixed key and key id \"{ViperProfiles.KeyId}\" of the encrypted profiles",
        "The header is not associated data, names no algorithm and is held to no limit: an application that adds these pays for them, and the comparison says that it would have to",
        "None known: one-shot Brotli, one AesGcm for the adapter's life, a reused payload buffer and pooled intermediates are the fastest forms the platform offers");

    public override byte[] Serialize<T>(T value)
    {
        _payload.ResetWrittenCount();
        _writer.Serialize(_payload, value);
        var payload = _payload.WrittenSpan;

        uint crc = Crc32.HashToUInt32(payload);
        byte[]? rented = null;

        try
        {
            var plaintext = payload;

            if (Compressed)
            {
                rented = ArrayPool<byte>.Shared.Rent(BrotliEncoder.GetMaxCompressedLength(payload.Length));

                if (!BrotliEncoder.TryCompress(payload, rented, out int written, BrotliQuality, BrotliWindow))
                    throw new InvalidOperationException("Brotli did not fit its own maximum compressed length.");

                plaintext = rented.AsSpan(0, written);
            }

            int onDisk = NonceSize + plaintext.Length + TagSize;
            Span<byte> header = stackalloc byte[64];
            int length = Header(header, Compressed, crc, payload.Length, onDisk);

            var frame = new byte[length + onDisk];
            header[..length].CopyTo(frame);

            var body = frame.AsSpan(length);
            var nonce = body[..NonceSize];
            RandomNumberGenerator.Fill(nonce);
            _aes.Encrypt(nonce, plaintext, body.Slice(NonceSize, plaintext.Length), body.Slice(NonceSize + plaintext.Length, TagSize));

            return frame;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public override T? Deserialize<T>(byte[] frame) where T : default
    {
        var reader = new SpanReader(frame);

        if (reader.UInt32() != Magic || reader.VarInt() != 1)
            throw new InvalidDataException("Not a composed frame of version 1.");

        bool compressed = (reader.VarInt() & 1) != 0;
        uint crc = reader.UInt32();
        int uncompressed = compressed ? checked((int)reader.VarInt()) : 0;
        reader.Skip(checked((int)reader.VarInt() - 1));
        int onDisk = checked((int)reader.VarInt());

        if (onDisk != reader.Remaining || onDisk < NonceSize + TagSize)
            throw new InvalidDataException("The declared length is not the length of the body.");

        var body = frame.AsSpan(reader.Position);
        int plaintextLength = onDisk - NonceSize - TagSize;
        var plaintext = ArrayPool<byte>.Shared.Rent(plaintextLength);
        byte[]? expanded = null;

        try
        {
            _aes.Decrypt(body[..NonceSize], body.Slice(NonceSize, plaintextLength), body.Slice(NonceSize + plaintextLength, TagSize), plaintext.AsSpan(0, plaintextLength));
            var payload = plaintext.AsSpan(0, plaintextLength);

            if (compressed)
            {
                expanded = ArrayPool<byte>.Shared.Rent(uncompressed);

                if (!BrotliDecoder.TryDecompress(payload, expanded.AsSpan(0, uncompressed), out int written) || written != uncompressed)
                    throw new InvalidDataException("The payload does not decompress to its declared length.");

                payload = expanded.AsSpan(0, uncompressed);
            }

            if (Crc32.HashToUInt32(payload) != crc)
                throw new InvalidDataException("The checksum does not match.");

            return _span is not null
                ? _span.Deserialize<T>(payload)
                : _writer.Deserialize<T>(new ReadOnlySequence<byte>(compressed ? expanded! : plaintext, 0, payload.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(plaintext);

            if (expanded is not null)
                ArrayPool<byte>.Shared.Return(expanded);
        }
    }

    public override void Serialize<T>(Stream destination, T value) => destination.Write(Serialize(value));

    public override T? Deserialize<T>(Stream source) where T : default
    {
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        return Deserialize<T>(copy.ToArray());
    }

    /// <summary>A composed baseline is a comparison point, not a library: it is never probed or placed.</summary>
    internal override ProbeOutcome SelfDescription() =>
        ProbeOutcome.Unsupported("a composed baseline is not probed: it stands for what an application writes around its serializer");

    private static string ConfigurationName(MarketAdapter inner, bool compressed) =>
        $"{inner.Configuration} composed {(compressed ? "Brotli+" : string.Empty)}CRC-32+AES-256-GCM";

    private static int Header(Span<byte> header, bool compressed, uint crc, int uncompressed, int onDisk)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        int position = 4;

        position += VarInt(header[position..], 1);
        position += VarInt(header[position..], compressed ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(header[position..], crc);
        position += 4;

        if (compressed)
            position += VarInt(header[position..], (uint)uncompressed);

        position += VarInt(header[position..], (uint)KeyId.Length + 1);
        KeyId.CopyTo(header[position..]);
        position += KeyId.Length;

        position += VarInt(header[position..], (uint)onDisk);
        return position;
    }

    private static int VarInt(Span<byte> destination, uint value)
    {
        int written = 0;

        while (value >= 0x80)
        {
            destination[written++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[written++] = (byte)value;
        return written;
    }

    /// <summary>The header's reader: a position over the frame and the three field kinds it holds.</summary>
    private ref struct SpanReader(ReadOnlySpan<byte> source)
    {
        private readonly ReadOnlySpan<byte> _source = source;

        internal int Position { get; private set; }

        internal readonly int Remaining => _source.Length - Position;

        internal uint UInt32()
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_source[Position..]);
            Position += 4;
            return value;
        }

        internal uint VarInt()
        {
            uint value = 0;

            for (int shift = 0; shift < 35; shift += 7)
            {
                byte next = _source[Position++];
                value |= (uint)(next & 0x7F) << shift;

                if (next < 0x80)
                    return value;
            }

            throw new InvalidDataException("A varint is longer than five bytes.");
        }

        internal void Skip(int count) => Position += count;
    }
}
