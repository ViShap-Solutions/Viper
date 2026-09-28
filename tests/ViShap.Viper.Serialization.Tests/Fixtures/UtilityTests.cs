using System.Buffers;
using System.Globalization;
using ViShap.Viper.Crypto;
using Xunit.Sdk;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Pins UTIL-01…UTIL-15 and UTIL-17…UTIL-21. A helper with a bug passes every suite that uses it, so the
/// helpers are tested before anything is allowed to rely on them.
/// </summary>
/// <remarks>
/// The negative cases assert that a helper <em>fails</em>, and what it throws is whichever assertion
/// xUnit raised — <c>ContainsException</c>, <c>EqualException</c>, <c>TrueException</c>. They are a
/// real family with one base, so those cases name <see cref="XunitException"/> and nothing wider.
/// </remarks>
public class UtilityTests
{
    // --- UTIL-01: assertion helpers ------------------------------------------------------------

    [Fact]
    public void Throws_MatchingTypeAndMessage_Passes()
    {
        AssertEx.Throws<BinaryFormatException>(
            "expected", static () => throw new BinaryFormatException("an expected failure"));
    }

    [Fact]
    public void Throws_MatchingTypeButDifferentMessage_Fails()
    {
        Assert.ThrowsAny<XunitException>(() => AssertEx.Throws<BinaryFormatException>(
            "absent", static () => throw new BinaryFormatException("an expected failure")));
    }

    [Fact]
    public void Throws_DerivedExceptionType_Fails()
    {
        // BinaryLimitException derives from BinaryFormatException; a gate asking for the base must
        // not silently accept the subtype.
        Assert.ThrowsAny<XunitException>(() => AssertEx.Throws<BinaryFormatException>(
            "over", static () => throw new BinaryLimitException("over the limit")));
    }

    [Fact]
    public void AllocatesLessThan_SmallAllocation_Passes()
    {
        AssertEx.AllocatesLessThan(1024 * 1024, static () => _ = new byte[16]);
    }

    [Fact]
    public void AllocatesLessThan_LargeAllocation_Fails()
    {
        Assert.ThrowsAny<XunitException>(
            () => AssertEx.AllocatesLessThan(1024, static () => _ = new byte[4 * 1024 * 1024]));
    }

    [Fact]
    public void SameContents_IgnoresOrder()
    {
        AssertEx.SameContents([1, 2, 3], new[] { 3, 1, 2 });
        Assert.ThrowsAny<XunitException>(() => AssertEx.SameContents([1, 2, 3], new[] { 1, 2 }));
    }

    [Fact]
    public void BehaviouralHelpers_DrainTheContainer()
    {
        AssertEx.PopsInOrder([3, 2, 1], new Stack<int>([1, 2, 3]));
        AssertEx.DequeuesInOrder([1, 2, 3], new Queue<int>([1, 2, 3]));

        var queue = new PriorityQueue<string, int>();
        queue.Enqueue("low", 10);
        queue.Enqueue("high", 1);
        AssertEx.DequeuesInPriorityOrder(["high", "low"], queue);
    }

    // --- UTIL-03, UTIL-04: byte mutation and truncation -----------------------------------------

    [Fact]
    public void FlipByte_ChangesOnlyTheTargetedByte()
    {
        byte[] original = [1, 2, 3];

        byte[] mutated = Mutate.FlipByte(original, 1);

        Assert.Equal([1, 2, 3], original);
        Assert.Equal([1, unchecked((byte)~2), 3], mutated);
    }

    [Fact]
    public void SetByteAndSetInt32_ReplaceExactlyTheTargetedRange()
    {
        byte[] original = [0, 0, 0, 0, 0, 9];

        Assert.Equal([0, 7, 0, 0, 0, 9], Mutate.SetByte(original, 1, 7));
        Assert.Equal([1, 0, 0, 0, 0, 9], Mutate.SetInt32(original, 0, 1));
        Assert.Equal([0, 0, 0, 0, 0, 9], original);
    }

    [Fact]
    public void Truncate_KeepsThePrefix()
    {
        Assert.Equal([1, 2], Mutate.Truncate([1, 2, 3, 4], 2));
    }

    [Fact]
    public void Prefixes_EnumeratesEveryProperPrefixShortestFirst()
    {
        byte[][] prefixes = [.. Mutate.Prefixes([1, 2, 3])];

        Assert.Equal(2, prefixes.Length);
        Assert.Equal([1], prefixes[0]);
        Assert.Equal([1, 2], prefixes[1]);
    }

    [Fact]
    public void SevenBitEncoded_MatchesTheWireEncoding()
    {
        Assert.Equal([0x00], Mutate.SevenBitEncoded(0));
        Assert.Equal([0x7F], Mutate.SevenBitEncoded(127));
        Assert.Equal([0x80, 0x01], Mutate.SevenBitEncoded(128));
    }

    // --- UTIL-05…UTIL-07: stream doubles --------------------------------------------------------

    [Fact]
    public void NonSeekableStream_CannotSeekButReads()
    {
        using var stream = new NonSeekableStream([1, 2, 3]);

        Assert.False(stream.CanSeek);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => _ = stream.Length);

        byte[] buffer = new byte[3];
        stream.ReadExactly(buffer);
        Assert.Equal([1, 2, 3], buffer);
    }

    [Fact]
    public void PartialReadStream_ReturnsShortReadsWithoutLosingData()
    {
        using var stream = new PartialReadStream([1, 2, 3, 4, 5], chunkSize: 2);
        byte[] buffer = new byte[5];

        int first = stream.Read(buffer);

        Assert.Equal(2, first);
        stream.ReadExactly(buffer.AsSpan(first));
        Assert.Equal([1, 2, 3, 4, 5], buffer);
    }

    [Fact]
    public void FailingStream_RaisesIoExceptionPastItsThreshold()
    {
        using var stream = new FailingStream(bytesBeforeFailure: 4);

        stream.Write(new byte[4]);

        Assert.Throws<IOException>(() => stream.Write(new byte[1]));
    }

    [Fact]
    public void FailingStream_RaisesIoExceptionOnReadPastItsThreshold()
    {
        using var stream = new FailingStream(bytesBeforeFailure: 2);

        Assert.Throws<IOException>(() => stream.ReadExactly(new byte[8]));
    }

    [Fact]
    public void FailingContentStream_ServesItsContentUntilTheThreshold()
    {
        using var stream = new FailingContentStream([1, 2, 3, 4, 5, 6], bytesBeforeFailure: 4);

        byte[] buffer = new byte[4];
        stream.ReadExactly(buffer);

        Assert.Equal([1, 2, 3, 4], buffer);
        Assert.Throws<IOException>(() => stream.ReadExactly(new byte[1]));
    }

    [Fact]
    public void Concurrent_Race_RunsItsWorkersAtTheSameTime()
    {
        // The bodies can only clear a barrier of their own if they are all in flight together, so a
        // helper that quietly ran them one after another would time out here instead of passing.
        using var together = new Barrier(Concurrent.Workers);

        bool[] met = Concurrent.Race(_ => together.SignalAndWait(TimeSpan.FromSeconds(30)));

        Assert.Equal(Concurrent.Workers, met.Length);
        Assert.All(met, Assert.True);
    }

    [Fact]
    public void Concurrent_Race_RethrowsWhatAWorkerThrew()
    {
        AssertEx.Throws<BinaryTypeException>(
            "worker 3",
            () => Concurrent.Race(worker =>
                worker == 3 ? throw new BinaryTypeException("worker 3 refused") : worker));
    }

    [Fact]
    public void Concurrent_Race_ReturnsEachResultUnderItsOwnIndex()
    {
        int[] squares = Concurrent.Race(worker => worker * worker);

        Assert.Equal([.. Enumerable.Range(0, Concurrent.Workers).Select(worker => worker * worker)], squares);
    }

    [Fact]
    public void Cultures_Specific_IsACultureThisHostCanResolve()
    {
        // Whatever the host provides, the value is usable and its name is what a payload would
        // carry. On a build with no globalization data that is the invariant culture, and the point
        // is that this still holds rather than throwing.
        var culture = Cultures.Specific;

        Assert.NotNull(culture);
        Assert.Same(culture, Cultures.Specific);
        Assert.Equal(culture, CultureInfo.GetCultureInfo(culture.Name));
    }

    // --- UTIL-02: the frame builders produce bytes a real reader accepts -------------------------

    [Fact]
    public void Header_MatchesWhatTheWriterProduces()
    {
        byte[] produced = new BinarySerializer().Serialize(0x11223344);

        Assert.Equal(Wire.PlainHeaderLength, Wire.Header(4).Length);
        Assert.Equal(Wire.Header(4), produced[..Wire.PlainHeaderLength]);
    }

    [Fact]
    public void Frame_IsAcceptedByARealReader()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer => writer.Write(123)));

        Assert.Equal(123, new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Header_RecordsPreserveReferencesAtTheDocumentedOffset()
    {
        byte[] header = Wire.Header(0, preserveReferences: true);

        Assert.Equal(1, header[Wire.PreserveReferencesOffset]);
        Assert.Equal(0, Wire.Header(0)[Wire.PreserveReferencesOffset]);
    }

    [Fact]
    public void KeyedFields_IsAcceptedByARealReader()
    {
        byte[] frame = Wire.KeyedFields(3);

        Assert.NotNull(new BinarySerializer().Deserialize<EmptyContract>(frame));
    }

    [Fact]
    public void FrameWith_AtItsDefaults_ProducesThePlainFrame()
    {
        byte[] body = Wire.Payload(writer => writer.Write(123));

        Assert.Equal(Wire.Frame(body), Wire.FrameWith(body));
    }

    [Fact]
    public void FrameWith_IsAcceptedByARealReader()
    {
        byte[] frame = Wire.FrameWith(Wire.Payload(writer => writer.Write(123)));

        Assert.Equal(123, new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void FrameWith_PlacesTheChecksumBeforeThePayload()
    {
        byte[] body = Wire.Payload(writer => writer.Write(123));

        byte[] frame = Wire.FrameWith(body, checksumAlgorithm: 1, checksum: [1, 2, 3, 4]);

        Assert.Equal(Wire.PlainHeaderLength + 4 + body.Length, frame.Length);
        Assert.Equal<byte[]>([1, 2, 3, 4], frame[Wire.PlainHeaderLength..(Wire.PlainHeaderLength + 4)]);
        Assert.Equal(body, frame[(Wire.PlainHeaderLength + 4)..]);
    }

    [Fact]
    public void ReadHeader_RecoversEveryFieldTheFrameBuilderWrote()
    {
        byte[] body = Wire.Payload(writer => writer.Write(123));

        var header = Wire.ReadHeader(Wire.FrameWith(
            body,
            compression: 255, customCompressionName: "zip",
            checksumAlgorithm: 255, customChecksumName: "sum",
            encryption: 255, customEncryptionName: "box",
            keyId: "ring",
            preserveReferences: true,
            uncompressedLength: 11,
            compressedLength: 22,
            onDiskLength: 33,
            checksum: [7, 8]));

        Assert.Equal(1, header.Version);
        Assert.Equal(255, header.Compression);
        Assert.Equal("zip", header.CustomCompressionName);
        Assert.Equal(255, header.ChecksumAlgorithm);
        Assert.Equal("sum", header.CustomChecksumName);
        Assert.Equal(255, header.Encryption);
        Assert.Equal("box", header.CustomEncryptionName);
        Assert.Equal("ring", header.KeyId);
        Assert.True(header.PreserveReferences);
        Assert.Equal(11, header.UncompressedLength);
        Assert.Equal(22, header.CompressedLength);
        Assert.Equal(33, header.OnDiskLength);
        Assert.Equal<byte[]>([7, 8], header.Checksum);
    }

    [Fact]
    public void ReadHeader_OnAPlainFrame_ReportsTheHeaderLengthThePayloadStartsAt()
    {
        byte[] body = Wire.Payload(writer => writer.Write(123));

        var header = Wire.ReadHeader(Wire.Frame(body));

        Assert.Equal(Wire.PlainHeaderLength, header.HeaderLength);
        Assert.Equal(body.Length, header.OnDiskLength);
    }

    // --- UTIL-10: the keyed and reference frame builders -----------------------------------------

    [Fact]
    public void KeyedBody_IsAcceptedByARealReader()
    {
        byte[] frame = Wire.Frame(
        [
            .. Wire.NotNull,
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(1, Wire.Payload(writer => writer.Write(11))),
                new Wire.KeyedField(3, Wire.Payload(writer => writer.Write(33)))
            ])
        ]);

        var result = new BinarySerializer().Deserialize<OuterKeys>(frame)!;

        Assert.Equal(11, result.First);
        Assert.Equal(33, result.Last);
    }

    [Fact]
    public void KeyedBody_DeclaresTheLengthsItWasGivenRatherThanTheRealOnes()
    {
        byte[] body = Wire.KeyedBody(
            [new Wire.KeyedField(2, [1, 2, 3, 4], DeclaredLength: 1024)],
            declaredFieldCount: 9);

        Assert.Equal(9, body[0]);                          // the field count it was told to claim
        Assert.Equal(2, body[1]);                          // the key
        Assert.Equal(1024, BitConverter.ToInt32(body, 2)); // the length it was told to claim
        Assert.Equal<byte[]>([1, 2, 3, 4], body[6..]);     // the bytes actually present
    }

    [Fact]
    public void ReferenceFrame_IsTheMarkerThenTheId()
    {
        byte[] frame = Wire.ReferenceFrame(1, 258);

        Assert.Equal(5, frame.Length);
        Assert.Equal(1, frame[0]);
        Assert.Equal(258, BitConverter.ToInt32(frame, 1));
    }

    [Fact]
    public void DoesNotContainBytes_FindsASubsequenceWhereverItSits()
    {
        AssertEx.DoesNotContainBytes([1, 2, 3], [4, 5]);
        Assert.ThrowsAny<XunitException>(
            static () => AssertEx.DoesNotContainBytes([1, 2, 3, 4], [3, 4]));
    }

    // --- UTIL-11: the multi-segment sequence builder ---------------------------------------------

    [Fact]
    public void Sequences_Of_ChainsTheSegmentsInOrder()
    {
        var sequence = Sequences.Of([1, 2], [3], [4, 5]);

        Assert.False(sequence.IsSingleSegment);
        Assert.Equal(5, sequence.Length);
        Assert.Equal([1, 2, 3, 4, 5], sequence.ToArray());
    }

    [Fact]
    public void Sequences_Of_NoSegments_IsEmpty()
    {
        Assert.Equal(0, Sequences.Of<int>().Length);
        Assert.Equal([], Sequences.Of<int>([]).ToArray());
    }

    // --- UTIL-12: the value frame builders --------------------------------------------------------

    [Fact]
    public void Container_DeclaresTheCountItWasGivenRatherThanTheBodyItHolds()
    {
        byte[] frame = Wire.Container(declaredCount: 9, int32Values: 2);
        byte[] body = frame[Wire.PlainHeaderLength..];

        Assert.Equal(1, body[0]);                       // the container is non-null
        Assert.Equal(9, BitConverter.ToInt32(body, 1)); // the count it was told to claim
        Assert.Equal(1 + 4 + (2 * 4), body.Length);     // two four-byte values actually present
    }

    [Fact]
    public void Container_IsAcceptedByARealReader()
    {
        var restored = new BinarySerializer().Deserialize<List<int>>(Wire.Container(3, 3));

        Assert.Equal([0, 1, 2], restored);
    }

    [Fact]
    public void StringValue_DeclaresTheLengthItWasGivenRatherThanTheBytesItHolds()
    {
        byte[] body = Wire.StringValue(200, 0x61, 0x62)[Wire.PlainHeaderLength..];

        Assert.Equal(1, body[0]);                       // the string is non-null
        Assert.Equal<byte[]>([0xC8, 0x01], body[1..3]); // 200, 7-bit encoded
        Assert.Equal<byte[]>([0x61, 0x62], body[3..]);
    }

    [Fact]
    public void StringValue_IsAcceptedByARealReader()
    {
        byte[] frame = Wire.StringValue(2, 0x61, 0x62);

        Assert.Equal("ab", new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void BitArrayValue_IsTheBitCountThenTheBlob()
    {
        byte[] body = Wire.BitArrayValue(declaredBits: 12, dataBytes: 2)[Wire.PlainHeaderLength..];

        Assert.Equal(1, body[0]);                        // the BitArray is non-null
        Assert.Equal(12, BitConverter.ToInt32(body, 1)); // the bit count
        Assert.Equal(2, body[5]);                        // the blob length
        Assert.Equal(1 + 4 + 1 + 2, body.Length);
    }

    [Fact]
    public void BitArrayValue_IsAcceptedByARealReader()
    {
        var restored = new BinarySerializer()
            .Deserialize<System.Collections.BitArray>(Wire.BitArrayValue(12, 2));

        Assert.Equal(12, restored!.Length);
    }

    [Fact]
    public void MultiDimensionalArray_IsTheRankThenTheDimensionsThenTheElements()
    {
        byte[] body = Wire.MultiDimensionalArray([2, 3], int32Elements: 6)[Wire.PlainHeaderLength..];

        Assert.Equal(1, body[0]);                       // the array is non-null
        Assert.Equal(2, BitConverter.ToInt32(body, 1)); // the rank
        Assert.Equal(2, BitConverter.ToInt32(body, 5));
        Assert.Equal(3, BitConverter.ToInt32(body, 9));
        Assert.Equal(1 + 4 + (2 * 4) + (6 * 4), body.Length);
    }

    [Fact]
    public void MultiDimensionalArray_DeclaresTheRankItWasGiven()
    {
        byte[] body = Wire.MultiDimensionalArray([2, 3], int32Elements: 0, declaredRank: 7)
            [Wire.PlainHeaderLength..];

        Assert.Equal(7, BitConverter.ToInt32(body, 1));
    }

    [Fact]
    public void MultiDimensionalArray_IsAcceptedByARealReader()
    {
        var restored = new BinarySerializer()
            .Deserialize<int[,]>(Wire.MultiDimensionalArray([2, 3], int32Elements: 6));

        Assert.Equal(2, restored!.GetLength(0));
        Assert.Equal(3, restored.GetLength(1));
        Assert.Equal(5, restored[1, 2]);
    }

    // --- UTIL-13: the write-only stream double ----------------------------------------------------

    [Fact]
    public void WriteOnlyStream_AcceptsWritesAndRefusesReads()
    {
        using var stream = new WriteOnlyStream();

        stream.Write([1, 2, 3]);
        stream.Position = 0;

        Assert.True(stream.CanSeek);
        Assert.False(stream.CanRead);
        Assert.Equal(3, stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.ReadByte());
    }

    // --- UTIL-09: the committed compatibility fixtures -------------------------------------------

    [Fact]
    public void Fixture_LoadsTheCommittedBytesRatherThanRegeneratingThem()
    {
        // The expected bytes are transcribed from §22 here as they are in the file, so a writer
        // change cannot quietly move both sides at once.
        Assert.Equal<byte[]>(
            [0x01, 0x24, 0x00, 0x00, 0x00, 0x01, 0x03, 0x41, 0x64, 0x61],
            Wire.Fixture("person-v0.bin"));

        byte[] framed = Wire.Fixture("person-v1.bin");

        Assert.Equal(Wire.PlainHeaderLength + 10, framed.Length);
        Assert.Equal(Wire.Fixture("person-v0.bin"), framed[Wire.PlainHeaderLength..]);
        Assert.Equal(Wire.Magic, BitConverter.ToInt32(framed));
    }

    // --- UTIL-14: the algorithm doubles that count and record --------------------------------------

    [Fact]
    public void IdentityCompression_CountsEveryCallItReceives()
    {
        var compression = new IdentityCompression();
        byte[] destination = new byte[3];

        compression.Compress([1, 2, 3], destination);
        compression.Decompress([4, 5, 6], destination);
        compression.Decompress([7, 8, 9], destination);

        Assert.Equal(1, compression.CompressCalls);
        Assert.Equal(2, compression.DecompressCalls);
        Assert.Equal<byte[]>([7, 8, 9], destination);
    }

    [Fact]
    public void RecordingKeyProvider_HandsOutAnOwnedCopyAndRecordsTheIdItWasAsked()
    {
        byte[] material = [1, 2, 3, 4];
        var provider = new RecordingKeyProvider(material);

        using (var first = provider.Resolve("ring-7"))
            Assert.Equal(material, first.Span.ToArray());

        var second = provider.Resolve(null);

        Assert.Equal<string?[]>(["ring-7", null], [.. provider.RequestedIds]);
        Assert.Equal(2, provider.Issued.Count);
        Assert.NotSame(provider.Issued[0], provider.Issued[1]);

        // Disposing the first key left the second one and the caller's material untouched.
        Assert.Equal(material, second.Span.ToArray());
        Assert.Equal<byte[]>([1, 2, 3, 4], material);
    }

    // --- UTIL-17: the byte oracle ------------------------------------------------------------------

    [Fact]
    public void OracleCompare_ChangedByte_ReportsBothOutputsInHexAndTheOffset()
    {
        byte[] recorded = [0x42, 0x10, 0x20, 0x30];
        byte[] written = [0x42, 0x10, 0x21, 0x30];
        var oracle = Oracle.Parse([Oracle.Line("Case.Method#0", recorded)]);

        string report = Oracle.Compare(oracle, [("Case.Method#0", written)]);

        Assert.Contains("Case.Method#0: first difference at offset 2", report);
        Assert.Contains("expected 42102030", report);
        Assert.Contains("actual   42102130", report);
    }

    [Fact]
    public void OracleCompare_DifferentLength_ReportsWhereTheCommonPrefixEnds()
    {
        var oracle = Oracle.Parse([Oracle.Line("Case.Method#0", [1, 2, 3])]);

        string report = Oracle.Compare(oracle, [("Case.Method#0", [1, 2, 3, 4])]);

        Assert.Contains("identical for 3 bytes, then expected 3 bytes and actual 4", report);
    }

    [Fact]
    public void OracleCompare_IdenticalOutput_ReportsNothing()
    {
        var oracle = Oracle.Parse([Oracle.Line("Case.Method#0", [1, 2, 3])]);

        Assert.Equal("", Oracle.Compare(oracle, [("Case.Method#0", [1, 2, 3])]));
    }

    [Fact]
    public void OracleCompare_MissingUnexpectedAndRepeatedCases_AreEachReported()
    {
        var oracle = Oracle.Parse(
        [
            "# a comment",
            "",
            Oracle.Line("Case.Recorded#0", [1]),
            Oracle.Line("Case.Kept#0", [2]),
        ]);

        string report = Oracle.Compare(
            oracle, [("Case.Kept#0", [2]), ("Case.Kept#0", [2]), ("Case.New#0", [3])]);

        Assert.Contains("Case.Recorded#0: recorded in the oracle but not produced", report);
        Assert.Contains("Case.New#0: not in the oracle", report);
        Assert.Contains("Case.Kept#0: produced twice", report);
    }

    [Fact]
    public void OracleCompare_AnyAdmissibleOutput_PassesAndAnyOtherIsReportedAgainstEachOfThem()
    {
        var oracle = Oracle.Parse(
        [
            Oracle.Line("Case.Unordered#0", [1, 2]),
            Oracle.Alternative("Case.Unordered#0", [2, 1]),
        ]);

        Assert.Equal("", Oracle.Compare(oracle, [("Case.Unordered#0", [1, 2])]));
        Assert.Equal("", Oracle.Compare(oracle, [("Case.Unordered#0", [2, 1])]));

        string report = Oracle.Compare(oracle, [("Case.Unordered#0", [2, 2])]);

        Assert.Contains("expected 0102", report);
        Assert.Contains("expected 0201", report);
        Assert.Contains("actual   0202", report);
    }

    [Fact]
    public void OracleCompare_AHostDependentCase_AcceptsAnyOutputButMustBeProduced()
    {
        var oracle = Oracle.Parse(
        [
            Oracle.HostDependent("Case.Culture#1", "the first specific culture of the host"),
            Oracle.Line("Case.Culture#0", [7]),
        ]);

        Assert.Equal("the first specific culture of the host", oracle["Case.Culture#1"].HostDependence);
        Assert.Equal("", Oracle.Compare(oracle, [("Case.Culture#0", [7]), ("Case.Culture#1", [1, 2, 3])]));
        Assert.Contains(
            "Case.Culture#1: recorded in the oracle but not produced",
            Oracle.Compare(oracle, [("Case.Culture#0", [7])]));
    }

    [Fact]
    public void OracleParse_AnAlternativeOfAHostDependentCaseOrAHostLineRepeatingACase_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Oracle.Parse(
        [
            Oracle.HostDependent("Case.Zone#0", "names localized by the operating system"),
            Oracle.Alternative("Case.Zone#0", [1]),
        ]));

        Assert.Throws<InvalidOperationException>(() => Oracle.Parse(
        [
            Oracle.Line("Case.Zone#0", [1]),
            Oracle.HostDependent("Case.Zone#0", "names localized by the operating system"),
        ]));
    }

    [Fact]
    public void OracleParse_AnAlternativeAwayFromItsCase_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Oracle.Parse(
        [
            Oracle.Line("Case.First#0", [1]),
            Oracle.Line("Case.Second#0", [2]),
            Oracle.Alternative("Case.First#0", [3]),
        ]));
    }

    [Fact]
    public void OracleParse_AnAlternativeRepeatingARecordedOutput_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Oracle.Parse(
        [
            Oracle.Line("Case.First#0", [1]),
            Oracle.Alternative("Case.First#0", [1]),
        ]));
    }

    [Fact]
    public void OracleParse_RepeatedKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Oracle.Parse([Oracle.Line("Case.Method#0", [1]), Oracle.Line("Case.Method#0", [2])]));
    }

    [Fact]
    public void OracleNormalize_EncryptedFrame_BecomesItsHeaderAndThePayloadItCarries()
    {
        byte[] key = [.. Enumerable.Range(0, 32).Select(value => (byte)value)];
        var encrypting = new BinarySerializer(
            BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), key).Build());
        var value = new List<string> { "a value long enough", "to span more than one block of the cipher" };

        byte[] first = encrypting.Serialize(value);
        byte[] second = encrypting.Serialize(value);
        byte[] payload = new BinarySerializer().Serialize(value)[Wire.PlainHeaderLength..];

        Assert.NotEqual(first, second);

        byte[] normalized = Oracle.Normalize(first, key);
        int headerLength = Wire.ReadHeader(first).HeaderLength;

        Assert.Equal(normalized, Oracle.Normalize(second, key));
        Assert.Equal(first[..headerLength], normalized[..headerLength]);
        Assert.Equal(payload, normalized[headerLength..]);
    }

    [Fact]
    public void OracleNormalize_WithoutAKey_ReturnsTheOutputUnchanged()
    {
        byte[] output = new BinarySerializer().Serialize(7);

        Assert.Same(output, Oracle.Normalize(output, null));
    }

    [Fact]
    public void OracleRecorder_OutsideACollection_WritesWhatSerializeWritesAndRecordsNothing()
    {
        var serializer = new BinarySerializer();

        Assert.Equal(serializer.Serialize("text"), serializer.SerializeRecorded("text"));
    }

    [Fact]
    public void OracleRecorder_InsideACollection_RecordsACopyOfEveryWriteInOrder()
    {
        var serializer = new BinarySerializer();
        byte[]? handed = null;

        var recorded = OracleRecorder.Collect(() =>
        {
            handed = serializer.SerializeRecorded(1);
            serializer.SerializeRecorded("two");
            handed[^1] ^= 0xFF;
        });

        Assert.Equal(2, recorded.Count);
        Assert.Equal(serializer.Serialize(1), recorded[0]);
        Assert.Equal(serializer.Serialize("two"), recorded[1]);
        Assert.Empty(OracleRecorder.Collect(() => { }));
    }

    // --- UTIL-18: the stream that ends at a frame boundary ---------------------------------------

    [Fact]
    public void FrameBoundStream_ServesTheBytesUpToTheBoundary()
    {
        var stream = new FrameBoundStream([1, 2, 3, 4, 5], boundary: 3);
        byte[] buffer = new byte[3];

        Assert.Equal(2, stream.Read(buffer.AsSpan(0, 2)));
        Assert.Equal(1, stream.Read(buffer.AsSpan(2, 1)));
        Assert.Equal([1, 2, 3], buffer);
        Assert.Equal(3, stream.Taken);
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public void FrameBoundStream_FailsAReadThatAsksPastTheBoundary()
    {
        var stream = new FrameBoundStream([1, 2, 3, 4, 5], boundary: 3);

        Assert.Throws<InvalidOperationException>(() => stream.Read(new byte[4]));
        Assert.Throws<InvalidOperationException>(() => stream.ReadAsync(new byte[4]).AsTask().GetAwaiter().GetResult());
        Assert.Equal(0, stream.Taken);
    }

    [Fact]
    public void FrameBoundStream_EndsWhenItsContentDoes()
    {
        var stream = new FrameBoundStream([1, 2], boundary: 5);

        Assert.Equal(2, stream.Read(new byte[5]));
        Assert.Equal(0, stream.Read(new byte[3]));
    }

    // --- UTIL-19: the pipe reader that delivers its content in chunks ----------------------------

    [Fact]
    public async Task ChunkedPipeReader_DeliversAChunkOnlyOnceEverythingShownWasExamined()
    {
        var pipe = new ChunkedPipeReader([1, 2, 3, 4, 5, 6, 7], chunkSize: 3);

        var first = await pipe.ReadAsync();
        Assert.Equal([1, 2, 3], first.Buffer.ToArray());
        pipe.AdvanceTo(first.Buffer.GetPosition(1), first.Buffer.GetPosition(2));

        var sameBytes = await pipe.ReadAsync();
        Assert.Equal([2, 3], sameBytes.Buffer.ToArray());
        pipe.AdvanceTo(sameBytes.Buffer.Start, sameBytes.Buffer.End);

        var more = await pipe.ReadAsync();
        Assert.Equal([2, 3, 4, 5, 6], more.Buffer.ToArray());
        Assert.False(more.Buffer.IsSingleSegment);
        Assert.False(more.IsCompleted);
        pipe.AdvanceTo(more.Buffer.End);

        var last = await pipe.ReadAsync();
        Assert.Equal([7], last.Buffer.ToArray());
        Assert.True(last.IsCompleted);
        pipe.AdvanceTo(last.Buffer.End);

        Assert.Equal(7, pipe.Consumed);
    }

    [Fact]
    public async Task ChunkedPipeReader_RefusesASecondReadBeforeAdvancing()
    {
        var pipe = new ChunkedPipeReader([1, 2, 3], chunkSize: 1);

        await pipe.ReadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipe.ReadAsync().AsTask());
    }

    [Fact]
    public async Task ChunkedPipeReader_ThatNeverCompletes_WaitsUntilTheReadIsCancelled()
    {
        var pipe = new ChunkedPipeReader([1], chunkSize: 1, completeAtEnd: false);
        var first = await pipe.ReadAsync();
        pipe.AdvanceTo(first.Buffer.Start, first.Buffer.End);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipe.ReadAsync(cancellation.Token).AsTask());
    }

    [Fact]
    public async Task ChunkedPipeReader_ReportsACancelledPendingRead()
    {
        var pipe = new ChunkedPipeReader([1, 2], chunkSize: 1);
        pipe.CancelPendingRead();

        var result = await pipe.ReadAsync();

        Assert.True(result.IsCanceled);
    }

    // --- UTIL-20: the buffer writer that hands out small spans ----------------------------------

    [Fact]
    public void StingyBufferWriter_HandsOutAtMostItsLimitAndCommitsInOrder()
    {
        var writer = new StingyBufferWriter(limit: 2);

        var first = writer.GetSpan(10);
        first[0] = 1;
        first[1] = 2;
        writer.Advance(2);
        var second = writer.GetSpan(10);
        second[0] = 3;
        writer.Advance(1);

        Assert.Equal(2, first.Length);
        Assert.Equal([1, 2, 3], writer.Written);
        Assert.Equal(2, writer.Requests);
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(1));
    }

    [Fact]
    public void StingyBufferWriter_WithALimitOfZero_HandsOutAnEmptySpan()
    {
        var writer = new StingyBufferWriter(limit: 0);

        Assert.True(writer.GetSpan(16).IsEmpty);
        Assert.True(writer.GetMemory(16).IsEmpty);
    }

    // --- UTIL-21: the operation state held on the heap -----------------------------------------

    [Fact]
    public void OperationBox_HoldsAFreshStateUnderTheLimitsItIsGiven()
    {
        var defaults = new OperationBox();
        var tight = new OperationBox(ViShap.Viper.Security.SerializationLimits.Default with { MaxDepth = 3 });

        Assert.Same(ViShap.Viper.Security.SerializationLimits.Default, defaults.State.Limits);
        Assert.Equal(3, tight.State.Limits.MaxDepth);
        Assert.Equal(0, defaults.State.Budget.TotalElements);
        Assert.False(defaults.State.PreserveReferences);
    }

    [Fact]
    public void OperationBox_IsTheStateAReaderHandedItChargesAndEachBoxIsItsOwn()
    {
        var operation = new OperationBox();
        var other = new OperationBox();
        var reader = new ViShap.Viper.Io.WireReader(new byte[] { 3, 0, 0, 0 }, ref operation.State);

        reader.ReadCount(ViShap.Viper.Io.CountKind.Collection, "Collection count");

        Assert.Equal(3, operation.State.Budget.TotalElements);
        Assert.Equal(0, other.State.Budget.TotalElements);
    }
}
