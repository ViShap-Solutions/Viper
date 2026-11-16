using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile.Fuzz;

/// <summary>
/// The models a fuzzed frame is read as, each with the valid frames its mutations start from: every
/// frozen fixture under the options it was written with, and the phases a fixture does not carry —
/// a checksum, Deflate and Brotli — over the same values.
/// </summary>
internal static class FuzzCatalog
{
    private static readonly BinarySerializerOptions Plain = BinarySerializerOptions.Default;

    private static readonly BinarySerializerOptions References =
        BinarySerializerOptions.Configure().PreserveReferences().Build();

    private static readonly BinarySerializerOptions Headerless =
        BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build();

    private static readonly BinarySerializerOptions Checksummed =
        BinarySerializerOptions.Configure().WithChecksum(new Crc32Checksum()).Build();

    private static readonly BinarySerializerOptions ChecksummedReferences =
        BinarySerializerOptions.Configure().WithChecksum(new Crc32Checksum()).PreserveReferences().Build();

    private static readonly BinarySerializerOptions Deflated =
        BinarySerializerOptions.Configure().WithCompression(new DeflateCompression()).Build();

    private static readonly BinarySerializerOptions Brotli =
        BinarySerializerOptions.Configure().WithCompression(new BrotliCompression()).Build();

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        Fixture<FrozenPrimitives>("primitives", Plain, "v1-primitives.bin"),
        Fixture<FrozenPrimitives>("primitives-v0", Headerless, "v0-primitives.bin", headerless: true),
        Fixture<FrozenTimeAndSystem>("time-system", Plain, "v1-time-system.bin", normalizes: true),
        Fixture<FrozenNumerics>("numerics", Plain, "v1-numerics.bin"),
        Fixture<FrozenCollections>("collections", Plain, "v1-collections.bin", normalizes: true),
        Fixture<FrozenComposites>("composites", Plain, "v1-composites.bin"),
        Fixture<FrozenKeyed>("keyed", Plain, "v1-keyed.bin"),
        new FuzzTarget<FrozenShape>("union", Plain, Plain, References, null, _ => [Wire.Fixture("v1-union.bin")]) { Canonical = true, Bounded = true },
        Fixture<FrozenGraph>("references", References, "v1-references.bin"),
        Fixture<FrozenAbsences>("absences", Plain, "v1-absences.bin"),
        Rewritten<FrozenPrimitives>("primitives-crc32", Checksummed, Plain, "v1-primitives.bin", canonical: true),
        Rewritten<FrozenCollections>("collections-deflate", Deflated, Plain, "v1-collections.bin", canonical: false),
        Rewritten<FrozenKeyed>("keyed-brotli", Brotli, Plain, "v1-keyed.bin", canonical: false),
        new FuzzTarget<FrozenGraph>("deep-graph", References, Plain, References, () => new FrozenGraph(), serializer => [serializer.Serialize(DeepGraph(40))])
        {
            Canonical = true,
            Bounded = true,
        },
    ];

    /// <summary>The target named <paramref name="name"/>.</summary>
    public static FuzzTarget Named(string name) =>
        All.FirstOrDefault(target => target.Name == name) ?? throw new ArgumentException($"No fuzz target '{name}'.", nameof(name));

    private static FuzzTarget Fixture<T>(
        string name, BinarySerializerOptions options, string file, bool headerless = false, bool normalizes = false)
        where T : class, new() =>
        new FuzzTarget<T>(name, options, Untwin(options), Twin(options), () => new T(), _ => [Wire.Fixture(file)])
        {
            Canonical = true,
            Bounded = true,
            Headerless = headerless,
            Normalizes = normalizes,
        };

    private static FuzzTarget Rewritten<T>(string name, BinarySerializerOptions options, BinarySerializerOptions fixtureOptions, string file, bool canonical)
        where T : class, new() =>
        new FuzzTarget<T>(name, options, Untwin(options), Twin(options), () => new T(), serializer =>
            [serializer.Serialize(new BinarySerializer(fixtureOptions).Deserialize<T>(Wire.Fixture(file)))])
        {
            Canonical = canonical,
            Bounded = canonical,
        };

    /// <summary>The options a frame whose header asks for reference framing is written back with.</summary>
    private static BinarySerializerOptions Twin(BinarySerializerOptions options) =>
        options == Checksummed ? ChecksummedReferences : options == Headerless ? Headerless : References;


    /// <summary>The options a frame whose header asks for no reference framing is written back with.</summary>
    private static BinarySerializerOptions Untwin(BinarySerializerOptions options) =>
        options == References ? Plain : options;

    private static FrozenGraph DeepGraph(int depth)
    {
        var first = new FrozenNode { Value = 0 };
        var node = first;
        for (int i = 1; i < depth; i++)
        {
            node.Next = new FrozenNode { Value = i };
            node = node.Next;
        }

        node.Next = first;
        return new FrozenGraph { Left = first, Right = node };
    }
}
