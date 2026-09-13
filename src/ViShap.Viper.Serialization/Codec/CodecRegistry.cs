using System.Text;

namespace ViShap.Viper.Codec;

internal static class CodecRegistry
{
    private static readonly Dictionary<int, IFormatCodecFactory> Factories = new();
    private static readonly Dictionary<int, IHeaderCodec> HeaderCodecs = new();

    static CodecRegistry()
    {
        Register(new V0FormatCodecFactory(), null);
        Register(new V1FormatCodecFactory(), new V1HeaderCodec());
    }

    public static void Register(IFormatCodecFactory factory, IHeaderCodec? headerCodec)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Factories[factory.Version] = factory;
        if (headerCodec != null)
        {
            HeaderCodecs[headerCodec.Version] = headerCodec;
        }
    }

    public static IEnumerable<IFormatCodec> CreateCodecs(BinarySerializerOptions options)
    {
        foreach (var (version, factory) in Factories)
        {
            if (version == 0 && !options.AllowV0Fallback)
                continue;

            yield return factory.Create(options);
        }
    }

    public static BinaryHeaderInfo? Inspect(Stream source, int version)
    {
        if (!HeaderCodecs.TryGetValue(version, out var headerCodec))
            return null;

        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        return headerCodec.ReadHeaderInfo(reader);
    }
}