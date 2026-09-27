using System.Buffers;
using System.IO.Pipelines;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Metering;

/// <summary>
/// Pins STR-29 and STR-30: the payload path builds a whole frame in the serializer's own pooled
/// buffers, with no <see cref="MemoryStream"/> between the engine and the destination, and copies
/// it out only once it is complete — so an exception in the middle of a graph leaves every kind of
/// destination with nothing written.
/// </summary>
public class AtomicWriteTests
{
    /// <summary>The production folders and files a payload passes through, from the engine to the destination.</summary>
    private static readonly string[] PayloadPath =
    [
        "ViShap.Viper.Serialization/BinarySerializer.cs",
        "ViShap.Viper.Serialization/Algorithms/",
        "ViShap.Viper.Serialization/Engine/",
        "ViShap.Viper.Serialization/Formatters/",
        "ViShap.Viper.Serialization/Io/",
        "ViShap.Viper.Serialization/Metadata/BinaryFormatHeaderV1.cs",
        "ViShap.Viper.Serialization/Pipeline/",
        "ViShap.Viper.Serialization/Security/"
    ];

    private static BinarySerializer Serializer(int version) =>
        new(BinarySerializerOptions.Configure().WithVersion(version).Build());

    private static BinarySerializer Protected() =>
        new(BinarySerializerOptions.Configure()
            .WithCompression(new Brotli())
            .WithChecksum(new Crc32())
            .WithEncryption(new Aes256Gcm(), new byte[32])
            .Build());

    /// <summary>
    /// A list whose first elements encode normally and whose last one refers back to itself, so the
    /// failure comes after bytes of the payload have already been produced.
    /// </summary>
    private static List<Cyclic> FailsMidGraph()
    {
        var cyclic = new Cyclic { Name = "loop" };
        cyclic.Next = cyclic;

        return
        [
            new Cyclic { Name = new string('a', 600) },
            new Cyclic { Name = new string('b', 600) },
            cyclic
        ];
    }

    public static TheoryData<string> Serializers => ["v1", "v0", "protected"];

    /// <summary>The serializers whose output is a function of the value alone; encryption draws a fresh nonce per call.</summary>
    public static TheoryData<string> Deterministic => ["v1", "v0"];

    private static BinarySerializer Named(string name) => name switch
    {
        "v1" => Serializer(1),
        "v0" => Serializer(0),
        _ => Protected()
    };

    // --- STR-29: no MemoryStream on the payload path ---------------------------------------------

    [Fact]
    public void PayloadPath_ContainsNoMemoryStream()
    {
        var files = SourceTree.ProductionFiles
            .Where(file => PayloadPath.Any(prefix => file.Key.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.Contains(files, file => file.Key.StartsWith("ViShap.Viper.Serialization/Pipeline/", StringComparison.Ordinal));

        var offenders = files
            .Where(file => file.Value.Contains("MemoryStream", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"`MemoryStream` on the payload path in: {string.Join(", ", offenders)}");
    }

    // --- STR-30: a failed write leaves the destination empty -------------------------------------

    [Theory]
    [MemberData(nameof(Serializers))]
    public void Serialize_FailingMidGraph_LeavesAStreamEmpty(string name)
    {
        using var destination = new MemoryStream();

        Assert.Throws<BinaryTypeException>(() => Named(name).Serialize(destination, FailsMidGraph()));

        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void Serialize_FailingMidGraph_LeavesANonSeekableStreamEmpty(string name)
    {
        using var destination = new NonSeekableWriteStream();

        Assert.Throws<BinaryTypeException>(() => Named(name).Serialize(destination, FailsMidGraph()));

        Assert.Empty(destination.Written);
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void Serialize_FailingMidGraph_LeavesABufferWriterEmpty(string name)
    {
        var destination = new ArrayBufferWriter<byte>();

        Assert.Throws<BinaryTypeException>(() => Named(name).SerializeTo(destination, FailsMidGraph()));

        Assert.Equal(0, destination.WrittenCount);
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task Serialize_FailingMidGraph_LeavesAPipeWriterEmpty(string name)
    {
        var pipe = new Pipe();

        Assert.Throws<BinaryTypeException>(() => Named(name).SerializeTo(pipe.Writer, FailsMidGraph()));

        Assert.Equal(0, pipe.Writer.UnflushedBytes);
        await pipe.Writer.CompleteAsync();
        var result = await pipe.Reader.ReadAsync();
        Assert.True(result.Buffer.IsEmpty);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Serialize_ExceedingTheWireBudget_LeavesAStreamEmpty(int version)
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithVersion(version)
                .WithLimits(SerializationLimits.Default with { MaxWireBytes = 1_000 })
                .Build());
        var value = new List<string> { new('a', 600), new('b', 600) };
        using var destination = new MemoryStream();

        AssertEx.Throws<BinaryLimitException>("wire", () => serializer.Serialize(destination, value));

        Assert.Equal(0, destination.Length);
    }

    // --- every destination receives the same bytes -----------------------------------------------

    [Theory]
    [MemberData(nameof(Deterministic))]
    public void SerializeTo_ABufferWriter_WritesTheSameBytesAsTheArrayForm(string name)
    {
        var serializer = Named(name);
        var value = new NewSchema { Removed = new Node { Value = 1 }, Kept = new Node { Value = 2 } };
        var destination = new ArrayBufferWriter<byte>(initialCapacity: 1);

        serializer.SerializeTo(destination, value);

        Assert.Equal(serializer.Serialize(value), destination.WrittenSpan.ToArray());
    }

    [Theory]
    [MemberData(nameof(Deterministic))]
    public async Task SerializeTo_APipeWriter_WritesTheSameBytesAsTheArrayForm(string name)
    {
        var serializer = Named(name);
        var value = new Person { Name = new string('p', 5_000), Age = 7 };
        var pipe = new Pipe();

        serializer.SerializeTo(pipe.Writer, value);
        await pipe.Writer.CompleteAsync();

        var result = await pipe.Reader.ReadAsync();
        Assert.Equal(serializer.Serialize(value), result.Buffer.ToArray());
    }

    [Fact]
    public async Task SerializeTo_ABufferWriterAndAPipeWriter_UnderEveryPhase_RoundTrip()
    {
        var serializer = Protected();
        var value = new Person { Name = new string('p', 5_000), Age = 7 };
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 1);
        var pipe = new Pipe();

        serializer.SerializeTo(buffer, value);
        serializer.SerializeTo(pipe.Writer, value);
        await pipe.Writer.CompleteAsync();
        var result = await pipe.Reader.ReadAsync();

        Assert.Equal(7, serializer.Deserialize<Person>(buffer.WrittenSpan.ToArray())!.Age);
        Assert.Equal(7, serializer.Deserialize<Person>(result.Buffer.ToArray())!.Age);
    }
}
