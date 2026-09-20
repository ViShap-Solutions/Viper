using System.Buffers;
using System.Runtime.InteropServices;
using ViShap.Viper.Cache;
using ViShap.Viper.Engine;
using ViShap.Viper.Formatters;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Concurrency;

/// <summary>
/// Pins CN-03 to CN-16 and CN-19: every cache in the engine is a process-wide map keyed by type, so
/// the moment that matters is the first touch, when several threads can be building the same entry
/// at once. Each test below names a type the rest of the suite never names, races the first touch,
/// and asserts that every thread came away with the same entry and that the entry works.
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
    public void FormatterRegistry_ResolutionUnderContention_YieldsOneFormatter()
    {
        var formatters = Concurrent.Race(_ => FormatterRegistry.Resolve(typeof(Queue<RacedElement>)));

        Assert.All(formatters, formatter => Assert.Same(formatters[0], formatter));
        Assert.NotNull(formatters[0]);
    }

    [Fact]
    public void ActivatorCache_FirstTouchUnderContention_BuildsOneInstancePerCall()
    {
        var instances = Concurrent.Race(_ => ActivatorCache.CreateInstance(typeof(RacedActivated)));

        Assert.All(instances, instance => Assert.IsType<RacedActivated>(instance));
        Assert.Equal(Concurrent.Workers, instances.Distinct().Count());
    }

    [Fact]
    public void DictionaryAccessorCache_FirstTouchUnderContention_YieldsOneAccessorPair()
    {
        var accessors = Concurrent.Race(
            _ => DictionaryAccessorCache.GetEntryAccessors(typeof(KeyValuePair<RacedKey, RacedElement>)));

        Assert.All(accessors, accessor => Assert.Same(accessors[0], accessor));

        var entry = new KeyValuePair<RacedKey, RacedElement>(new RacedKey(7), new RacedElement { Value = 9 });

        Assert.Equal(new RacedKey(7), accessors[0].KeyGetter(entry));
        Assert.Equal(9, ((RacedElement)accessors[0].ValueGetter(entry)!).Value);
    }

    [Fact]
    public void FrozenFactoryCache_FirstTouchUnderContention_YieldsOneFactoryPerShape()
    {
        var dictionaries = Concurrent.Race(
            _ => FrozenFactoryCache.GetToFrozenDictionary(typeof(RacedKey), typeof(int)));

        var sets = Concurrent.Race(_ => FrozenFactoryCache.GetToFrozenSet(typeof(RacedKey)));

        Assert.All(dictionaries, factory => Assert.Same(dictionaries[0], factory));
        Assert.All(sets, factory => Assert.Same(sets[0], factory));

        var frozen = (IReadOnlyDictionary<RacedKey, int>)dictionaries[0](
            new List<KeyValuePair<RacedKey, int>> { new(new RacedKey(1), 10) });

        Assert.Equal(10, frozen[new RacedKey(1)]);
        Assert.Equal([new RacedKey(2)], (IEnumerable<RacedKey>)sets[0](new List<RacedKey> { new(2) }));
    }

    [Fact]
    public void ImmutableFactoryCache_FirstTouchUnderContention_YieldsOneFactoryPerShape()
    {
        var asArray = Concurrent.Race(_ => ImmutableFactoryCache.GetStructFactory(
            typeof(ImmutableCollectionsMarshal), "AsArray", typeof(RacedElement)));

        var asImmutable = Concurrent.Race(_ => ImmutableFactoryCache.GetArrayFactory(
            typeof(ImmutableCollectionsMarshal), "AsImmutableArray", typeof(RacedElement)));

        Assert.All(asArray, factory => Assert.Same(asArray[0], factory));
        Assert.All(asImmutable, factory => Assert.Same(asImmutable[0], factory));
    }

    [Fact]
    public void ImmutableArray_BackingArray_IsTakenThroughTheMarshalRatherThanCopied()
    {
        // CN-19, contract section 17: the marshal hands back the array the value already holds. A
        // reflective ToArray would return an equal array, so identity is what tells the two apart.
        var backing = new[] { new RacedElement { Value = 1 } };
        var immutable = ImmutableCollectionsMarshal.AsImmutableArray(backing);

        object taken = ImmutableFactoryCache.GetStructFactory(
            typeof(ImmutableCollectionsMarshal), "AsArray", typeof(RacedElement))(immutable);

        Assert.Same(backing, taken);
    }

    [Fact]
    public void LazyAccessorCache_FirstTouchUnderContention_YieldsOneFactory()
    {
        var factories = Concurrent.Race(_ => LazyAccessorCache.GetFactory(typeof(RacedElement)));

        Assert.All(factories, factory => Assert.Same(factories[0], factory));

        var element = new RacedElement { Value = 42 };
        var lazy = (Lazy<RacedElement>)factories[0](element);

        Assert.False(lazy.IsValueCreated);
        Assert.Same(element, lazy.Value);
    }

    [Fact]
    public void MethodInvokerCache_FirstTouchUnderContention_YieldsOneInvoker()
    {
        var invokers = Concurrent.Race(
            _ => MethodInvokerCache.GetOneArgInvoker(typeof(List<RacedElement>), "Add", typeof(RacedElement)));

        Assert.All(invokers, invoker => Assert.Same(invokers[0], invoker));

        var list = new List<RacedElement>();
        invokers[0](list, new RacedElement { Value = 3 });

        Assert.Equal(3, list[0].Value);
    }

    [Fact]
    public void ReadOnlySequenceAccessorCache_FirstTouchUnderContention_YieldsOneAccessor()
    {
        var accessors = Concurrent.Race(_ => ReadOnlySequenceAccessorCache.GetToArray(typeof(RacedKey)));

        Assert.All(accessors, accessor => Assert.Same(accessors[0], accessor));

        var sequence = new ReadOnlySequence<RacedKey>(new[] { new RacedKey(4), new RacedKey(5) });

        Assert.Equal([new RacedKey(4), new RacedKey(5)], (RacedKey[])accessors[0](sequence));
    }

    [Fact]
    public void TupleAccessorCache_FirstTouchUnderContention_YieldsOneAccessorSet()
    {
        var accessors = Concurrent.Race(_ => TupleAccessorCache.GetAccessors(typeof((RacedKey, RacedElement))));

        Assert.All(accessors, accessor => Assert.Same(accessors[0], accessor));

        var element = new RacedElement { Value = 8 };
        object tuple = (new RacedKey(6), element);

        Assert.Equal(new RacedKey(6), accessors[0].Getters[0](tuple));
        Assert.Same(element, accessors[0].Getters[1](tuple));
    }

    [Fact]
    public void CachedAccessor_IsFunctionallyIdenticalOnTheFirstAndTheSecondUse()
    {
        // CN-14: the cached entry is the same object and behaves the same, so a value built through
        // it after the cache is warm is indistinguishable from one built while it was cold.
        var cold = TupleAccessorCache.GetAccessors(typeof((int, string)));
        object first = cold.Construct([1, "a"]);

        var warm = TupleAccessorCache.GetAccessors(typeof((int, string)));
        object second = warm.Construct([1, "a"]);

        Assert.Same(cold, warm);
        Assert.Equal(first, second);
        Assert.Equal(1, warm.Getters[0](second));
        Assert.Equal("a", warm.Getters[1](second));
    }

    [Fact]
    public void CacheConstruction_NeverNamesTheOperationOrItsBudget()
    {
        // CN-15, contract section 2.2: a cache outlives the call that filled it, so anything
        // request-local it read while building an entry would leak that call's policy into every
        // later one.
        string[] requestLocal =
            ["SerializationOperation", "SerializationBudget", "PhaseBudget", "SerializationLimits"];

        var offenders = SourceTree.ProductionFiles
            .Where(file => file.Key.Contains("ViShap.Viper.Serialization/Cache/", StringComparison.Ordinal))
            .Where(file => requestLocal.Any(name => file.Value.Contains(name, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Request-local state named inside a cache in: {string.Join(", ", offenders)}");
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
    public void InvalidInvokerRequest_FailsTheSameWayOnEveryAttempt()
    {
        var first = Assert.Throws<BinaryTypeException>(
            () => MethodInvokerCache.GetOneArgInvoker(typeof(RacedElement), "NoSuchMethod", typeof(int)));

        var second = Assert.Throws<BinaryTypeException>(
            () => MethodInvokerCache.GetOneArgInvoker(typeof(RacedElement), "NoSuchMethod", typeof(int)));

        Assert.Equal(first.Message, second.Message);
    }

    [Fact]
    public void InvalidTypeUnderContention_FailsOnEveryThread()
    {
        var messages = Concurrent.Race(_ =>
            Assert.Throws<BinaryTypeException>(
                () => MethodInvokerCache.GetOneArgInvoker(typeof(RacedActivated), "AlsoMissing", typeof(int)))
                .Message);

        Assert.Equal(Concurrent.Workers, messages.Length);
        Assert.All(messages, message => Assert.Equal(messages[0], message));
    }
}
