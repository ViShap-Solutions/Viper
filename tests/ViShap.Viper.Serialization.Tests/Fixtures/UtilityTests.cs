using System.Buffers;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Pins UTIL-01…UTIL-14. A helper with a bug passes every suite that uses it, so the helpers are
/// tested before anything is allowed to rely on them.
/// </summary>
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
        Assert.ThrowsAny<Exception>(() => AssertEx.Throws<BinaryFormatException>(
            "absent", static () => throw new BinaryFormatException("an expected failure")));
    }

    [Fact]
    public void Throws_DerivedExceptionType_Fails()
    {
        // BinaryLimitException derives from BinaryFormatException; a gate asking for the base must
        // not silently accept the subtype.
        Assert.ThrowsAny<Exception>(() => AssertEx.Throws<BinaryFormatException>(
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
        Assert.ThrowsAny<Exception>(
            () => AssertEx.AllocatesLessThan(1024, static () => _ = new byte[4 * 1024 * 1024]));
    }

    [Fact]
    public void SameContents_IgnoresOrder()
    {
        AssertEx.SameContents([1, 2, 3], new[] { 3, 1, 2 });
        Assert.ThrowsAny<Exception>(() => AssertEx.SameContents([1, 2, 3], new[] { 1, 2 }));
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
        Assert.ThrowsAny<Exception>(
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
}
