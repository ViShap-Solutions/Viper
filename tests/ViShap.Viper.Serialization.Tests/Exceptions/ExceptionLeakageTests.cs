using System.Security.Cryptography;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Exceptions;

/// <summary>
/// Pins EXC-16…EXC-20: no framework exception escapes the codec under its own name, and the ones §9
/// makes part of the diagnostic contract survive as <c>InnerException</c>.
/// </summary>
/// <remarks>
/// The two halves are one rule seen from both sides. A caller catching
/// <see cref="BinarySerializerException"/> must catch everything the codec can fail with, and a
/// caller debugging a failure must still be able to reach the cause underneath it.
/// </remarks>
public class ExceptionLeakageTests
{
    private static byte[] ValidFrame() =>
        new BinarySerializer().Serialize(new Person { Name = "Alice", Age = 30 });

    // --- EXC-16: EndOfStreamException never escapes a truncated read ------------------------------

    [Fact]
    public void Deserialize_EveryProperPrefixOfAValidFrame_ThrowsFormatAndNeverEndOfStream()
    {
        var serializer = new BinarySerializer();
        var leaked = new List<(int Length, string Type)>();

        foreach (byte[] prefix in Mutate.Prefixes(ValidFrame()))
        {
            try
            {
                serializer.Deserialize<Person>(prefix);
                leaked.Add((prefix.Length, "no exception"));
            }
            catch (BinaryFormatException)
            {
                // §8.2: a truncated header or payload is malformed input.
            }
            catch (Exception leak)
            {
                leaked.Add((prefix.Length, leak.GetType().FullName!));
            }
        }

        Assert.True(
            leaked.Count == 0,
            $"Prefixes that did not fail as BinaryFormatException: " +
            $"{string.Join(", ", leaked.Select(entry => $"{entry.Length}→{entry.Type}"))}");
    }

    [Fact]
    public void Deserialize_AFrameWhoseDeclaredPayloadIsNotPresent_ThrowsFormatAndNeverEndOfStream()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer => writer.Write(1)), declaredLength: 4096);

        var error = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int>(frame));

        Assert.IsNotType<EndOfStreamException>(error.InnerException);
    }

    [Fact]
    public void Deserialize_EveryProperPrefixOfAV0Payload_ThrowsFormatAndNeverEndOfStream()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

        byte[] payload = serializer.Serialize(new Person { Name = "Alice", Age = 30 });

        foreach (byte[] prefix in Mutate.Prefixes(payload))
            Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(prefix));
    }

    // --- EXC-17: ArgumentException from a parser never escapes -------------------------------------

    [Fact]
    public void Deserialize_ADecimalWithAnUndefinedScale_ThrowsFormatPreservingTheArgumentException()
    {
        // The wire carries the four int32 words of a decimal verbatim; a scale above 28 is a value
        // the CLR constructor refuses, and that refusal is an ArgumentException.
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(0x00FF0000);
        }));

        var error = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<decimal>(frame));

        Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
    }

    [Fact]
    public void Deserialize_ADateTimeOffsetOutsideTheRepresentableRange_ThrowsFormatPreservingTheArgumentException()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(long.MaxValue);   // ticks
            writer.Write(0L);              // offset ticks
        }));

        var error = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<DateTimeOffset>(frame));

        Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
    }

    // --- EXC-18: IOException is preserved as BinaryStreamException.InnerException ------------------

    [Fact]
    public void Serialize_IntoAFailingStream_PreservesTheIoException()
    {
        using var destination = new FailingStream(bytesBeforeFailure: 4);

        var error = Assert.Throws<BinaryStreamException>(
            () => new BinarySerializer().Serialize(destination, new Person { Name = "Alice", Age = 1 }));

        Assert.IsType<IOException>(error.InnerException);
    }

    [Fact]
    public void Deserialize_FromAFailingStream_PreservesTheIoException()
    {
        using var source = new FailingStream(bytesBeforeFailure: 2);

        var error = Assert.Throws<BinaryStreamException>(
            () => new BinarySerializer().Deserialize<int>(source));

        Assert.IsType<IOException>(error.InnerException);
    }

    // --- EXC-19: CryptographicException is preserved as BinaryIntegrityException.InnerException ----

    [Fact]
    public void Deserialize_WithTheWrongKey_PreservesTheCryptographicException()
    {
        byte[] payload = Encrypted(RandomNumberGenerator.GetBytes(32)).Serialize(123);

        var error = Assert.Throws<BinaryIntegrityException>(
            () => Encrypted(RandomNumberGenerator.GetBytes(32)).Deserialize<int>(payload));

        Assert.IsAssignableFrom<CryptographicException>(error.InnerException);
    }

    // --- EXC-20: InvalidDataException is preserved as BinaryFormatException.InnerException ---------

    [Fact]
    public void Deserialize_ADeflateBodyThatIsNotADeflateStream_ThrowsFormatPreservingTheInvalidData()
    {
        byte[] garbage = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        byte[] frame = Wire.FrameWith(
            garbage,
            compression: (byte)CompressionAlgorithm.Deflate,
            uncompressedLength: 64);

        var error = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int>(frame));

        Assert.IsType<InvalidDataException>(error.InnerException);
    }

    private static BinarySerializer Encrypted(byte[] key) =>
        new(BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), key).Build());
}
