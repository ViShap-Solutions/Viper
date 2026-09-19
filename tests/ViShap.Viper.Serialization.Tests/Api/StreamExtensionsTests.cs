using System.Security.Cryptography;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins SX-01…SX-09: each of the thirteen overloads of contract §3.2 agrees with the serializer it
/// builds, leaves the caller's stream open, and rejects a null argument the normal way.
/// </summary>
public class StreamExtensionsTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    private static BinarySerializerOptions Encrypted(byte[] key, string? keyId = null) =>
        BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), key, keyId).Build();

    private static MemoryStream PayloadOf<T>(T value, BinarySerializerOptions? options = null) =>
        new(new BinarySerializer(options).Serialize(value), writable: false);

    // --- SX-01, SX-02: agreement with the serializer -----------------------------------------------

    [Fact]
    public void Serialize_MatchesTheSerializerByteForByte()
    {
        using var stream = new MemoryStream();

        stream.Serialize(Sample());

        Assert.Equal(new BinarySerializer().Serialize(Sample()), stream.ToArray());
    }

    [Fact]
    public void Serialize_WithOptions_MatchesTheSerializerByteForByte()
    {
        var options = BinarySerializerOptions.Configure().PreserveReferences().Build();
        using var stream = new MemoryStream();

        stream.Serialize(Sample(), options);

        Assert.Equal(new BinarySerializer(options).Serialize(Sample()), stream.ToArray());
    }

    [Fact]
    public void Deserialize_WithOptions_MatchesTheSerializer()
    {
        var options = BinarySerializerOptions.Configure().Build();
        using var source = PayloadOf(Sample());

        Assert.Equivalent(Sample(), source.Deserialize<Person>(options));
    }

    // --- SX-03…SX-05: header-derived configuration --------------------------------------------------

    [Fact]
    public void Deserialize_ConfiguresItselfFromTheHeader()
    {
        var written = BinarySerializerOptions.Configure()
            .WithCompression(new Compression.Deflate())
            .WithChecksum(new Checksum.Crc32())
            .Build();
        using var source = PayloadOf(Sample(), written);

        Assert.Equivalent(Sample(), source.Deserialize<Person>());
    }

    [Fact]
    public void Deserialize_WithAKey_DecryptsThePayload()
    {
        byte[] key = NewKey();
        using var source = PayloadOf(Sample(), Encrypted(key));

        Assert.Equivalent(Sample(), source.Deserialize<Person>(key));
    }

    [Fact]
    public void Deserialize_WithAKeyResolver_ResolvesByTheHeaderKeyId()
    {
        byte[] key = NewKey();
        using var source = PayloadOf(Sample(), Encrypted(key, "primary"));
        string? observed = null;

        var result = source.Deserialize<Person>(id => { observed = id; return key; });

        Assert.Equivalent(Sample(), result);
        Assert.Equal("primary", observed);
    }

    // --- SX-06: the existing-reference-instance overloads --------------------------------------------

    [Fact]
    public void Deserialize_IntoAnExistingInstance_WithOptions()
    {
        var options = BinarySerializerOptions.Configure().Build();
        using var source = PayloadOf(Sample());
        var target = new Person();

        Assert.Same(target, source.Deserialize(target, options));
        Assert.Equal("Alice", target.Name);
    }

    [Fact]
    public void Deserialize_IntoAnExistingInstance_FromTheHeader()
    {
        using var source = PayloadOf(Sample());
        var target = new Person();

        Assert.Same(target, source.Deserialize(target));
        Assert.Equal("Alice", target.Name);
    }

    [Fact]
    public void Deserialize_IntoAnExistingInstance_WithAKey()
    {
        byte[] key = NewKey();
        using var source = PayloadOf(Sample(), Encrypted(key));
        var target = new Person();

        Assert.Same(target, source.Deserialize(target, key));
        Assert.Equal("Alice", target.Name);
    }

    [Fact]
    public void Deserialize_IntoAnExistingInstance_WithAKeyResolver()
    {
        byte[] key = NewKey();
        using var source = PayloadOf(Sample(), Encrypted(key, "primary"));
        var target = new Person();

        Assert.Same(target, source.Deserialize(target, _ => key));
        Assert.Equal("Alice", target.Name);
    }

    // --- SX-07: the ref value-type overloads ------------------------------------------------------------

    [Fact]
    public void Deserialize_IntoARefValue_WithOptions()
    {
        var options = BinarySerializerOptions.Configure().Build();
        using var source = PayloadOf(new PointStruct { X = 1, Y = 2 });
        var target = default(PointStruct);

        source.Deserialize(ref target, options);

        Assert.Equal(new PointStruct { X = 1, Y = 2 }, target);
    }

    [Fact]
    public void Deserialize_IntoARefValue_FromTheHeader()
    {
        using var source = PayloadOf(new PointStruct { X = 1, Y = 2 });
        var target = default(PointStruct);

        source.Deserialize(ref target);

        Assert.Equal(new PointStruct { X = 1, Y = 2 }, target);
    }

    [Fact]
    public void Deserialize_IntoARefValue_WithAKey()
    {
        byte[] key = NewKey();
        using var source = PayloadOf(new PointStruct { X = 1, Y = 2 }, Encrypted(key));
        var target = default(PointStruct);

        source.Deserialize(ref target, key);

        Assert.Equal(new PointStruct { X = 1, Y = 2 }, target);
    }

    [Fact]
    public void Deserialize_IntoARefValue_WithAKeyResolver()
    {
        byte[] key = NewKey();
        using var source = PayloadOf(new PointStruct { X = 1, Y = 2 }, Encrypted(key, "primary"));
        var target = default(PointStruct);

        source.Deserialize(ref target, _ => key);

        Assert.Equal(new PointStruct { X = 1, Y = 2 }, target);
    }

    // --- SX-08: stream ownership --------------------------------------------------------------------------

    [Fact]
    public void Serialize_LeavesTheCallerStreamOpen()
    {
        using var destination = new TrackingStream();

        destination.Serialize(Sample());

        Assert.False(destination.Disposed);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public void Deserialize_LeavesTheCallerStreamOpen()
    {
        using var source = new TrackingStream(new BinarySerializer().Serialize(Sample()));

        source.Deserialize<Person>();

        Assert.False(source.Disposed);
        Assert.True(source.CanRead);
    }

    [Fact]
    public void Deserialize_IntoAnExistingInstance_LeavesTheCallerStreamOpen()
    {
        using var source = new TrackingStream(new BinarySerializer().Serialize(Sample()));

        source.Deserialize(new Person());

        Assert.False(source.Disposed);
    }

    // --- SX-09: null arguments ----------------------------------------------------------------------------

    [Fact]
    public void Serialize_NullStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => ((Stream)null!).Serialize(Sample()));
    }

    [Fact]
    public void Deserialize_NullStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => ((Stream)null!).Deserialize<Person>());
    }

    [Fact]
    public void Deserialize_NullOptions_ThrowsArgumentNull()
    {
        using var source = PayloadOf(Sample());

        Assert.Throws<ArgumentNullException>(
            () => source.Deserialize<Person>((BinarySerializerOptions)null!));
    }

    [Fact]
    public void Deserialize_NullKeyResolver_ThrowsArgumentNull()
    {
        using var source = PayloadOf(Sample());

        Assert.Throws<ArgumentNullException>(
            () => source.Deserialize<Person>((Func<string?, byte[]?>)null!));
    }

    [Fact]
    public void Deserialize_NullExistingInstance_ThrowsArgumentNull()
    {
        using var source = PayloadOf(Sample());

        Assert.Throws<ArgumentNullException>(() => source.Deserialize((Person)null!));
    }

    [Fact]
    public void Deserialize_IntoARefValue_NullStream_ThrowsArgumentNull()
    {
        var target = default(PointStruct);
        var source = (Stream)null!;

        Assert.Throws<ArgumentNullException>(() => source.Deserialize(ref target));
    }
}
