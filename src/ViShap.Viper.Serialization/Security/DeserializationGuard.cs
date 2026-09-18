using System.Buffers;
using System.Collections;

namespace ViShap.Viper.Security;

internal static class DeserializationGuard
{
    private const int IoChunkSize = 64 * 1024;
    private const int ArrayCapacityHint = 1024;

    public static int ValidateCount(
        BinaryPayloadReader reader,
        int rawCount,
        int maxCount,
        string what)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (rawCount < 0)
            throw new BinaryFormatException($"{what} {rawCount} must be non-negative.");

        if (rawCount > maxCount)
            throw new BinaryLimitException(
                $"{what} {rawCount} exceeds the configured maximum of {maxCount}.");

        reader.Budget.ConsumeElements(rawCount);
        return rawCount;
    }

    public static void ValidateLength(int rawLength, long maxLength, string what)
    {
        if (rawLength < 0)
            throw new BinaryFormatException($"{what} {rawLength} must be non-negative.");

        if (rawLength > maxLength)
            throw new BinaryLimitException(
                $"{what} {rawLength} exceeds the configured maximum of {maxLength}.");
    }

    public static long ValidateTotalElements(
        BinaryPayloadReader reader,
        int[] lengths,
        long maxCount,
        string what)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(lengths);

        long total = 1;
        foreach (int length in lengths)
        {
            if (length < 0)
                throw new BinaryFormatException(
                    $"{what}: a dimension length {length} must be non-negative.");

            if (length == 0)
            {
                total = 0;
                continue;
            }

            if (total > maxCount / length)
                throw new BinaryLimitException(
                    $"{what}: total element count exceeds the configured maximum of {maxCount}.");

            total *= length;
        }

        if (total > maxCount)
            throw new BinaryLimitException(
                $"{what}: total element count {total} exceeds the configured maximum of {maxCount}.");

        reader.Budget.ConsumeElements(total);
        return total;
    }

    public static byte[] ReadValidatedBytes(
        BinaryPayloadReader reader,
        int rawLength,
        long maxLength,
        string what)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ValidateLength(rawLength, maxLength, what);
        return ReadExactly(reader.RawReader.BaseStream, rawLength, what);
    }

    public static void SkipBytes(Stream stream, long length, string what)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        if (length < 0)
            throw new BinaryFormatException($"{what} {length} must be non-negative.");
        if (length == 0)
            return;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(IoChunkSize);
        try
        {
            long remaining = length;
            while (remaining > 0)
            {
                int request = (int)Math.Min(buffer.Length, remaining);
                int read = stream.Read(buffer, 0, request);
                if (read == 0)
                    throw new BinaryFormatException(
                        $"{what} ended early. Expected {length} bytes, got {length - remaining}.");

                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    public static string ReadString(BinaryPayloadReader reader, long maxLength, string what)
    {
        int byteLength = ReadBounded7BitEncodedInt(
            reader.RawReader,
            $"{what} length");

        byte[] bytes = ReadValidatedBytes(
            reader,
            byteLength,
            maxLength,
            what);

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public static int ReadBounded7BitEncodedInt(BinaryReader reader, string what)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        uint result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte current;
            try
            {
                current = reader.ReadByte();
            }
            catch (EndOfStreamException ex)
            {
                throw new BinaryFormatException(
                    $"Malformed {what} 7-bit integer: truncated encoding.", ex);
            }

            if (shift == 28 && (current & 0xF0) != 0)
                throw new BinaryFormatException($"Malformed {what} 7-bit integer.");

            result |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                if (result > int.MaxValue)
                    throw new BinaryFormatException(
                        $"{what} value {result} is outside the supported non-negative Int32 range.");

                return (int)result;
            }
        }

        throw new BinaryFormatException($"Malformed {what} 7-bit integer.");
    }

    public static Array ReadIntoArray(
        BinaryPayloadReader reader,
        Type elementType,
        int validatedCount)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(elementType);

        if (validatedCount < 0)
            throw new BinaryFormatException(
                $"Array length {validatedCount} must be non-negative.");

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)ActivatorCache.GetOneArgConstructor(
            listType,
            typeof(int))(Math.Min(validatedCount, ArrayCapacityHint));

        for (int i = 0; i < validatedCount; i++)
            list.Add(reader.ReadElement(elementType));

        var array = Array.CreateInstance(elementType, list.Count);
        list.CopyTo(array, 0);
        return array;
    }

    public static T[] ReadIntoArray<T>(BinaryPayloadReader reader, int validatedCount)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (validatedCount < 0)
            throw new BinaryFormatException(
                $"Array length {validatedCount} must be non-negative.");

        var list = new List<T>(Math.Min(validatedCount, ArrayCapacityHint));
        for (int i = 0; i < validatedCount; i++)
            list.Add((T)reader.ReadElement(typeof(T))!);

        return list.ToArray();
    }

    public static int ValidateBitCount(int rawBitCount, long maxByteLength, string what)
    {
        if (rawBitCount < 0)
            throw new BinaryFormatException(
                $"{what} {rawBitCount} must be non-negative.");

        if (rawBitCount > maxByteLength * 8L)
            throw new BinaryLimitException(
                $"{what} {rawBitCount} exceeds the configured maximum.");

        return rawBitCount;
    }

    internal static byte[] ReadExactly(Stream stream, int length, string what)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        if (length < 0)
            throw new BinaryFormatException($"{what} {length} must be non-negative.");
        if (length == 0)
            return Array.Empty<byte>();

        byte[] result = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int request = Math.Min(IoChunkSize, length - offset);
            int read = stream.Read(result, offset, request);
            if (read == 0)
            {
                Array.Clear(result);
                throw new BinaryFormatException(
                    $"{what} ended early. Expected {length} bytes, got {offset}.");
            }

            offset += read;
        }

        return result;
    }
}