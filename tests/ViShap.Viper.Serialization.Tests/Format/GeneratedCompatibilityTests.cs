using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins GEN-07: the frozen payloads of <c>Fixtures/Wire</c> read through the contracts the source
/// generator writes for their shapes — with reflection refused, so no type escapes to it — decode to
/// what the reflected contracts decode, and write back to exactly the frozen bytes.
/// </summary>
public class GeneratedCompatibilityTests
{
    private static BinarySerializer Generated(Func<BinarySerializerOptionsBuilder, BinarySerializerOptionsBuilder>? configure = null)
    {
        var builder = BinarySerializerOptions.Configure()
            .WithContracts(GeneratedCompatibilityContracts.Default)
            .RequireGeneratedContracts();

        return new BinarySerializer((configure?.Invoke(builder) ?? builder).Build());
    }

    [Fact]
    public void V1Fixtures_ReadAndWrittenThroughGeneratedContracts_ReproduceTheFrozenBytes()
    {
        var generated = Generated();

        AssertReproduced<FrozenPrimitives>("v1-primitives.bin", generated, CompatibilityTests.Plain);
        AssertReproduced<FrozenTimeAndSystem>("v1-time-system.bin", generated, CompatibilityTests.Plain);
        AssertReproduced<FrozenNumerics>("v1-numerics.bin", generated, CompatibilityTests.Plain);
        AssertReproduced<FrozenCollections>("v1-collections.bin", generated, CompatibilityTests.Plain);
        AssertReproduced<FrozenComposites>("v1-composites.bin", generated, CompatibilityTests.Plain);
        AssertReproduced<FrozenKeyed>("v1-keyed.bin", generated, CompatibilityTests.Plain);
        AssertReproduced<FrozenShape>("v1-union.bin", generated, CompatibilityTests.Plain);
        AssertReproduced<FrozenAbsences>("v1-absences.bin", generated, CompatibilityTests.Plain);
    }

    [Fact]
    public void V1References_ReadAndWrittenThroughGeneratedContracts_ReproduceTheFrozenBytes()
    {
        var generated = Generated(builder => builder.PreserveReferences());
        var restored = AssertReproduced<FrozenGraph>("v1-references.bin", generated, CompatibilityTests.Referencing);

        Assert.Same(restored.Left, restored.Right);
        Assert.Same(restored.Left, restored.Left!.Next);
    }

    [Fact]
    public void V0Primitives_ReadAndWrittenThroughGeneratedContracts_ReproduceTheFrozenBytes()
    {
        var generated = Generated(builder => builder.WithVersion(0).AllowV0Fallback());

        AssertReproduced<FrozenPrimitives>("v0-primitives.bin", generated, CompatibilityTests.Headerless);
    }

    [Fact]
    public void V1Protected_DecryptedThroughGeneratedContracts_HoldsWhatReflectionReads()
    {
        // The cipher's nonce is fresh on every write, so the protected frame cannot be reproduced;
        // its payload is compared instead, written plainly by the same reflected writer.
        var generated = Generated(builder => builder
            .WithCompression(new BrotliCompression())
            .WithChecksum(new Crc32Checksum())
            .WithEncryption(new Aes256GcmEncryption(), ProtectedCorpusKey, keyId: "v1-fixture"));

        byte[] frame = Wire.Fixture("v1-protected.bin");

        var throughGenerated = generated.Deserialize<FrozenPrimitives>(frame);
        var throughReflection = CompatibilityTests.Protected.Deserialize<FrozenPrimitives>(frame);

        Assert.Equal(CompatibilityTests.Plain.Serialize(throughReflection), CompatibilityTests.Plain.Serialize(throughGenerated));
    }

    /// <summary>The key the protected fixture was encrypted with.</summary>
    private static byte[] ProtectedCorpusKey => [.. Enumerable.Range(0, 32).Select(index => (byte)index)];

    /// <summary>
    /// Reads <paramref name="fixture"/> through generated contracts, requires the value to be what the
    /// reflected contracts read, and requires writing it back through generated contracts to give the
    /// frozen bytes again.
    /// </summary>
    private static T AssertReproduced<T>(string fixture, BinarySerializer generated, BinarySerializer reflected)
    {
        byte[] frozen = Wire.Fixture(fixture);

        var restored = generated.Deserialize<T>(frozen)!;

        Assert.Equal(reflected.Serialize(reflected.Deserialize<T>(frozen)), reflected.Serialize(restored));
        Assert.Equal(frozen, generated.Serialize(restored));
        return restored;
    }
}
