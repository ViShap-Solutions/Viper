using ViShap.Viper.Engine;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Concurrency;

/// <summary>
/// Pins CN-03 to CN-05, CN-14 to CN-16 and CN-20: the caches that remain — the contract per type,
/// the union map per declared type, and the codec per type in <see cref="FormatterCache{T}"/> — are
/// process-wide and filled on first use, so the moment that matters is the first touch, when several
/// threads can be building the same entry at once. Each test below names a type the rest of the
/// suite never names, races the first touch, and asserts that every thread came away with the same
/// entry and that the entry works.
/// </summary>
public class CacheTests
{
    [Fact]
    public void TypeContractCache_FirstTouchUnderContention_YieldsOneContract()
    {
        var contracts = Concurrent.Race(_ => TypeContractCache.Get(typeof(RacedPlan)));

        Assert.All(contracts, contract => Assert.Same(contracts[0], contract));
        Assert.Equal(3, contracts[0].Members.Length);
    }

    [Fact]
    public void UnionMapCache_FirstTouchUnderContention_YieldsOneMap()
    {
        var maps = Concurrent.Race(_ => TypeContractCache.GetUnion(typeof(RacedUnionBase)));

        Assert.All(maps, map => Assert.Same(maps[0], map));
        Assert.True(maps[0]!.TryGetTag(typeof(RacedUnionDerived), out byte tag));
        Assert.Equal(1, tag);
    }

    [Fact]
    public void FormatterCache_FirstTouchUnderContention_YieldsOneCodec()
    {
        var codecs = Concurrent.Race(_ => FormatterCache<Queue<RacedElement>>.Instance);

        Assert.All(codecs, codec => Assert.Same(codecs[0], codec));
        Assert.Equal(CodecShape.Sequence, codecs[0].Shape);
    }

    [Fact]
    public void FormatterCache_ForAGenericDefinition_ClosesOneShapePerType()
    {
        // The shape of a generic definition is closed once per closed type, and each closed type has
        // its own codec: two instantiations of one definition never share one.
        var keys = FormatterCache<HashSet<RacedKey>>.Instance;
        var elements = FormatterCache<HashSet<RacedElement>>.Instance;

        Assert.NotSame(keys, (object)elements);
        Assert.Same(keys, FormatterCache<HashSet<RacedKey>>.Instance);
    }

    [Fact]
    public void CachedCodec_IsFunctionallyIdenticalOnTheFirstAndTheSecondUse()
    {
        // CN-14: the cached entry is the same object and behaves the same, so a value written through
        // it after the cache is warm is indistinguishable from one written while it was cold.
        var serializer = new BinarySerializer();
        var cold = FormatterCache<RacedActivated>.Instance;
        byte[] first = serializer.Serialize(new RacedActivated { Value = 5 });

        var warm = FormatterCache<RacedActivated>.Instance;
        byte[] second = serializer.Serialize(new RacedActivated { Value = 5 });

        Assert.Same(cold, warm);
        Assert.Equal(first, second);
        Assert.Equal(5, serializer.Deserialize<RacedActivated>(second)!.Value);
    }

    [Fact]
    public void CacheConstruction_NeverNamesTheOperationOrItsBudget()
    {
        // CN-15, contract section 2.2: a cache outlives the call that filled it, so anything
        // request-local it read while building an entry would leak that call's policy into every
        // later one.
        string[] builders =
        [
            "ViShap.Viper.Serialization/Engine/Codec.cs",
            "ViShap.Viper.Serialization/Engine/Contracts/ReflectedContract.cs",
            "ViShap.Viper.Serialization/Engine/Contracts/TypeContractCache.cs",
            "ViShap.Viper.Serialization/Formatters/FormatterRegistry.cs"
        ];

        string[] requestLocal =
            ["OperationState", "SerializationBudget", "PhaseBudget", "SerializationLimits"];

        Assert.All(builders, builder => Assert.Contains(builder, SourceTree.ProductionFiles.Keys));

        var offenders = builders
            .Where(builder => requestLocal.Any(name =>
                SourceTree.ProductionFiles[builder].Contains(name, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Request-local state named where a cache entry is built in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void CachedContract_BuiltWhileAnOperationFailedOnALimit_CarriesNoneOfThatPolicy()
    {
        var value = new RacedBudgetType { Items = [.. Enumerable.Range(0, 50)] };

        var tight = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with { MaxTotalElements = 10 })
            .Build());

        Assert.Throws<BinaryLimitException>(() => tight.Serialize(value));

        var serializer = new BinarySerializer();
        var restored = serializer.Deserialize<RacedBudgetType>(serializer.Serialize(value));

        Assert.Equal(value.Items, restored!.Items);
    }

    [Fact]
    public void InvalidMember_FailsTheSameWayOnEveryAttempt()
    {
        // CN-16: a rejection is recomputed, never cached as a success and never swallowed after the
        // first attempt, so the second caller sees exactly what the first one saw.
        var serializer = new BinarySerializer();

        var first = Assert.Throws<BinaryTypeException>(() => serializer.Serialize(new WithDelegate()));
        var second = Assert.Throws<BinaryTypeException>(() => serializer.Serialize(new WithDelegate()));
        var third = Assert.Throws<BinaryTypeException>(() => serializer.Serialize(new WithDelegate()));

        Assert.Equal(first.Message, second.Message);
        Assert.Equal(second.Message, third.Message);
    }

    [Fact]
    public void InvalidUnion_FailsTheSameWayOnEveryAttempt()
    {
        var first = Assert.Throws<BinaryTypeException>(() => TypeContractCache.GetUnion(typeof(RacedInvalidUnion)));
        var second = Assert.Throws<BinaryTypeException>(() => TypeContractCache.GetUnion(typeof(RacedInvalidUnion)));

        Assert.Equal(first.Message, second.Message);
    }

    [Fact]
    public void InvalidTypeUnderContention_FailsOnEveryThread()
    {
        var serializer = new BinarySerializer();

        var messages = Concurrent.Race(_ =>
            Assert.Throws<BinaryTypeException>(() => serializer.Serialize<RacedInvalidUnion>(new RacedInvalidArmA()))
                .Message);

        Assert.Equal(Concurrent.Workers, messages.Length);
        Assert.All(messages, message => Assert.Equal(messages[0], message));
    }

    // --- CN-20: the caches that remain are the only ones -----------------------------------------

    [Fact]
    public void TheCacheFolder_HoldsNoType()
    {
        var files = SourceTree.ProductionFiles.Keys
            .Where(file => file.StartsWith("ViShap.Viper.Serialization/Cache/", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(files);
    }

    [Fact]
    public void AConcurrentDictionaryKeyedByType_ExistsOnlyForContractsAndUnions()
    {
        // The engine works with typed codecs, so nothing is looked up by Type on the hot path except
        // what the polymorphic slot needs: the contract of a runtime type and the union map of a
        // declared one. A reflective accessor cache would reappear here first.
        var owners = SourceTree.ProductionFiles
            .Where(file => file.Value.Contains("ConcurrentDictionary<Type", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToArray();

        Assert.Equal(["ViShap.Viper.Serialization/Engine/Contracts/TypeContractCache.cs"], owners);
    }
}
