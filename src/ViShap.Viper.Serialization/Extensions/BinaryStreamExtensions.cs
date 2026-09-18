namespace ViShap.Viper.Extensions;

internal static class BinaryStreamExtensions
{
    public static void WriteOptionalString(this BinaryWriter writer, string? value)
    {
        bool hasValue = !string.IsNullOrEmpty(value);
        writer.Write(hasValue);
        if (hasValue)
            writer.Write(value!);
    }

    public static void WriteOptionalString(
        this BinaryWriter writer,
        string? value,
        SerializationLimits limits,
        string what)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        limits.Validate();

        bool hasValue = !string.IsNullOrEmpty(value);
        writer.Write(hasValue);
        if (!hasValue)
            return;

        int byteLength = System.Text.Encoding.UTF8.GetByteCount(value!);
        if (byteLength > limits.MaxStringBytes)
            throw new BinaryLimitException(
                $"{what} byte length {byteLength} exceeds the configured maximum of {limits.MaxStringBytes}.");

        writer.Write(value!);
    }

    public static string? ReadOptionalString(
        this BinaryReader reader,
        SerializationLimits limits,
        string what)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        limits.Validate();

        bool hasValue = reader.ReadBoolean();
        if (!hasValue)
            return null;

        int byteLength = DeserializationGuard.ReadBounded7BitEncodedInt(
            reader,
            $"{what} length");

        if (byteLength > limits.MaxStringBytes)
            throw new BinaryLimitException(
                $"{what} byte length {byteLength} exceeds the configured maximum of {limits.MaxStringBytes}.");

        byte[] bytes = DeserializationGuard.ReadExactly(
            reader.BaseStream,
            byteLength,
            what);

        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}