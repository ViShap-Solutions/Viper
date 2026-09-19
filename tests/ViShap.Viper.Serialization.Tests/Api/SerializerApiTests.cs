using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-01…API-05 and API-13…API-20: construction, the equivalence of the byte-array and stream
/// entry points, null-argument behaviour, and what a call is allowed to do to the caller's stream.
/// </summary>
public class SerializerApiTests
{
    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    // --- construction ----------------------------------------------------------------------------

    [Fact]
    public void Construct_WithoutOptions_UsesTheDocumentedDefaults()
    {
        byte[] fromDefault = new BinarySerializer().Serialize(Sample());
        byte[] fromExplicit = new BinarySerializer(BinarySerializerOptions.Default).Serialize(Sample());

        Assert.Equal(fromExplicit, fromDefault);
    }

    [Fact]
    public void Construct_WithNullOptions_IsTheParameterlessForm()
    {
        byte[] fromNull = new BinarySerializer(null).Serialize(Sample());

        Assert.Equal(new BinarySerializer().Serialize(Sample()), fromNull);
    }

    [Fact]
    public void Construct_WithInvalidLimits_ThrowsConfiguration()
    {
        // Build() validates, so reaching the constructor's own check needs options assembled directly.
        var options = new BinarySerializerOptions
        {
            Limits = SerializationLimits.Default with { MaxDepth = 0 }
        };

        Assert.Throws<BinaryConfigurationException>(() => new BinarySerializer(options));
    }

    // --- entry-point equivalence -------------------------------------------------------------------

    [Fact]
    public void Serialize_ByteArrayAndStream_ProduceIdenticalBytes()
    {
        var serializer = new BinarySerializer();
        using var stream = new MemoryStream();

        serializer.Serialize(stream, Sample());

        Assert.Equal(serializer.Serialize(Sample()), stream.ToArray());
    }

    [Fact]
    public void Deserialize_ByteArrayAndStream_ProduceEqualResults()
    {
        var serializer = new BinarySerializer();
        byte[] payload = serializer.Serialize(Sample());
        using var stream = new MemoryStream(payload, writable: false);

        var fromArray = serializer.Deserialize<Person>(payload);
        var fromStream = serializer.Deserialize<Person>(stream);

        Assert.Equivalent(fromArray, fromStream);
    }

    [Fact]
    public void Serialize_NullRootReferenceType_RoundTripsAsNull()
    {
        var serializer = new BinarySerializer();

        Assert.Null(serializer.Deserialize<Person>(serializer.Serialize<Person?>(null)));
    }

    // --- null arguments ------------------------------------------------------------------------------

    [Fact]
    public void Serialize_NullDestination_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BinarySerializer().Serialize(null!, Sample()));
    }

    [Fact]
    public void Deserialize_NullSource_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BinarySerializer().Deserialize<Person>((Stream)null!));
    }

    [Fact]
    public void Deserialize_NullByteArray_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BinarySerializer().Deserialize<Person>((byte[])null!));
    }

    [Fact]
    public void Deserialize_NullExistingInstance_ThrowsArgumentNull()
    {
        var serializer = new BinarySerializer();
        byte[] payload = serializer.Serialize(Sample());
        using var stream = new MemoryStream(payload, writable: false);

        Assert.Throws<ArgumentNullException>(() => serializer.Deserialize(stream, (Person)null!));
    }

    // --- stream ownership ------------------------------------------------------------------------------

    [Fact]
    public void Serialize_CallerStream_IsNeverDisposed()
    {
        using var destination = new TrackingStream();

        new BinarySerializer().Serialize(destination, Sample());

        Assert.False(destination.Disposed);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public void Deserialize_CallerStream_IsNeverDisposed()
    {
        var serializer = new BinarySerializer();
        using var source = new TrackingStream(serializer.Serialize(Sample()));

        serializer.Deserialize<Person>(source);

        Assert.False(source.Disposed);
        Assert.True(source.CanRead);
    }

    [Fact]
    public void Serialize_CallerStream_IsNeverRewound()
    {
        var serializer = new BinarySerializer();
        using var destination = new MemoryStream();
        destination.Write(new byte[8]);

        serializer.Serialize(destination, Sample());

        Assert.Equal(8 + serializer.Serialize(Sample()).Length, destination.Length);
        Assert.Equal(destination.Length, destination.Position);
    }

    [Fact]
    public void Deserialize_ReadsOnlyAsFarAsThePayloadExtends()
    {
        var serializer = new BinarySerializer();
        byte[] payload = serializer.Serialize(Sample());
        using var source = new MemoryStream();
        source.Write(payload);
        source.Write(new byte[16]);
        source.Position = 0;

        serializer.Deserialize<Person>(source);

        Assert.Equal(payload.Length, source.Position);
    }

    [Fact]
    public void Deserialize_NonSeekableSource_ThrowsNotSupported()
    {
        using var source = new NonSeekableStream(new BinarySerializer().Serialize(Sample()));

        Assert.Throws<NotSupportedException>(() => new BinarySerializer().Deserialize<Person>(source));
    }

    [Fact]
    public void Serialize_NonSeekableDestination_Succeeds()
    {
        var serializer = new BinarySerializer();
        using var destination = new NonSeekableWriteStream();

        serializer.Serialize(destination, Sample());

        Assert.Equal(serializer.Serialize(Sample()), destination.Written);
    }

    // --- per-call isolation ------------------------------------------------------------------------------

    [Fact]
    public void Serialize_RepeatedCalls_CarryNoStateBetweenThem()
    {
        var serializer = new BinarySerializer();

        byte[] first = serializer.Serialize(Sample());
        serializer.Serialize(new List<int> { 1, 2, 3 });
        byte[] third = serializer.Serialize(Sample());

        Assert.Equal(first, third);
    }

    [Fact]
    public void Deserialize_AfterAFailedCall_StillSucceeds()
    {
        var serializer = new BinarySerializer();
        byte[] good = serializer.Serialize(Sample());

        Assert.Throws<BinaryFormatException>(
            () => serializer.Deserialize<Person>(Mutate.Truncate(good, good.Length - 2)));

        Assert.Equal("Alice", serializer.Deserialize<Person>(good)!.Name);
    }

    [Fact]
    public void Deserialize_AfterAFailure_LeavesTheStreamWhereItStopped()
    {
        var serializer = new BinarySerializer();
        byte[] truncated = Mutate.Truncate(serializer.Serialize(Sample()), 20);
        using var source = new MemoryStream(truncated, writable: false);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(source));

        // No position restoration is promised outside the inspection APIs.
        Assert.True(source.Position > 0);
    }

    [Fact]
    public void Serialize_SharedAcrossThreads_ProducesCorrectResults()
    {
        var serializer = new BinarySerializer();
        byte[] expected = serializer.Serialize(Sample());

        Parallel.For(0, 64, _ => Assert.Equal(expected, serializer.Serialize(Sample())));
    }
}
