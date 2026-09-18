using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Security;

/// <summary>
/// Acceptance gate for the hostile-input findings of the architecture audit (S03–S12, C06).
/// Each test pins the behavior the target architecture guarantees, not the behavior that was
/// observed before it.
/// </summary>
public class HostileInputTests
{
    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    // --- S03: graph-wide depth coverage -------------------------------------------------------

    [Fact]
    public void Serialize_RecursiveCollectionDeeperThanMaxDepth_ThrowsLimit()
    {
        var root = new Tree();
        var current = root;
        for (int i = 0; i < 30; i++)
        {
            var child = new Tree();
            current.Add(child);
            current = child;
        }

        var serializer = Limited(SerializationLimits.Default with { MaxDepth = 4 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(root));
    }

    [Fact]
    public void Deserialize_RecursiveCollectionDeeperThanMaxDepth_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDepth = 4 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<Tree>(Wire.NestedCollections(30)));
    }

    /// <summary>
    /// The 1.2 MB frame that used to kill the process with an uncatchable StackOverflowException.
    /// It must now fail as an ordinary limit violation, leaving the process alive.
    /// </summary>
    [Fact]
    public void Deserialize_DeeplyNestedCollectionPayload_FailsWithoutStackOverflow()
    {
        byte[] hostile = Wire.NestedCollections(200_000);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<Tree>(hostile));
    }

    // --- S04: container instances are object-graph nodes ---------------------------------------

    [Fact]
    public void Deserialize_ContainersExceedingNodeBudget_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxObjectGraphNodes = 1 });
        var payload = new BinarySerializer().Serialize(new List<List<int>> { new(), new(), new() });

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<List<List<int>>>(payload));
    }

    // --- S05: V0 payload bound on the read path ------------------------------------------------

    [Fact]
    public void Deserialize_V0PayloadOverMaxPayloadBytes_ThrowsLimit()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .AllowV0Fallback()
                .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 1 })
                .Build());

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<int>(BitConverter.GetBytes(123)));
    }

    // --- S06: allocation never precedes the budget check ---------------------------------------

    [Fact]
    public void Deserialize_FrameDeclaringHugePayload_RejectsBeforeAllocating()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 64 });
        byte[] frame = Wire.Frame([], declaredLength: 8 * 1024 * 1024);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.ThrowsAny<BinaryFormatException>(() => serializer.Deserialize<int>(frame));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1024 * 1024, $"rejecting the frame allocated {allocated} bytes");
    }

    [Fact]
    public void Deserialize_StringLongerThanThePayload_RejectsBeforeAllocating()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);                 // non-null string
            writer.Write7BitEncodedInt(3_000_000);
        }));

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<string>(frame));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1024 * 1024, $"rejecting the string allocated {allocated} bytes");
    }

    // --- S10: the root payload is consumed exactly ---------------------------------------------

    [Fact]
    public void Deserialize_PayloadWithTrailingBytes_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(123);
            writer.Write(456);
        }));

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<int>(frame));
    }

    // --- S11: lazy sequences are bounded while being written -----------------------------------

    [Fact]
    public void Serialize_LazySequenceOverCollectionLimit_StopsEnumeratingAtTheLimit()
    {
        int visited = 0;

        IEnumerable<int> Items()
        {
            while (true)
            {
                visited++;
                yield return visited;
            }
        }

        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 8 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize<IEnumerable<int>>(Items()));
        Assert.True(visited <= 9, $"enumerated {visited} items for a limit of 8");
    }

    // --- S12: cumulative keyed-field budget ----------------------------------------------------

    [Fact]
    public void Deserialize_UnknownKeyedFieldsOverCumulativeBudget_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxTotalKeyedFields = 2 });

        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write7BitEncodedInt(3);
            for (int key = 0; key < 3; key++)
            {
                writer.Write7BitEncodedInt(key);
                writer.Write(4);
                writer.Write(0);
            }
        }));

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<EmptyContract>(frame));
    }

    [Fact]
    public void Deserialize_UnknownKeyedFieldsWithinBudget_IsAccepted()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write7BitEncodedInt(3);
            for (int key = 0; key < 3; key++)
            {
                writer.Write7BitEncodedInt(key);
                writer.Write(4);
                writer.Write(0);
            }
        }));

        Assert.NotNull(new BinarySerializer().Deserialize<EmptyContract>(frame));
    }

    // --- C06: the write budget is relative to the operation ------------------------------------

    [Fact]
    public void Serialize_IntoStreamAtNonZeroOffset_ChargesOnlyTheBytesItWrites()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 64 });

        using var atOrigin = new MemoryStream();
        serializer.Serialize(atOrigin, 123);
        long produced = atOrigin.Length;

        using var atOffset = new MemoryStream();
        atOffset.Write(new byte[1024]);
        serializer.Serialize(atOffset, 123);

        Assert.Equal(1024 + produced, atOffset.Length);
    }

    // --- C07: malformed fixed-size values stay inside the exception hierarchy -------------------

    [Theory]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(Int128))]
    [InlineData(typeof(UInt128))]
    public void Deserialize_TruncatedFixedSizeValue_ThrowsFormat(Type type)
    {
        byte[] frame = Wire.Frame([1]);
        var serializer = new BinarySerializer();

        var deserialize = typeof(BinarySerializer)
            .GetMethod(nameof(BinarySerializer.Deserialize), [typeof(byte[])])!
            .MakeGenericMethod(type);

        var error = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => deserialize.Invoke(serializer, [frame]));

        Assert.IsAssignableFrom<BinaryFormatException>(error.InnerException);
    }
}
