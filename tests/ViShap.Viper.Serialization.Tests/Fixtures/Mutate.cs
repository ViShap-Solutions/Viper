namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Byte-level edits applied to a valid payload so a test can present one that is wrong in exactly one
/// way. Every helper copies its input, so a corpus can be derived from one frame.
/// </summary>
internal static class Mutate
{
    /// <summary>Returns a copy with the byte at <paramref name="offset"/> inverted.</summary>
    public static byte[] FlipByte(byte[] payload, int offset)
    {
        byte[] copy = (byte[])payload.Clone();
        copy[offset] ^= 0xFF;
        return copy;
    }

    /// <summary>Returns a copy with the byte at <paramref name="offset"/> set to <paramref name="value"/>.</summary>
    public static byte[] SetByte(byte[] payload, int offset, byte value)
    {
        byte[] copy = (byte[])payload.Clone();
        copy[offset] = value;
        return copy;
    }

    /// <summary>Returns a copy with the little-endian <c>int32</c> at <paramref name="offset"/> replaced.</summary>
    public static byte[] SetInt32(byte[] payload, int offset, int value)
    {
        byte[] copy = (byte[])payload.Clone();
        BitConverter.GetBytes(value).CopyTo(copy, offset);
        return copy;
    }

    /// <summary>Returns the first <paramref name="length"/> bytes.</summary>
    public static byte[] Truncate(byte[] payload, int length) => payload[..length];

    /// <summary>
    /// Every non-empty proper prefix, shortest first. A truncation suite asserts that each one fails
    /// deterministically rather than picking a single arbitrary cut.
    /// </summary>
    public static IEnumerable<byte[]> Prefixes(byte[] payload)
    {
        for (int length = 1; length < payload.Length; length++)
            yield return Truncate(payload, length);
    }

    /// <summary>Encodes <paramref name="value"/> the way the wire encodes a 7-bit integer.</summary>
    public static byte[] SevenBitEncoded(int value)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write7BitEncodedInt(value);
        writer.Flush();
        return buffer.ToArray();
    }
}
