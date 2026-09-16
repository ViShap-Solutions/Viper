using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Schema;

public sealed class SchemaVersioningTests
{
    [Fact]
    public void KEY01_SameSchemaRoundTrips()
    {
        var value = new ContractV1 { Name = "Ada", Age = 37 };
        var actual = TestHelpers.RoundTrip(value);

        Assert.Equal(value.Name, actual.Name);
        Assert.Equal(value.Age, actual.Age);
    }

    [Fact]
    public void KEY02_NewReaderRemovedPreviouslySerializedMemberSkipsOldMember()
    {
        var oldSchema = new ContractV2 { Name = "Ada", Age = 37, NewField = "legacy" };
        var bytes = new BinarySerializer().Serialize(oldSchema);

        var actual = new BinarySerializer().Deserialize<ContractV1>(bytes)!;

        Assert.Equal("Ada", actual.Name);
        Assert.Equal(37, actual.Age);
    }

    [Fact]
    public void KEY03_NewReaderAddsMemberMissingKeyLeavesClrDefault()
    {
        var oldSchema = new ContractV1 { Name = "Ada", Age = 37 };
        var bytes = new BinarySerializer().Serialize(oldSchema);

        var actual = new BinarySerializer().Deserialize<ContractV2>(bytes)!;

        Assert.Equal("Ada", actual.Name);
        Assert.Equal(37, actual.Age);
        Assert.Equal("default", actual.NewField);
    }

    [Fact]
    public void KEY04_UnknownKeyBetweenKnownKeysIsSkipped()
    {
        var original = new ContractV1 { Name = "Ada", Age = 37 };
        var payload = ExtractPayload(new BinarySerializer().Serialize(original));
        var fields = ReadFields(payload);
        fields.Insert(1, new KeyedField(99, SerializePayload(123456789)));

        var bytes = RebuildV1(fields);
        var actual = new BinarySerializer().Deserialize<ContractV1>(bytes)!;

        Assert.Equal("Ada", actual.Name);
        Assert.Equal(37, actual.Age);
    }

    [Fact]
    public void KEY05_UnknownNestedValueIsSkippedAndFollowingKnownFieldRemainsReadable()
    {
        var original = new ContractV1 { Name = "Ada", Age = 37 };
        var payload = ExtractPayload(new BinarySerializer().Serialize(original));
        var fields = ReadFields(payload);
        fields.Insert(1, new KeyedField(99, SerializePayload(new Person
        {
            Name = "nested",
            Age = 7,
            Address = new Address { City = "Yerevan", Street = "Unknown" }
        })));

        var bytes = RebuildV1(fields);
        var actual = new BinarySerializer().Deserialize<ContractV1>(bytes)!;

        Assert.Equal("Ada", actual.Name);
        Assert.Equal(37, actual.Age);
    }

    [Fact]
    public void KEY06_UnknownLargeBoundedBlobIsSkipped()
    {
        var original = new ContractV1 { Name = "Ada", Age = 37 };
        var payload = ExtractPayload(new BinarySerializer().Serialize(original));
        var fields = ReadFields(payload);
        var blob = new byte[1024];
        for (var i = 0; i < blob.Length; i++)
            blob[i] = (byte)(i * 31);

        fields.Insert(1, new KeyedField(99, SerializePayload(blob)));

        var limits = BinarySerializerOptions.Configure()
            .WithLimits(new DeserializationLimits
            {
                MaxMessageBytes = 8 * 1024,
                MaxStringLength = 4096,
                MaxByteBlobLength = 2048,
                MaxArrayLength = 4096,
                MaxCollectionLength = 4096,
                MaxDictionaryEntries = 4096,
                MaxTotalElements = 4096,
                MaxDepth = 64
            })
            .Build();

        var actual = new BinarySerializer(limits).Deserialize<ContractV1>(RebuildV1(fields))!;

        Assert.Equal("Ada", actual.Name);
        Assert.Equal(37, actual.Age);
    }

    [Fact]
    public void KEY07_UnknownKeyWithTruncatedDeclaredPayloadIsFormatError()
    {
        var original = new ContractV1 { Name = "Ada", Age = 37 };
        var payload = ExtractPayload(new BinarySerializer().Serialize(original));
        var fields = ReadFields(payload);
        fields.Insert(1, new KeyedField(99, SerializePayload(123)) { DeclaredLengthOverride = 1024 });

        Assert.Throws<BinaryFormatException>(() =>
        {
            var bytes = RebuildV1(fields);
            _ = new BinarySerializer().Deserialize<ContractV1>(bytes);
        });
    }

    [Fact]
    public void KEY08_DuplicateInputKeyIsRejectedDeterministically()
    {
        var original = new ContractV1 { Name = "Ada", Age = 37 };
        var payload = ExtractPayload(new BinarySerializer().Serialize(original));
        var fields = ReadFields(payload);
        fields.Insert(1, fields[0]);

        Assert.Throws<BinaryFormatException>(() =>
        {
            var bytes = RebuildV1(fields);
            _ = new BinarySerializer().Deserialize<ContractV1>(bytes);
        });
    }

    [Fact]
    public void KEY09_InvalidKeyVarintIsRejected()
    {
        var original = new ContractV1 { Name = "Ada", Age = 37 };
        var payload = ExtractPayload(new BinarySerializer().Serialize(original));
        var fields = ReadFields(payload);
        var malformed = new KeyedField(0, Array.Empty<byte>())
        {
            RawKeyEncoding = [0xFF, 0xFF, 0xFF, 0xFF, 0x08]
        };
        fields.Insert(1, malformed);

        Assert.Throws<BinaryFormatException>(() =>
        {
            var bytes = RebuildV1(fields);
            _ = new BinarySerializer().Deserialize<ContractV1>(bytes);
        });
    }

    [Fact]
    public void KEY10_KeyBoundaryValuesRoundTripAndDispatchCorrectly()
    {
        var value = new BoundaryContract
        {
            K127 = 127,
            K128 = 128,
            K16383 = 16383,
            K16384 = 16384,
            KMax = int.MaxValue
        };

        var actual = TestHelpers.RoundTrip(value);

        Assert.Equal(127, actual.K127);
        Assert.Equal(128, actual.K128);
        Assert.Equal(16383, actual.K16383);
        Assert.Equal(16384, actual.K16384);
        Assert.Equal(int.MaxValue, actual.KMax);
    }

    [Fact]
    public void KEY11_KeyedPolymorphicMember()
    {
        var actual = TestHelpers.RoundTrip(new KeyedAnimal { Value = new Dog { Name = "d", BarkVolume = 9 } });

        var dog = Assert.IsType<Dog>(actual.Value);
        Assert.Equal("d", dog.Name);
        Assert.Equal(9, dog.BarkVolume);
    }

    [Fact]
    public void KEY12_CycleCrossingKeyedBoundaryIsRejected()
    {
        var node = new KeyedCycle();
        node.Next = node;

        Assert.Throws<BinaryTypeException>(() => new BinarySerializer().Serialize(node));
    }

    [Fact]
    public void KEY13_PreserveReferencesAcrossKeyedBoundary()
    {
        var shared = new Dog { Name = "d", BarkVolume = 3 };
        var options = BinarySerializerOptions.Configure().PreserveReferences().Build();

        var actual = TestHelpers.RoundTrip(
            new KeyedAnimalPair { A = shared, B = shared },
            options);

        Assert.Same(actual.A, actual.B);
    }
    
    [Fact]
    public void KEY_V0_01_V0WriterRejectsBinaryContract()
    {
        var options = BinarySerializerOptions.Configure()
            .WithVersion(0)
            .AllowV0Fallback(true)
            .Build();

        var serializer = new BinarySerializer(options);

        var value = new ContractV1
        {
            Name = "Ada",
            Age = 37
        };

        var exception = Assert.Throws<BinaryFormatNotSupportedException>(
            () => serializer.Serialize(value));

        Assert.Contains("[BinaryContract]/[BinaryKey]", exception.Message);
        Assert.Contains("requires V1 keyed wire encoding", exception.Message);
    }
    
    [Fact]
    public void KEY_V0_02_V0ReaderRejectsBinaryContract()
    {
        var codec = new V0FormatCodec(DeserializationLimits.Default);

        using var stream = new MemoryStream();

        using (var writer = new BinaryWriter(
                   stream,
                   Encoding.UTF8,
                   leaveOpen: true))
        {
            // BinaryPayloadReader.Deserialize<T>() first reads hasValue.
            // true is enough to reach PopulateMembers() where the V0
            // keyed-contract capability check must reject ContractV1.
            writer.Write(true);
            writer.Flush();
        }

        stream.Position = 0;

        var exception = Assert.Throws<BinaryFormatNotSupportedException>(
            () => codec.Deserialize<ContractV1>(stream));

        Assert.Contains("[BinaryContract]/[BinaryKey]", exception.Message);
        Assert.Contains("requires V1 keyed wire encoding", exception.Message);
    }
    
    [Fact]
    public void KEY_V0_03_V1KeyedContractWorksWithV0FallbackDisabled()
    {
        var options = BinarySerializerOptions.Configure()
            .WithVersion(1)
            .AllowV0Fallback(false)
            .Build();

        var serializer = new BinarySerializer(options);

        var expected = new ContractV1
        {
            Name = "Ada",
            Age = 37
        };

        var bytes = serializer.Serialize(expected);

        var header = BinaryFormatInspector.Peek(new MemoryStream(bytes));

        Assert.NotNull(header);
        Assert.Equal(1, header?.FormatVersion);

        var actual = serializer.Deserialize<ContractV1>(bytes);

        Assert.NotNull(actual);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Age, actual.Age);
    }
    
    [Fact]
    public void KEY_V0_04_V1KeyedContractWorksWithV0FallbackEnabled()
    {
        var options = BinarySerializerOptions.Configure()
            .WithVersion(1)
            .AllowV0Fallback(true)
            .Build();

        var serializer = new BinarySerializer(options);

        var expected = new ContractV1
        {
            Name = "Ada",
            Age = 37
        };

        var bytes = serializer.Serialize(expected);

        var header = BinaryFormatInspector.Peek(new MemoryStream(bytes));

        Assert.NotNull(header);
        Assert.Equal(1, header?.FormatVersion);

        var actual = serializer.Deserialize<ContractV1>(bytes);

        Assert.NotNull(actual);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Age, actual.Age);
    }

    private static byte[] SerializePayload<T>(T value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        new BinaryPayloadWriter(writer, limits: DeserializationLimits.Default, keyedContracts: true).Serialize(value);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] ExtractPayload(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var header = BinaryFormatHeaderV1.ReadFrom(reader);
        Assert.Equal(CompressionAlgorithm.None, header.Compression);
        Assert.Equal(ChecksumAlgorithm.None, header.ChecksumAlgorithm);
        Assert.Equal(EncryptionAlgorithm.None, header.Encryption);
        Assert.Empty(header.Checksum);
        var payload = reader.ReadBytes(header.OnDiskLength);
        Assert.Equal(header.OnDiskLength, payload.Length);
        return payload;
    }

    private static byte[] RebuildV1(IReadOnlyList<KeyedField> fields)
    {
        var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            Write7Bit(writer, fields.Count);
            foreach (var field in fields)
            {
                if (field.RawKeyEncoding is not null)
                    writer.Write(field.RawKeyEncoding);
                else
                    Write7Bit(writer, field.Key);

                writer.Write(field.DeclaredLengthOverride ?? field.Payload.Length);
                writer.Write(field.Payload);
            }
        }

        var rawPayload = payload.ToArray();
        var originalBytes = new BinarySerializer().Serialize(new ContractV1 { Name = "template", Age = 0 });
        using var input = new MemoryStream(originalBytes, writable: false);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var header = BinaryFormatHeaderV1.ReadFrom(reader);
        Assert.Equal(CompressionAlgorithm.None, header.Compression);
        Assert.Equal(ChecksumAlgorithm.None, header.ChecksumAlgorithm);
        Assert.Equal(EncryptionAlgorithm.None, header.Encryption);
        Assert.Empty(header.Checksum);
        var rebuiltHeader = header with
        {
            UncompressedLength = rawPayload.Length,
            CompressedLength = rawPayload.Length,
            OnDiskLength = rawPayload.Length,
            Checksum = []
        };

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            rebuiltHeader.WriteTo(writer);
            writer.Write(rawPayload);
        }

        return output.ToArray();
    }

    private static List<KeyedField> ReadFields(byte[] payload)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var count = Read7Bit(reader);
        var fields = new List<KeyedField>(count);

        for (var i = 0; i < count; i++)
        {
            var key = Read7Bit(reader);
            var length = reader.ReadInt32();
            var bytes = reader.ReadBytes(length);
            Assert.Equal(length, bytes.Length);
            fields.Add(new KeyedField(key, bytes));
        }

        Assert.Equal(stream.Length, stream.Position);
        return fields;
    }

    private static int Read7Bit(BinaryReader reader)
    {
        uint result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var current = reader.ReadByte();
            result |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return checked((int)result);
        }

        throw new InvalidOperationException("Invalid test fixture varint.");
    }

    private static void Write7Bit(BinaryWriter writer, int value)
    {
        var remaining = (uint)value;
        while (remaining >= 0x80)
        {
            writer.Write((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        writer.Write((byte)remaining);
    }

    private sealed class KeyedField(int key, byte[] payload)
    {
        public int Key { get; } = key;
        public byte[] Payload { get; } = payload;
        public int? DeclaredLengthOverride { get; init; }
        public byte[]? RawKeyEncoding { get; init; }
    }

    [BinaryContract]
    public sealed class BoundaryContract
    {
        [BinaryKey(127)] public int K127 { get; set; }
        [BinaryKey(128)] public int K128 { get; set; }
        [BinaryKey(16383)] public int K16383 { get; set; }
        [BinaryKey(16384)] public int K16384 { get; set; }
        [BinaryKey(int.MaxValue)] public int KMax { get; set; }
    }

    [BinaryContract]
    public sealed class KeyedAnimal
    {
        [BinaryKey(1)] public Animal Value { get; set; } = null!;
    }

    [BinaryContract]
    public sealed class KeyedAnimalPair
    {
        [BinaryKey(1)] public Animal A { get; set; } = null!;
        [BinaryKey(2)] public Animal B { get; set; } = null!;
    }

    [BinaryContract]
    public sealed class KeyedCycle
    {
        [BinaryKey(1)] public KeyedCycle? Next { get; set; }
    }
}
