namespace ViShap.Viper.Extensions;

internal static class BinaryStreamExtensions
{
    public static void WriteOptionalString(this BinaryWriter writer, string? value)
    {
        bool hasValue = !string.IsNullOrEmpty(value);
        writer.Write(hasValue);
        if (hasValue) writer.Write(value!);
    }

    public static string? ReadOptionalString(this BinaryReader reader) =>
        reader.ReadBoolean() ? reader.ReadString() : null;
}