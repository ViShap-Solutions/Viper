using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins the one wire state a container decides for itself: a key or an element the payload declares
/// twice. The containers disagree — some raise the framework's own argument exception, some drop the
/// repeat and say nothing — so left to them the same payload would be a crash, a silent loss of data,
/// or neither, depending on which type a member happens to be declared as.
/// </summary>
/// <remarks>
/// The rule is one rule: a duplicate is malformed input. It is decided by the engine, which owns the
/// element loop, so no container is trusted to report it and no framework exception reaches the
/// caller under its own name.
/// </remarks>
public class DuplicateEntryTests
{
    private static readonly BinarySerializer Serializer = new();

    /// <summary>A frame holding a map that names key 1 twice, with different values.</summary>
    private static byte[] DuplicateKey() =>
        Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(2);
            writer.Write(1); writer.Write(10);
            writer.Write(1); writer.Write(20);
        }));

    /// <summary>A frame holding a sequence that names element 1 twice.</summary>
    private static byte[] DuplicateElement() =>
        Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(2);
            writer.Write(1);
            writer.Write(1);
        }));

    // --- the containers that refuse a duplicate themselves ----------------------------------------

    [Fact]
    public void Deserialize_ADictionaryDeclaringTheSameKeyTwice_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(
            () => Serializer.Deserialize<Dictionary<int, int>>(DuplicateKey()));
    }

    [Fact]
    public void Deserialize_ASortedDictionaryDeclaringTheSameKeyTwice_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(
            () => Serializer.Deserialize<SortedDictionary<int, int>>(DuplicateKey()));
    }

    [Fact]
    public void Deserialize_ASortedListDeclaringTheSameKeyTwice_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(
            () => Serializer.Deserialize<SortedList<int, int>>(DuplicateKey()));
    }

    [Fact]
    public void Deserialize_AReadOnlyDictionaryDeclaringTheSameKeyTwice_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(
            () => Serializer.Deserialize<ReadOnlyDictionary<int, int>>(DuplicateKey()));
    }

    [Fact]
    public void Deserialize_AnImmutableDictionaryDeclaringTheSameKeyTwice_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(
            () => Serializer.Deserialize<ImmutableDictionary<int, int>>(DuplicateKey()));
    }

    [Fact]
    public void Deserialize_AFrozenDictionaryDeclaringTheSameKeyTwice_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(
            () => Serializer.Deserialize<FrozenDictionary<int, int>>(DuplicateKey()));
    }

    // --- the containers that would have collapsed it silently -------------------------------------

    [Fact]
    public void Deserialize_AConcurrentDictionaryDeclaringTheSameKeyTwice_ThrowsFormat()
    {
        AssertEx.Throws<BinaryFormatException>(
            "materialized 1",
            () => Serializer.Deserialize<ConcurrentDictionary<int, int>>(DuplicateKey()));
    }

    [Fact]
    public void Deserialize_AHashSetDeclaringTheSameElementTwice_ThrowsFormat()
    {
        AssertEx.Throws<BinaryFormatException>(
            "materialized 1", () => Serializer.Deserialize<HashSet<int>>(DuplicateElement()));
    }

    [Fact]
    public void Deserialize_ASortedSetDeclaringTheSameElementTwice_ThrowsFormat()
    {
        AssertEx.Throws<BinaryFormatException>(
            "materialized 1", () => Serializer.Deserialize<SortedSet<int>>(DuplicateElement()));
    }

    [Fact]
    public void Deserialize_AFrozenSetDeclaringTheSameElementTwice_ThrowsFormat()
    {
        AssertEx.Throws<BinaryFormatException>(
            "materialized 1", () => Serializer.Deserialize<FrozenSet<int>>(DuplicateElement()));
    }

    [Fact]
    public void Deserialize_AnImmutableHashSetDeclaringTheSameElementTwice_ThrowsFormat()
    {
        AssertEx.Throws<BinaryFormatException>(
            "materialized 1", () => Serializer.Deserialize<ImmutableHashSet<int>>(DuplicateElement()));
    }

    // --- no framework exception escapes under its own name ----------------------------------------

    [Fact]
    public void Deserialize_ADuplicateKey_NeverLeavesTheTaxonomy()
    {
        var error = Record.Exception(() => Serializer.Deserialize<Dictionary<int, int>>(DuplicateKey()));

        Assert.IsAssignableFrom<BinarySerializerException>(error);
        Assert.IsAssignableFrom<ArgumentException>(error!.InnerException);
    }

    [Fact]
    public void Deserialize_ADictionaryDeclaringANullKey_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(1);
            writer.Write(false);   // a null key
            writer.Write(10);
        }));

        Assert.Throws<BinaryFormatException>(
            () => Serializer.Deserialize<Dictionary<string, int>>(frame));
    }

    // --- what a well-formed payload still does ----------------------------------------------------

    [Fact]
    public void Deserialize_DistinctKeys_IsUnaffected()
    {
        var value = new Dictionary<int, int> { [1] = 10, [2] = 20 };

        Assert.Equal(value, Serializer.Deserialize<Dictionary<int, int>>(Serializer.Serialize(value)));
    }

    [Fact]
    public void Deserialize_ASequenceThatAdmitsRepeats_IsUnaffected()
    {
        // A list is not a set: the same value twice is data, not a duplicate.
        Assert.Equal([1, 1], Serializer.Deserialize<List<int>>(DuplicateElement()));
    }
}
