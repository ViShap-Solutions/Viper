using System.Text.RegularExpressions;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Diagnostics;

/// <summary>
/// Pins DMP-01…DMP-05: the dumper is the one place in the library allowed to turn a failure into
/// text. It renders what the envelope declares, it leaves the stream where it found it, and the
/// licence it holds is exactly one catch of one exception family — an exception outside the taxonomy
/// still propagates, and no production type outside <c>Diagnostics/</c> may report a failure the same
/// way (§19).
/// </summary>
public class DumperTests
{
    private static readonly byte[] Key =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void DumpHeader_Bytes_RendersTheEnvelope()
    {
        string report = BinaryFormatDumper.DumpHeader(_serializer.Serialize(new Person { Name = "Alice", Age = 30 }));

        Assert.Contains("Format version : 1", report, StringComparison.Ordinal);
        Assert.Contains("Compression    : None", report, StringComparison.Ordinal);
        Assert.Contains("Checksum       : None", report, StringComparison.Ordinal);
        Assert.Contains("Encryption     : None", report, StringComparison.Ordinal);
        Assert.Contains("Key id         : (none)", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpHeader_Bytes_NamesTheCustomAlgorithmsAndTheKeyId()
    {
        var options = BinarySerializerOptions.Configure()
            .WithCompression(new IdentityCompression())
            .WithChecksum(new Sum8())
            .WithEncryption(new UnauthenticatedCipher(), Key, keyId: "primary")
            .Build();

        string report = BinaryFormatDumper.DumpHeader(new BinarySerializer(options).Serialize(123));

        Assert.Contains($"Custom ('{IdentityCompression.RegisteredName}')", report, StringComparison.Ordinal);
        Assert.Contains($"Custom ('{Sum8.RegisteredName}')", report, StringComparison.Ordinal);
        Assert.Contains($"Custom ('{UnauthenticatedCipher.RegisteredName}')", report, StringComparison.Ordinal);
        Assert.Contains("Key id         : primary", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpHeader_Bytes_RevealsNoKeyMaterial()
    {
        var options = BinarySerializerOptions.Configure()
            .WithEncryption(new Crypto.Aes256Gcm(), Key, keyId: "primary")
            .Build();

        string report = BinaryFormatDumper.DumpHeader(new BinarySerializer(options).Serialize(123));

        Assert.Contains("Key id         : primary", report, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(Key), report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DumpHeader_NullBytes_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => BinaryFormatDumper.DumpHeader((byte[])null!));
    }

    [Fact]
    public void DumpHeader_Stream_RendersTheEnvelopeAndLeavesThePositionUntouched()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[16]);
        _serializer.Serialize(stream, 123);
        stream.Position = 16;

        string report = BinaryFormatDumper.DumpHeader(stream);

        Assert.Contains("Format version : 1", report, StringComparison.Ordinal);
        Assert.Equal(16, stream.Position);
        Assert.Equal(123, _serializer.Deserialize<int>(stream));
    }

    [Fact]
    public void DumpHeader_NullStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => BinaryFormatDumper.DumpHeader((Stream)null!));
    }

    [Fact]
    public void DumpHeader_UnrecognizedBytes_ReportsItAsTextRatherThanThrowing()
    {
        string report = BinaryFormatDumper.DumpHeader(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.Contains("No recognized Viper header", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpHeader_MalformedHeader_ReportsTheExceptionAsText()
    {
        string report = BinaryFormatDumper.DumpHeader(Mutate.Truncate(_serializer.Serialize(123), 12));

        Assert.Contains("Header could not be read", report, StringComparison.Ordinal);
        Assert.Contains(nameof(BinaryFormatException), report, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpHeader_UnsupportedVersion_ReportsTheExceptionAsText()
    {
        string report = BinaryFormatDumper.DumpHeader(
            Wire.FrameWith(Wire.Payload(writer => writer.Write(1)), version: 2));

        Assert.Contains(nameof(BinaryFormatNotSupportedException), report, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpHeader_NonSeekableStream_PropagatesTheFrameworkException()
    {
        // The licence is one family: `NotSupportedException` is outside the taxonomy, so the dumper
        // is not allowed to render it as a line of text (§19).
        using var stream = new NonSeekableStream(_serializer.Serialize(123));

        Assert.Throws<NotSupportedException>(() => BinaryFormatDumper.DumpHeader(stream));
    }

    [Fact]
    public void DumpHeader_StreamFailure_ReportsTheStreamExceptionAsText()
    {
        // A stream failure arrives as BinaryStreamException, which is inside the family the dumper
        // catches, so it is reported rather than propagated.
        using var stream = new FailingContentStream(_serializer.Serialize(123), bytesBeforeFailure: 12);

        string report = BinaryFormatDumper.DumpHeader(stream);

        Assert.Contains(nameof(BinaryStreamException), report, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureAsOutput_AppearsNowhereOutsideTheDiagnosticsFolder()
    {
        // The discriminator is what the catch does with what it caught: a cleanup that rethrows
        // leaves the failure on its way to the caller, while a block that falls through has turned
        // it into a return value. Only the dumper is allowed the second shape.
        var offenders = SourceTree.ProductionFiles
            .Where(file => !file.Key.Contains("/Diagnostics/", StringComparison.Ordinal))
            .Where(file => ViperCatchPattern.Count(file.Value) != ViperCatchThatRethrowsPattern.Count(file.Value))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"A Viper exception is turned into a result outside Diagnostics/ in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void FailureAsOutput_IsWhatTheDumperItselfDoes()
    {
        // Guards the invariant above: if the two patterns stopped telling the shapes apart, the
        // test would pass by matching nothing anywhere.
        string dumper = SourceTree.ProductionFiles["ViShap.Viper.Serialization/Diagnostics/BinaryFormatDumper.cs"];

        Assert.Equal(1, ViperCatchPattern.Count(dumper));
        Assert.Equal(0, ViperCatchThatRethrowsPattern.Count(dumper));
    }

    [Fact]
    public void TheDumper_CatchesNothingBeyondTheViperFamily()
    {
        string dumper = SourceTree.ProductionFiles["ViShap.Viper.Serialization/Diagnostics/BinaryFormatDumper.cs"];

        Assert.Equal(1, AnyCatchPattern.Count(dumper));
        Assert.Equal(1, ViperCatchPattern.Count(dumper));
    }

    private static readonly Regex ViperCatchPattern = new(
        @"catch\s*\(\s*Binary\w*Exception[^)]*\)");

    private static readonly Regex ViperCatchThatRethrowsPattern = new(
        @"catch\s*\(\s*Binary\w*Exception[^)]*\)\s*\{[^{}]*?\bthrow\s*;", RegexOptions.Singleline);

    private static readonly Regex AnyCatchPattern = new(@"\bcatch\s*[({]");
}
