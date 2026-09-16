namespace ViShap.Viper.Security;

internal static class DeserializationGuard
{
    public static int ValidateCount(
        BinaryPayloadReader reader,
        int rawCount,
        int maxCount,
        string what)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (rawCount < 0)
            throw new BinaryFormatException(
                $"{what} {rawCount} must be non-negative.");

        if (rawCount > maxCount)
            throw new BinaryFormatException(
                $"{what} {rawCount} exceeds the configured maximum of {maxCount}.");

        reader.Budget.ConsumeElements(rawCount);

        return rawCount;
    }

    public static void ValidateLength(
        int rawLength,
        long maxLength,
        string what)
    {
        if (rawLength < 0)
            throw new BinaryFormatException(
                $"{what} {rawLength} must be non-negative.");

        if (rawLength > maxLength)
            throw new BinaryFormatException(
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
                throw new BinaryFormatException(
                    $"{what}: total element count exceeds the configured maximum of {maxCount}.");

            total *= length;
        }

        if (total > maxCount)
            throw new BinaryFormatException(
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
        ValidateLength(rawLength, maxLength, what);

        reader.Budget.ConsumeBytes(rawLength);

        byte[] bytes = reader.RawReader.ReadBytes(rawLength);

        if (bytes.Length != rawLength)
            throw new BinaryFormatException(
                $"{what}: expected {rawLength} bytes, got {bytes.Length}.");

        return bytes;
    }

    public static string ReadString(
        BinaryPayloadReader reader,
        long maxLength,
        string what)
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
            catch (EndOfStreamException)
            {
                throw new BinaryFormatException(
                    $"Malformed {what} 7-bit integer: truncated encoding.");
            }

            if (shift == 28 && (current & 0xF0) != 0)
            {
                throw new BinaryFormatException(
                    $"Malformed {what} 7-bit integer.");
            }

            result |= (uint)(current & 0x7F) << shift;

            if ((current & 0x80) == 0)
            {
                if (result > int.MaxValue)
                {
                    throw new BinaryFormatException(
                        $"{what} value {result} is outside the supported non-negative Int32 range.");
                }

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
        var array = Array.CreateInstance(elementType, validatedCount);

        for (int i = 0; i < validatedCount; i++)
        {
            array.SetValue(
                reader.ReadElement(elementType),
                i);
        }

        return array;
    }

    public static T[] ReadIntoArray<T>(
        BinaryPayloadReader reader,
        int validatedCount)
    {
        var array = new T[validatedCount];

        for (int i = 0; i < validatedCount; i++)
        {
            array[i] = (T)reader.ReadElement(typeof(T))!;
        }

        return array;
    }

    public static int ValidateBitCount(
        int rawBitCount,
        long maxByteLength,
        string what)
    {
        if (rawBitCount < 0)
            throw new BinaryFormatException(
                $"{what} {rawBitCount} must be non-negative.");

        if (rawBitCount > maxByteLength * 8L)
            throw new BinaryFormatException(
                $"{what} {rawBitCount} exceeds the configured maximum.");

        return rawBitCount;
    }
}