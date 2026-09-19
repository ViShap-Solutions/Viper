namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Pins UTIL-01…UTIL-09. A helper with a bug passes every suite that uses it, so the helpers are
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
}
