namespace ViShap.Viper.Extensions;

internal static class BinaryStreamExtensions
{
    public static void WriteOptionalString(this BinaryWriter writer, string? value)
    {
        bool hasValue = !string.IsNullOrEmpty(value);
        writer.Write(hasValue);
        if (hasValue) writer.Write(value!);
    }

    public static string? ReadOptionalString(
        this BinaryReader reader,
        DeserializationLimits limits,
        string what)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);

        limits.Validate();

        bool hasValue = reader.ReadBoolean();

        if (!hasValue)
            return null;

        int byteLength = reader.Read7BitEncodedInt();

        if (byteLength < 0)
            throw new BinaryFormatException(
                $"{what} length {byteLength} must be non-negative.");

        if (byteLength > limits.MaxStringLength)
            throw new BinaryFormatException(
                $"{what} length {byteLength} exceeds the configured maximum of {limits.MaxStringLength}.");

        byte[] bytes = reader.ReadBytes(byteLength);

        if (bytes.Length != byteLength)
            throw new BinaryFormatException(
                $"{what}: expected {byteLength} bytes, got {bytes.Length}.");

        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}