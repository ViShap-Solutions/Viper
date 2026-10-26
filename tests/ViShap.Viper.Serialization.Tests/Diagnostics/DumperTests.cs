using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;
using ViShap.Viper.Serialization.Tests.Format;

namespace ViShap.Viper.Serialization.Tests.Diagnostics;

/// <summary>
/// Pins DMP-01…DMP-17: the dumper reads a frame for a person — its header, each phase, and with a type
/// the payload as a tree of nodes whose offsets and lengths are the bytes they occupy — and reports a
/// failure as part of the dump instead of throwing it. It is the one place in the library allowed to
/// turn a failure into output, it catches nothing outside the Viper family, it reads under the
/// limits of the options it is given, and it never renders key material (§19).
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

    private static readonly string[] FixtureNames =
    [
        "v1-primitives.bin", "v1-time-system.bin", "v1-numerics.bin", "v1-collections.bin",
        "v1-composites.bin", "v1-keyed.bin", "v1-union.bin", "v1-references.bin",
        "v1-absences.bin", "v1-protected.bin", "v0-primitives.bin", "person-v0.bin", "person-v1.bin"
    ];

    private static BinarySerializerOptions ProtectedOptions =>
        BinarySerializerOptions.Configure()
            .WithCompression(new BrotliCompression())
            .WithChecksum(new Crc32Checksum())
            .WithEncryption(new Aes256GcmEncryption(), CompatibilityProtectedKey, keyId: "v1-fixture")
            .Build();

    /// <summary>The key <c>v1-protected.bin</c> was encrypted with.</summary>
    private static readonly byte[] CompatibilityProtectedKey = [.. Enumerable.Range(0, 32).Select(value => (byte)value)];

    // --- DMP-01, DMP-02: the header -----------------------------------------------------------------

    [Fact]
    public void DumpHeader_Span_RendersTheEnvelope()
    {
        string report = BinaryFormatDumper.DumpHeader(_serializer.Serialize(new Person { Name = "Alice", Age = 30 }));

        Assert.Contains("Format version : 1", report, StringComparison.Ordinal);
        Assert.Contains("References     : none", report, StringComparison.Ordinal);
        Assert.Contains("Compression    : None", report, StringComparison.Ordinal);
        Assert.Contains("Checksum       : None", report, StringComparison.Ordinal);
        Assert.Contains("Encryption     : None", report, StringComparison.Ordinal);
        Assert.Contains("Key id         : (none)", report, StringComparison.Ordinal);
        Assert.Contains("Header length  : 8", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpHeader_Span_NamesTheCustomAlgorithmsTheKeyIdAndTheLengths()
    {
        var options = BinarySerializerOptions.Configure()
            .WithCompression(new IdentityCompression())
            .WithChecksum(new Sum8())
            .WithEncryption(new UnauthenticatedCipher(), Key, keyId: "primary")
            .Build();

        string report = BinaryFormatDumper.DumpHeader(new BinarySerializer(options).Serialize(123));

        Assert.Contains($"Custom ('{IdentityCompression.RegisteredName}'), 4 bytes uncompressed", report, StringComparison.Ordinal);
        Assert.Contains($"Custom ('{Sum8.RegisteredName}')", report, StringComparison.Ordinal);
        Assert.Contains($"Custom ('{UnauthenticatedCipher.RegisteredName}')", report, StringComparison.Ordinal);
        Assert.Contains("Key id         : primary", report, StringComparison.Ordinal);
        Assert.Contains("On-disk length : 4", report, StringComparison.Ordinal);
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

    // --- DMP-03, DMP-04: a failure is text, and only the Viper family is ------------------------------

    [Fact]
    public void DumpHeader_UnrecognizedBytes_ReportsItAsTextRatherThanThrowing()
    {
        string report = BinaryFormatDumper.DumpHeader(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.Contains("No recognized Viper header", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpHeader_MalformedHeader_ReportsTheExceptionAsText()
    {
        string report = BinaryFormatDumper.DumpHeader(Mutate.Truncate(_serializer.Serialize(123), 6));

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
        using var stream = new FailingContentStream(_serializer.Serialize(123), bytesBeforeFailure: 6);

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

        Assert.True(ViperCatchPattern.Count(dumper) > ViperCatchThatRethrowsPattern.Count(dumper));
    }

    [Fact]
    public void TheDiagnostics_CatchNothingBeyondTheViperFamily()
    {
        var files = SourceTree.ProductionFiles.Where(file => file.Key.Contains("/Diagnostics/", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.Equal(AnyCatchPattern.Count(file.Value), ViperCatchPattern.Count(file.Value)));
    }

    // --- DMP-06: the header reads the same from every source ----------------------------------------

    [Fact]
    public void DumpHeader_SpanSequenceAndStream_RenderTheSameTextForEveryFixture()
    {
        foreach (string name in FixtureNames)
        {
            byte[] fixture = Wire.Fixture(name);
            string fromSpan = BinaryFormatDumper.DumpHeader(fixture.AsSpan());
            string fromSequence = BinaryFormatDumper.DumpHeader(Sequences.Of(fixture[..3], fixture[3..Math.Min(9, fixture.Length)], fixture[Math.Min(9, fixture.Length)..]));
            using var stream = new MemoryStream(fixture, writable: false);

            Assert.Equal(fromSpan, fromSequence);
            Assert.Equal(fromSpan, BinaryFormatDumper.DumpHeader(stream));
        }
    }

    // --- DMP-07: every node kind ----------------------------------------------------------------------

    [Fact]
    public void Dump_RendersEveryNodeKind()
    {
        var composites = BinaryFormatDumper.Dump<FrozenComposites>(Wire.Fixture("v1-composites.bin"));
        var collections = BinaryFormatDumper.Dump<FrozenCollections>(Wire.Fixture("v1-collections.bin"));
        var absences = BinaryFormatDumper.Dump<FrozenAbsences>(Wire.Fixture("v1-absences.bin"));
        var keyed = BinaryFormatDumper.Dump<FrozenKeyed>(Wire.Fixture("v1-keyed.bin"));
        var union = BinaryFormatDumper.Dump<FrozenShape>(Wire.Fixture("v1-union.bin"));
        var references = BinaryFormatDumper.Dump<FrozenGraph>(Wire.Fixture("v1-references.bin"), Referencing);
        var unknown = BinaryFormatDumper.Dump<OldSchema>(new BinarySerializer().Serialize(new NewSchema { Removed = new Node { Value = 1 }, Kept = new Node { Value = 2 } }));

        var kinds = new[] { composites, collections, absences, keyed, union, references, unknown }
            .SelectMany(dump => All(dump.Root!))
            .Select(node => node.Kind)
            .ToHashSet();

        Assert.Equal(Enum.GetValues<BinaryDumpNodeKind>().ToHashSet(), kinds);

        var field = All(keyed.Root!).First(node => node.Kind == BinaryDumpNodeKind.KeyedField && node.Key == 2);
        Assert.Equal("Text", field.Name);
        Assert.Equal("\"keyed\"", field.Children.Single().Value);

        var skipped = All(unknown.Root!).Single(node => node.Kind == BinaryDumpNodeKind.UnknownKeyedField);
        Assert.Equal(1, skipped.Key);
        Assert.True(skipped.Length > 0);

        Assert.Equal(BinaryDumpNodeKind.Union, union.Root!.Kind);
        Assert.Equal((byte)1, union.Root.UnionTag);
        Assert.Equal(nameof(FrozenCircle), union.Root.TypeName);

        var back = All(references.Root!).First(node => node.Kind == BinaryDumpNodeKind.BackReference);
        Assert.NotNull(back.ReferenceId);
        Assert.StartsWith("FrozenGraph.", back.ReferenceTarget, StringComparison.Ordinal);

        Assert.Contains(All(composites.Root!), node => node.Name == "Pair" && node.Kind == BinaryDumpNodeKind.Composite);
        Assert.Contains(All(composites.Root!), node => node.Name == "Grid" && node.Kind == BinaryDumpNodeKind.Composite);
        Assert.Contains(All(collections.Root!), node => node.Name == "Dictionary" && node.Kind == BinaryDumpNodeKind.Map);
        Assert.Contains(All(absences.Root!), node => node.Name == "NullText" && node.Kind == BinaryDumpNodeKind.Null);
    }

    // --- DMP-08: every node is the bytes it occupies ------------------------------------------------

    [Fact]
    public void Dump_EveryNodeIsTheBytesItOccupies_ForEveryFixture()
    {
        foreach (var dump in FixtureDumps())
        {
            Assert.Null(dump.Failure);
            Assert.Equal(0, dump.Root!.Offset);
            Assert.Equal(dump.PayloadLength, dump.Root.Length);

            foreach (var node in All(dump.Root))
            {
                long end = node.Offset + node.Length;
                long cursor = node.Offset;
                foreach (var child in node.Children)
                {
                    Assert.True(child.Offset >= cursor, $"{child.Name} of {node.Name} starts before its sibling ends.");
                    Assert.True(child.Offset + child.Length <= end, $"{child.Name} of {node.Name} ends past its parent.");
                    cursor = child.Offset + child.Length;
                }

                // What is left between the children is the node's own framing: its null, its frame,
                // its count, its tag or a keyed field's key and length. A scalar's bytes are its own.
                int framing = node.Length - node.Children.Sum(child => child.Length);
                Assert.True(framing >= 0, $"{node.Name} is shorter than its children.");
            }
        }
    }

    // --- DMP-09: one failure per class ----------------------------------------------------------------

    [Fact]
    public void Dump_AFormatFailure_KeepsTheTreeAndPointsAtTheFailingMember()
    {
        byte[] frame = _serializer.Serialize(new Person { Name = "Ada", Age = 36 });
        var header = Wire.ReadHeader(frame);
        // Person is its flag, Age, then Name: its length and three bytes. The first of them becomes
        // a continuation byte with nothing in front of it.
        byte[] broken = Mutate.SetByte(frame, header.HeaderLength + 6, 0x8D);

        var dump = BinaryFormatDumper.Dump<Person>(broken);

        Assert.IsType<BinaryFormatException>(dump.Failure);
        Assert.Equal("Person.Name", dump.FailurePath);
        Assert.Equal(5, dump.FailureOffset);
        Assert.Equal("36", dump.Root!.Children.Single(node => node.Name == "Age").Value);
        Assert.Contains("failure at @0005 (5) in Person.Name", dump.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_ALimitFailure_IsReportedAsALimit()
    {
        var options = BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with { MaxCollectionLength = 2 })
            .Build();

        var dump = BinaryFormatDumper.Dump<List<int>>(_serializer.Serialize(new List<int> { 1, 2, 3 }), options);

        Assert.IsType<BinaryLimitException>(dump.Failure);
        Assert.Equal("List<Int32>", dump.FailurePath);
    }

    [Fact]
    public void Dump_ANotSupportedFailure_IsReportedAsNotSupported()
    {
        var dump = BinaryFormatDumper.Dump<int>(Wire.FrameWith([42, 0, 0, 0], services: [Wire.Service(5, critical: true, [])]));

        Assert.IsType<BinaryFormatNotSupportedException>(dump.Failure);
        Assert.Null(dump.Header);
    }

    [Fact]
    public void Dump_AnIntegrityFailure_ReportsTheChecksumAsNotMatching()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithChecksum(new Crc32Checksum()).Build());
        byte[] frame = serializer.Serialize(42);
        byte[] tampered = Mutate.FlipByte(frame, frame.Length - 1);

        var dump = BinaryFormatDumper.Dump<int>(tampered);

        Assert.IsType<BinaryIntegrityException>(dump.Failure);
        Assert.False(dump.ChecksumVerified);
        Assert.Contains("does not match", dump.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_AKeyFailure_IsReportedAndNothingIsDecrypted()
    {
        var dump = BinaryFormatDumper.Dump<FrozenPrimitives>(Wire.Fixture("v1-protected.bin"));

        Assert.IsType<BinaryEncryptionKeyException>(dump.Failure);
        Assert.False(dump.Decrypted);
        Assert.Null(dump.Root);
        Assert.Contains("not decrypted", dump.ToString(), StringComparison.Ordinal);
    }

    // --- DMP-10: without a type ----------------------------------------------------------------------

    [Fact]
    public void Dump_WithoutAType_UndoesEveryPhaseWithTheKeyAndVerifiesTheChecksum()
    {
        var dump = BinaryFormatDumper.Dump(Wire.Fixture("v1-protected.bin"), ProtectedOptions);

        Assert.Null(dump.Failure);
        Assert.True(dump.Decrypted);
        Assert.True(dump.ChecksumVerified);
        Assert.Null(dump.Root);
        Assert.Equal(Wire.Fixture("v0-primitives.bin").Length, dump.PayloadLength);
        Assert.Contains("verified", dump.ToString(), StringComparison.Ordinal);
        Assert.Contains("decrypted, header authenticated", dump.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_WithoutTheKey_StopsAtTheCiphertextAndDoesNotThrow()
    {
        var dump = BinaryFormatDumper.Dump(Wire.Fixture("v1-protected.bin"));

        Assert.False(dump.Decrypted);
        Assert.Null(dump.ChecksumVerified);
        Assert.Equal(0, dump.PayloadLength);
        Assert.Contains("stored bytes", dump.ToHex(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_BytesWithoutTheMagic_AreShownAsAVersion0Payload()
    {
        byte[] payload = Wire.Fixture("person-v0.bin");

        var dump = BinaryFormatDumper.Dump(payload);

        Assert.Equal(0, dump.FormatVersion);
        Assert.Null(dump.Header);
        Assert.Equal(payload.Length, dump.PayloadLength);
        Assert.StartsWith("@0000  01 24 00 00 00 04 41 64 61", dump.ToHex(), StringComparison.Ordinal);
        Assert.Equal("Ada", BinaryFormatDumper.Dump<Person>(payload).Root!.Children.Single(node => node.Name == "Name").Value!.Trim('"'));
    }

    // --- DMP-11: under the options' limits ----------------------------------------------------------

    [Fact]
    public void Dump_AHostileFrame_IsRefusedUnderTheLimitsAndCostsNoMoreThanTheRead()
    {
        byte[] frame = Wire.Container(declaredCount: 50_000_000, int32Values: 0);

        var dump = BinaryFormatDumper.Dump<List<int>>(frame);

        Assert.IsType<BinaryLimitException>(dump.Failure);
        AssertEx.AllocatesLessThan(64 * 1024, () => BinaryFormatDumper.Dump<List<int>>(frame));
    }

    // --- DMP-12: no key material -------------------------------------------------------------------

    [Fact]
    public void Dump_RendersNoKeyMaterial_ForEveryKeyProvider()
    {
        byte[] root = [.. Enumerable.Range(100, 32).Select(value => (byte)value)];
        var providers = new (string Name, BinarySerializerOptionsBuilder Options, byte[] Secret)[]
        {
            ("static", BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), Key, "k"), Key),
            ("delegate", BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), _ => Key, "k"), Key),
            ("hkdf", BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), new HkdfKeyProvider(root), "k"), root)
        };

        foreach (var (name, builder, secret) in providers)
        {
            var options = builder.Build();
            var dump = BinaryFormatDumper.DumpValue(new Person { Name = "Ada", Age = 36 }, options);

            Assert.True(dump.Decrypted, name);
            foreach (string rendering in new[] { dump.ToString(), dump.ToJson(), dump.ToHex(), BinaryFormatDumper.DumpHeader(new BinarySerializer(options).Serialize(1)) })
            {
                Assert.DoesNotContain(Convert.ToHexString(secret), rendering.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Convert.ToBase64String(secret), rendering, StringComparison.Ordinal);
            }
        }
    }

    // --- DMP-13, DMP-14: write and read back, and compare -------------------------------------------

    [Fact]
    public void DumpValue_IsTheDumpOfWhatSerializeWrites()
    {
        var value = CompatibilityTests.Collections;

        Assert.Equal(
            BinaryFormatDumper.Dump<FrozenCollections>(_serializer.Serialize(value)).ToString(),
            BinaryFormatDumper.DumpValue(value).ToString());
    }

    [Fact]
    public void Compare_TwoFramesOfTheSameValue_AreEqualEvenWithDifferentNonces()
    {
        var options = ProtectedOptions;
        var serializer = new BinarySerializer(options);

        byte[] first = serializer.Serialize(CompatibilityTests.Primitives);
        byte[] second = serializer.Serialize(CompatibilityTests.Primitives);

        Assert.NotEqual(first, second);
        Assert.Null(BinaryFormatDumper.Compare<FrozenPrimitives>(first, second, options));
    }

    [Fact]
    public void Compare_TwoDifferentValues_ReportsTheFirstDifferingNode()
    {
        var expected = new FrozenKeyed { Number = 5, Text = "keyed", Numbers = [1, 2, 3] };
        var actual = new FrozenKeyed { Number = 5, Text = "keyed", Numbers = [1, 9, 3] };

        var difference = BinaryFormatDumper.Compare<FrozenKeyed>(_serializer.Serialize(expected), _serializer.Serialize(actual));

        Assert.NotNull(difference);
        Assert.Equal("FrozenKeyed.Numbers.value[1]", difference.Path);
        Assert.Equal("2", difference.Expected!.Value);
        Assert.Equal("9", difference.Actual!.Value);
    }

    // --- DMP-15: the same on every machine ------------------------------------------------------------

    [Fact]
    public void Renderings_AreTheSameUnderEveryCulture()
    {
        var reference = Render();
        var saved = CultureInfo.CurrentCulture;

        try
        {
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures).Where((_, index) => index % 40 == 0).Append(Cultures.Specific))
            {
                CultureInfo.CurrentCulture = culture;
                Assert.Equal(reference, Render());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }

        static (string, string, string) Render()
        {
            var dump = BinaryFormatDumper.Dump<FrozenTimeAndSystem>(Wire.Fixture("v1-time-system.bin"));
            return (dump.ToString(), dump.ToJson() + dump.ToXml(), dump.ToHex());
        }
    }

    [Fact]
    public void Renderings_WriteTimesInUtc()
    {
        var dump = BinaryFormatDumper.Dump<FrozenTimeAndSystem>(Wire.Fixture("v1-time-system.bin"));

        Assert.Equal("2026-09-20T13:45:30.1230000Z", dump.Root!.Children.Single(node => node.Name == "Timestamp").Value);
        Assert.Equal("2026-09-20T18:45:30.0000000Z", dump.Root.Children.Single(node => node.Name == "Offset").Value);
    }

    [Fact]
    public void ToJsonAndToXml_ParseAndToHex_CoversEveryPayloadByteOnce()
    {
        foreach (var dump in FixtureDumps())
        {
            using var parsed = JsonDocument.Parse(dump.ToJson());
            Assert.Equal(dump.NodeCount, CountNodes(parsed.RootElement.GetProperty("root")));
            Assert.Equal(dump.NodeCount, System.Xml.Linq.XDocument.Parse(dump.ToXml()).Descendants("node").Count());

            var bytes = dump.ToHex()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(line => line[7..(7 + 48)].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToArray();

            Assert.Equal(dump.PayloadLength, bytes.Length);
        }
    }

    // --- DMP-16: the seam is off outside the dumper ---------------------------------------------------

    [Fact]
    public void TheTrace_IsSetOnlyByTheDiagnostics()
    {
        var assignments = SourceTree.ProductionFiles
            .Where(file => TraceAssignment.IsMatch(file.Value))
            .Select(file => file.Key)
            .ToArray();

        Assert.NotEmpty(assignments);
        Assert.All(assignments, file => Assert.Contains("/Diagnostics/", file, StringComparison.Ordinal));
        Assert.Null(new OperationState(SerializationLimits.Default, null, false, false, false).Trace);
    }

    [Fact]
    public void TheTrace_IsReportedToOnlyByTheEngine()
    {
        var callers = SourceTree.ProductionFiles
            .Where(file => TraceUse.IsMatch(file.Value))
            .Select(file => file.Key)
            .ToArray();

        Assert.NotEmpty(callers);
        Assert.All(callers, file => Assert.True(
            file.Contains("/Engine/", StringComparison.Ordinal) || file.Contains("/Diagnostics/", StringComparison.Ordinal),
            $"{file} reports to the trace."));
    }

    // --- DMP-17: the report itself ------------------------------------------------------------------

    [Theory]
    [InlineData("v1-keyed.bin", "keyed.txt")]
    [InlineData("v1-references.bin", "references.txt")]
    [InlineData("v1-protected.bin", "protected.txt")]
    public void ToString_OfAFixture_IsTheCommittedReport(string fixture, string expected)
    {
        BinaryDump dump = fixture switch
        {
            "v1-keyed.bin" => BinaryFormatDumper.Dump<FrozenKeyed>(Wire.Fixture(fixture)),
            "v1-references.bin" => BinaryFormatDumper.Dump<FrozenGraph>(Wire.Fixture(fixture)),
            _ => BinaryFormatDumper.Dump<FrozenPrimitives>(Wire.Fixture(fixture), ProtectedOptions)
        };

        string committed = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dumps", expected)).ReplaceLineEndings("\n");

        Assert.Equal(committed, dump.ToString());
    }

    // --- helpers ----------------------------------------------------------------------------------------

    private static BinarySerializerOptions Referencing => BinarySerializerOptions.Configure().PreserveReferences().Build();

    private static IEnumerable<BinaryDump> FixtureDumps()
    {
        yield return BinaryFormatDumper.Dump<FrozenPrimitives>(Wire.Fixture("v1-primitives.bin"));
        yield return BinaryFormatDumper.Dump<FrozenTimeAndSystem>(Wire.Fixture("v1-time-system.bin"));
        yield return BinaryFormatDumper.Dump<FrozenNumerics>(Wire.Fixture("v1-numerics.bin"));
        yield return BinaryFormatDumper.Dump<FrozenCollections>(Wire.Fixture("v1-collections.bin"));
        yield return BinaryFormatDumper.Dump<FrozenComposites>(Wire.Fixture("v1-composites.bin"));
        yield return BinaryFormatDumper.Dump<FrozenKeyed>(Wire.Fixture("v1-keyed.bin"));
        yield return BinaryFormatDumper.Dump<FrozenShape>(Wire.Fixture("v1-union.bin"));
        yield return BinaryFormatDumper.Dump<FrozenGraph>(Wire.Fixture("v1-references.bin"));
        yield return BinaryFormatDumper.Dump<FrozenAbsences>(Wire.Fixture("v1-absences.bin"));
        yield return BinaryFormatDumper.Dump<FrozenPrimitives>(Wire.Fixture("v1-protected.bin"), ProtectedOptions);
        yield return BinaryFormatDumper.Dump<FrozenPrimitives>(Wire.Fixture("v0-primitives.bin"));
        yield return BinaryFormatDumper.Dump<Person>(Wire.Fixture("person-v0.bin"));
        yield return BinaryFormatDumper.Dump<Person>(Wire.Fixture("person-v1.bin"));
    }

    private static IEnumerable<BinaryDumpNode> All(BinaryDumpNode node) =>
        node.Children.SelectMany(All).Prepend(node);

    private static int CountNodes(JsonElement node) =>
        1 + (node.TryGetProperty("children", out var children) ? children.EnumerateArray().Sum(CountNodes) : 0);

    private static readonly Regex ViperCatchPattern = new(
        @"catch\s*\(\s*Binary\w*Exception[^)]*\)");

    private static readonly Regex ViperCatchThatRethrowsPattern = new(
        @"catch\s*\(\s*Binary\w*Exception[^)]*\)\s*\{[^{}]*?\bthrow\s*;", RegexOptions.Singleline);

    private static readonly Regex AnyCatchPattern = new(@"\bcatch\s*[({]");

    private static readonly Regex TraceAssignment = new(@"\.Trace\s*=[^=]");

    private static readonly Regex TraceUse = new(@"\bTrace\s*(\?\.|is\b)|\btrace\??\.(Begin|Value|Label|Shape|Field|End|Null|Reference|Union|Continue)");
}
