namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Hand-builds V1 frames so a test can present a payload no writer would ever produce.
/// The layout mirrors <c>BinaryFormatHeaderV1</c>.
/// </summary>
internal static class Wire
{
    public const int Magic = 0x52455342;

    public static byte[] Payload(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        write(writer);
        writer.Flush();
        return buffer.ToArray();
    }

    /// <summary>Wraps <paramref name="body"/> in a V1 frame, optionally lying about the lengths.</summary>
    public static byte[] Frame(byte[] body, int? declaredLength = null, bool preserveReferences = false)
    {
        int length = declaredLength ?? body.Length;

        return Payload(writer =>
        {
            writer.Write(Magic);
            writer.Write(1);
            writer.Write((byte)0); writer.Write(false);   // compression + custom name
            writer.Write((byte)0); writer.Write(false);   // checksum + custom name
            writer.Write((byte)0); writer.Write(false);   // encryption + custom name
            writer.Write(false);                          // key id
            writer.Write(preserveReferences);
            writer.Write(length);                         // uncompressed
            writer.Write(length);                         // compressed
            writer.Write(length);                         // on disk
            writer.Write((byte)0);                        // checksum length
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
}
