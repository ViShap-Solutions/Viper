using System.Collections;
using System.Security.Cryptography;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-17, HST-18, HST-20…HST-24: a short payload that asks for a large amount of work. Every
/// declaration is measured against what the source can actually deliver and against the budget the
/// operation still holds, before the buffer or the loop it describes exists.
/// </summary>
public class AmplificationTests
{
    /// <summary>Bytes the whole operation may allocate. Generous, and orders below the declarations.</summary>
    private const long AllocationCeiling = 1024 * 1024;

    private const byte Deflate = 1;
    private const byte Aes256Gcm = 1;

    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    // --- HST-17: a declared count with nothing behind it -----------------------------------------

    [Fact]
    public void Deserialize_ACountAtTheLimitWithNoElements_ThrowsFormat()
    {
        byte[] frame = Wire.Container(declaredCount: 1_000_000, int32Values: 0);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<List<int>>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_ACountAtTheLimitWithNoElements_AllocatesNothingProportional()
    {
        // A count is not a length, so it is not compared with the remaining bytes; what bounds it is
        // the incremental growth path, which never sizes the builder from the declaration.
        byte[] frame = Wire.Container(declaredCount: 1_000_000, int32Values: 0);

        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<List<int>>(frame));
    }

    [Fact]
    public void Deserialize_AnArrayCountAtTheLimitWithNoElements_AllocatesNothingProportional()
    {
        byte[] frame = Wire.Container(declaredCount: 1_000_000, int32Values: 0);

        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<int[]>(frame));
    }

    [Fact]
    public void Deserialize_ADictionaryCountAtTheLimitWithNoEntries_AllocatesNothingProportional()
    {
        byte[] frame = Wire.Container(declaredCount: 1_000_000, int32Values: 0);

        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<Dictionary<int, int>>(frame));
    }

    // --- HST-18: a declared length inside the payload --------------------------------------------

    [Fact]
    public void Deserialize_AStringLongerThanThePayload_ThrowsFormatBeforeAllocating()
    {
        byte[] frame = Wire.StringValue(3_000_000);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<string>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_ABlobLongerThanThePayload_ThrowsFormatBeforeAllocating()
    {
        byte[] lying = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(64);                        // bit count
            writer.Write7BitEncodedInt(8_000_000);   // a blob far larger than the frame
        }));

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<BitArray>(lying));

        Assert.IsNotType<BinaryLimitException>(ex);
        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<BitArray>(lying));
    }

    [Fact]
    public void Deserialize_AStringLongerThanItsKeyedWindow_ThrowsFormatBeforeAllocating()
    {
        byte[] frame = Wire.Frame(
        [
            .. Wire.NotNull,
            .. Wire.KeyedBody(
            [
                // Key 2 of OldSchema decodes a Node, whose Value claims far more than the window.
                new Wire.KeyedField(2, [1, 0xFF, 0xFF, 0x7F])
            ])
        ]);

        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<OldSchema>(frame));
    }

    // --- HST-20: a declared plaintext larger than the ciphertext ---------------------------------

    [Fact]
    public void Deserialize_ADeclaredPlaintextLongerThanTheCiphertext_ThrowsFormat()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), RandomNumberGenerator.GetBytes(32))
                .Build());
        byte[] frame = Wire.FrameWith(
            new byte[32],
            encryption: Aes256Gcm,
            uncompressedLength: 8 * 1024 * 1024,
            compressedLength: 8 * 1024 * 1024,
            onDiskLength: 32);

        AssertEx.Throws<BinaryFormatException>(
            "ciphertext byte(s) present", () => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_ADeclaredPlaintextLongerThanTheCiphertext_AllocatesNothingProportional()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), RandomNumberGenerator.GetBytes(32))
                .Build());
        byte[] frame = Wire.FrameWith(
            new byte[32],
            encryption: Aes256Gcm,
            uncompressedLength: 8 * 1024 * 1024,
            compressedLength: 8 * 1024 * 1024,
            onDiskLength: 32);

        AssertEx.AllocatesLessThan(AllocationCeiling, () => serializer.Deserialize<int>(frame));
    }

    // --- HST-21: a decompression bomb -------------------------------------------------------------

    [Fact]
    public void Deserialize_ADeclaredUncompressedLengthAboveMaxPayloadBytes_ThrowsLimit()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithCompression(new Deflate())
                .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 1024 })
                .Build());
        byte[] frame = Wire.FrameWith(
            new byte[16],
            compression: Deflate,
            uncompressedLength: 8 * 1024 * 1024,
            compressedLength: 16,
            onDiskLength: 16);

        AssertEx.Throws<BinaryLimitException>(
            "UncompressedLength", () => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_ADeclaredUncompressedLengthAboveMaxPayloadBytes_AllocatesNothingProportional()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithCompression(new Deflate())
                .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 1024 })
                .Build());
        byte[] frame = Wire.FrameWith(
            new byte[16],
            compression: Deflate,
            uncompressedLength: 8 * 1024 * 1024,
            compressedLength: 16,
            onDiskLength: 16);

        AssertEx.AllocatesLessThan(AllocationCeiling, () => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_ACompressedLengthTheFrameDoesNotCarry_ThrowsFormat()
    {
        // The attacker must actually deliver CompressedLength bytes; claiming them is not enough.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithCompression(new Deflate()).Build());
        byte[] frame = Wire.FrameWith(
            new byte[16],
            compression: Deflate,
            uncompressedLength: 4096,
            compressedLength: 4096,
            onDiskLength: 4096);

        var ex = Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<int>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_AHighlyCompressiblePayloadWithinTheLimits_StillRoundTrips()
    {
        // The bound is the declared uncompressed size, not the compression ratio: a legitimate
        // payload that compresses well is unaffected.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithCompression(new Deflate()).Build());
        string original = new('x', 100_000);

        byte[] frame = serializer.Serialize(original);

        Assert.True(frame.Length < 10_000, $"The frame is {frame.Length} bytes.");
        Assert.Equal(original, serializer.Deserialize<string>(frame));
    }

    // --- HST-22: nesting does not escape the cumulative element budget ---------------------------

    [Fact]
    public void Deserialize_NestedIndividuallyLegalCollections_ShareTheElementBudget()
    {
        var nested = new List<List<List<int>>>
        {
            new() { new() { 1, 2 }, new() { 3, 4 } },
            new() { new() { 5, 6 }, new() { 7, 8 } }
        };
        byte[] payload = new BinarySerializer().Serialize(nested);

        // Every single count is 2, far below the per-collection ceiling; together they are fourteen.
        var serializer = Limited(SerializationLimits.Default with
        {
            MaxCollectionLength = 4,
            MaxTotalElements = 10
        });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<List<List<List<int>>>>(payload));
    }

    [Fact]
    public void Deserialize_NestedIndividuallyLegalCollectionsWithinTheBudget_Succeeds()
    {
        var nested = new List<List<List<int>>>
        {
            new() { new() { 1, 2 }, new() { 3, 4 } },
            new() { new() { 5, 6 }, new() { 7, 8 } }
        };
        byte[] payload = new BinarySerializer().Serialize(nested);

        var serializer = Limited(SerializationLimits.Default with
        {
            MaxCollectionLength = 4,
            MaxTotalElements = 14
        });

        Assert.Equal(2, serializer.Deserialize<List<List<List<int>>>>(payload)!.Count);
    }

    // --- HST-23: many small keyed objects ---------------------------------------------------------

    [Fact]
    public void Deserialize_ManyKeyedObjectsEachWithinTheirOwnCeiling_ShareTheMetadataBudget()
    {
        var objects = Enumerable.Range(0, 8)
            .Select(_ => new NewSchema { Removed = new Node(), Kept = new Node() })
            .ToList();
        byte[] payload = new BinarySerializer().Serialize(objects);

        // Two fields each satisfies MaxKeyedFields; sixteen together do not satisfy the operation.
        var serializer = Limited(SerializationLimits.Default with
        {
            MaxKeyedFields = 2,
            MaxTotalKeyedFields = 10
        });

        AssertEx.Throws<BinaryLimitException>(
            "Cumulative keyed field count",
            () => serializer.Deserialize<List<NewSchema>>(payload));
    }

    [Fact]
    public void Deserialize_ManyKeyedObjectsWithinTheMetadataBudget_Succeeds()
    {
        var objects = Enumerable.Range(0, 8)
            .Select(_ => new NewSchema { Removed = new Node(), Kept = new Node() })
            .ToList();
        byte[] payload = new BinarySerializer().Serialize(objects);

        var serializer = Limited(SerializationLimits.Default with
        {
            MaxKeyedFields = 2,
            MaxTotalKeyedFields = 16
        });

        Assert.Equal(8, serializer.Deserialize<List<NewSchema>>(payload)!.Count);
    }

    [Fact]
    public void Deserialize_ManyUnknownKeyedFieldsAcrossObjects_ShareTheMetadataBudget()
    {
        // Skipped fields count too, so a payload of objects a reader knows nothing about is bounded.
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);                      // the list is non-null
            writer.Write(4);                         // four objects
            for (int i = 0; i < 4; i++)
            {
                writer.Write(true);                  // the object is non-null
                writer.Write7BitEncodedInt(3);       // three fields nobody knows
                for (int key = 10; key < 13; key++)
                {
                    writer.Write7BitEncodedInt(key);
                    writer.Write(0);                 // an empty payload
                }
            }
        }));

        var serializer = Limited(SerializationLimits.Default with
        {
            MaxKeyedFields = 3,
            MaxTotalKeyedFields = 8
        });

        AssertEx.Throws<BinaryLimitException>(
            "Cumulative keyed field count",
            () => serializer.Deserialize<List<EmptyContract>>(frame));
    }
}
