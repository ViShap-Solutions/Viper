using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// What a §18.1 microbenchmark needs to drive one internal mechanism: an operation that carries a
/// policy, and payload buffers sized once so a timed method measures the mechanism rather than a
/// buffer growing under it.
/// </summary>
internal static class ComponentFixtures
{
    /// <summary>An operation under the shipped default policy.</summary>
    internal static SerializationOperation Operation() =>
        new(SerializationLimits.Default, keys: null, preserveReferences: false,
            requireEncryption: false, requireChecksum: false);

    /// <summary>
    /// An operation whose cumulative ceilings a hot loop cannot reach. The accounting path is the one
    /// the default policy takes — a single comparison against the ceiling — so what a suite built on
    /// this measures is the cost of charging a budget, not the cost of a particular ceiling.
    /// </summary>
    internal static SerializationOperation UnboundedTotals() =>
        new(
            SerializationLimits.Default with
            {
                MaxTotalElements = long.MaxValue,
                MaxObjectGraphNodes = long.MaxValue,
                MaxTotalKeyedFields = long.MaxValue,
            },
            keys: null,
            preserveReferences: false,
            requireEncryption: false,
            requireChecksum: false);

    /// <summary>The bytes one writer call sequence produced, for a reader to be pointed at.</summary>
    internal static byte[] Encode(SerializationOperation operation, Action<ValueWriter> write)
    {
        using var buffer = new MemoryStream(4096);
        write(new ValueWriter(buffer, operation));
        return buffer.ToArray();
    }

    /// <summary>A reader over a fixed payload, rewound by the benchmark before every invocation.</summary>
    internal static (MemoryStream Source, ValueReader Reader) Decoder(
        SerializationOperation operation,
        byte[] payload)
    {
        var source = new MemoryStream(payload, writable: false);
        return (source, new ValueReader(source, operation));
    }
}
