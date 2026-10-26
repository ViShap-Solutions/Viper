using System.Reflection;
using ViShap.Viper.Contracts;
using ViShap.Viper.Engine;
using ViShap.Viper.Formatters;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins NAOT-01…NAOT-05: every supported type a context adds through <see cref="SupportedTypes"/> is
/// encoded by the codec the reflection path builds for the same type — the same codec, over the same
/// shape — so which path supplies a type never changes a byte; the table covers every supported generic
/// definition; a method refuses a type another entry of the table claims; a union is the map its
/// attributes declare and nothing else; a custom collection is filled by the rule of the reflection
/// path; and a context adds each type once, before its first use.
/// </summary>
public class SupportedTypesTests
{
    // --- NAOT-01: the codec offered is the codec the registry builds --------------------------------

    /// <summary>The type arguments each generic method of the façade is closed over in turn.</summary>
    private static readonly Type[] Arguments = [typeof(int), typeof(string), typeof(ConformanceKeyed)];

    public static TheoryData<string> GenericMethods()
    {
        var data = new TheoryData<string>();
        foreach (var method in typeof(SupportedTypes).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(method => !Special.Contains(method.Name))
                     .Select(Signature)
                     .Distinct()
                     .Order(StringComparer.Ordinal))
            data.Add(method);

        return data;
    }

    [Theory]
    [MemberData(nameof(GenericMethods))]
    public void SupportedType_OfEveryGenericDefinition_IsTheRegistrysCodec(string signature)
    {
        var method = typeof(SupportedTypes).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate => Signature(candidate) == signature);

        foreach (var argument in Arguments)
        {
            var closing = method.GetGenericArguments().Select(parameter => Admissible(parameter, argument)).ToArray();
            var supported = (SupportedType)method.MakeGenericMethod(closing).Invoke(null, null)!;

            AssertSameCodec(supported);
        }
    }

    [Fact]
    public void SupportedType_OfTheSpecialEntries_IsTheRegistrysCodec()
    {
        AssertSameCodec(SupportedTypes.Object<ConformanceKeyed>());
        AssertSameCodec(SupportedTypes.Object<ConformancePoint>());
        AssertSameCodec(SupportedTypes.Enum<ByteEnum>());
        AssertSameCodec(SupportedTypes.Nullable<ConformancePoint>());
        AssertSameCodec(SupportedTypes.MultiDimensionalArray<int[,], int>());
        AssertSameCodec(SupportedTypes.MultiDimensionalArray<string[,,], string>());
        AssertSameCodec(SupportedTypes.Collection<Bag, int>());
        AssertSameCodec(SupportedTypes.Collection<Bag<string>, string>());
        AssertSameCodec(SupportedTypes.Collection<AddlessBag, int>());
    }

    [Fact]
    public void SupportedTypes_CoverEveryGenericDefinitionOfTheTable()
    {
        var covered = typeof(SupportedTypes).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => !Special.Contains(method.Name))
            .Select(method => ((SupportedType)method
                .MakeGenericMethod(method.GetGenericArguments().Select(parameter => Admissible(parameter, typeof(int))).ToArray())
                .Invoke(null, null)!).Type)
            .Where(type => type.IsGenericType)
            .Select(type => type.GetGenericTypeDefinition())
            .ToHashSet();

        Assert.Empty(TypeRules.Shapes.Keys.Where(definition => !covered.Contains(definition)));
    }

    // --- NAOT-02: a union is the map its attributes declare -------------------------------------------

    [Fact]
    public void Union_WithTheArmsItsAttributesDeclare_IsTheMapTheReflectionPathReads()
    {
        var supported = SupportedTypes.Union<ConformanceShape>((1, typeof(ConformanceCircle)), (2, typeof(ConformanceSquare)));
        var offered = Union(supported.Codec)!;
        var read = TypeContractCache.GetUnion(typeof(ConformanceShape))!;

        Assert.Equal(typeof(ObjectCodec<ConformanceShape>), supported.Codec.GetType());
        Assert.Same(read, offered);
        foreach (var arm in new[] { typeof(ConformanceCircle), typeof(ConformanceSquare) })
        {
            Assert.True(offered.TryGetTag(arm, out byte offeredTag));
            Assert.True(read.TryGetTag(arm, out byte readTag));
            Assert.Equal(readTag, offeredTag);
            Assert.True(offered.TryGetType(offeredTag, out var back));
            Assert.Equal(arm, back);
        }

        Assert.False(offered.TryGetType(3, out _));
    }

    [Fact]
    public void Union_WithTheDeclaredArmsInAnotherOrder_IsTheMapTheReflectionPathReads()
    {
        var supported = SupportedTypes.Union<Shape>((3, typeof(Triangle)), (1, typeof(Circle)), (2, typeof(Square)));

        Assert.Same(TypeContractCache.GetUnion(typeof(Shape)), Union(supported.Codec));
    }

    [Fact]
    public void Union_WithArmsItsAttributesDoNotDeclare_ThrowsConfiguration()
    {
        const string differ = "differ from the ones its [BinaryUnion] attributes declare: (1, ";

        Refused(() => SupportedTypes.Union<Shape>((9, typeof(Circle)), (2, typeof(Square)), (3, typeof(Triangle))), differ);
        Refused(() => SupportedTypes.Union<Shape>((2, typeof(Circle)), (1, typeof(Square)), (3, typeof(Triangle))), differ);
        Refused(() => SupportedTypes.Union<Shape>((1, typeof(Circle)), (2, typeof(Square))), differ);
        Refused(
            () => SupportedTypes.Union<Shape>(
                (1, typeof(Circle)), (2, typeof(Square)), (3, typeof(Triangle)), (4, typeof(UndeclaredShape))),
            differ);
        Refused(() => SupportedTypes.Union<Shape>(), differ);
    }

    [Fact]
    public void Union_OfATypeWithoutUnionAttributes_ThrowsConfiguration()
    {
        Refused(() => SupportedTypes.Union<ConformanceKeyed>(), "declares no [BinaryUnion] arm");
        Refused(() => SupportedTypes.Union<ConformanceKeyed>((1, typeof(ConformanceKeyed))), "declares no [BinaryUnion] arm");
        Refused(() => SupportedTypes.Union<UndeclaredShape>((1, typeof(UndeclaredShape))), "declares no [BinaryUnion] arm");
    }

    [Fact]
    public void Union_OfATypeWhoseAttributesTheSerializerRefuses_ThrowsConfiguration()
    {
        Refused(() => SupportedTypes.Union<DuplicateTagBase>((1, typeof(FirstClaim))), "duplicate [BinaryUnion] tag");
        Refused(() => SupportedTypes.Union<AboveRangeBase>(), "must fit in a byte");
        Refused(() => SupportedTypes.Union<UnrelatedKnownTypeBase>(), "is not assignable");
    }

    [Fact]
    public void Object_OfATypeWithoutUnion_CarriesNoMap()
    {
        Assert.Null(Union(SupportedTypes.Object<ConformanceKeyed>().Codec));
    }

    // --- NAOT-03: a method refuses a type another entry claims ---------------------------------------

    [Fact]
    public void Object_OfATypeAnotherEntryClaims_ThrowsConfiguration()
    {
        Refused(() => SupportedTypes.Object<List<int>>(), "supported generic type");
        Refused(() => SupportedTypes.Object<int>(), "scalar");
        Refused(() => SupportedTypes.Object<int[]>(), "SupportedTypes.Array");
        Refused(() => SupportedTypes.Object<ByteEnum>(), "SupportedTypes.Enum");
        Refused(() => SupportedTypes.Object<Bag>(), "SupportedTypes.Collection");
        Refused(() => SupportedTypes.Object<Action>(), "delegate");
        Refused(() => SupportedTypes.Union<List<int>>(), "supported generic type");
        Refused(() => SupportedTypes.Object<ConformanceShape>(), "SupportedTypes.Union");
    }

    [Fact]
    public void Collection_OfATypeAnotherEntryClaimsOrOfAnotherElement_ThrowsConfiguration()
    {
        Refused(
            () => SupportedTypes.Collection<List<int>, int>(),
            "supported generic type");
        if (TypeRules.CollectionElement(typeof(DoubleBag)) == typeof(int))
            Refused(() => SupportedTypes.Collection<DoubleBag, string>(), "not of");
        else
            Refused(() => SupportedTypes.Collection<DoubleBag, int>(), "not of");
    }

    [Fact]
    public void Collection_FillsThroughItsPublicAdd_AndWithoutOneRefusesToRead()
    {
        var bag = new CustomCollectionShape<Bag, int>();
        var filled = bag.Create(capacity: 2);
        bag.Add(ref filled, 7);
        bag.Add(ref filled, 8);

        var structBag = new CustomCollectionShape<StructBag, int>();
        var filledStruct = structBag.Create(capacity: 1);
        structBag.Add(ref filledStruct, 9);

        var addless = new CustomCollectionShape<AddlessBag, int>();
        var empty = addless.Create(capacity: 1);

        Assert.Equal([7, 8], filled);
        Assert.Equal([9], filledStruct);
        AssertEx.Throws<BinaryTypeException>("has no public instance method Add", () => addless.Add(ref empty, 1));
    }

    [Fact]
    public void MultiDimensionalArray_OfAnythingButAnArrayOfRankTwoOrMore_ThrowsConfiguration()
    {
        Refused(() => SupportedTypes.MultiDimensionalArray<int[], int>(), "rank 2 or more");
        Refused(() => SupportedTypes.MultiDimensionalArray<int[,], long>(), "rank 2 or more");
        Refused(() => SupportedTypes.MultiDimensionalArray<string, char>(), "rank 2 or more");
    }

    [Fact]
    public void Union_WithAMalformedArm_ThrowsConfiguration()
    {
        Refused(
            () => SupportedTypes.Union<ConformanceShape>((256, typeof(ConformanceCircle))), "fit in a byte");
        Refused(
            () => SupportedTypes.Union<ConformanceShape>((-1, typeof(ConformanceCircle))), "fit in a byte");
        Refused(
            () => SupportedTypes.Union<ConformanceShape>((1, typeof(ConformanceCircle)), (1, typeof(ConformanceSquare))),
            "more than once");
        Refused(
            () => SupportedTypes.Union<ConformanceShape>((1, typeof(ConformanceCircle)), (2, typeof(ConformanceCircle))),
            "more than once");
        Refused(
            () => SupportedTypes.Union<ConformanceShape>((1, typeof(ConformanceKeyed))), "not assignable");
        Refused(
            () => SupportedTypes.Union<ConformanceShape>((1, null!)), "names no type");
        Assert.Throws<ArgumentNullException>(() => SupportedTypes.Union<ConformanceShape>(null!));
    }

    // --- NAOT-04: a context adds each type once, before its first use --------------------------------

    [Fact]
    public void Add_TheSameSupportedTypeTwice_ThrowsConfiguration()
    {
        Refused(() => new TwiceSupportedContext(), "already makes");
    }

    [Fact]
    public void Add_AfterOptionsWereBuilt_ThrowsConfiguration()
    {
        var context = new LateSupportedContext();
        _ = BinarySerializerOptions.Configure().WithContracts(context).Build();

        Refused(context.AddLate, "already in use");
    }

    [Fact]
    public void Add_ANullSupportedType_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NullSupportedContext());
    }

    // --- NAOT-05: an offered codec is the one the type's cache holds ---------------------------------

    [Fact]
    public void Add_ATypeNothingHasUsed_MakesItsCodecTheCachedOne()
    {
        var context = new OfferingContext();

        Assert.Same(context.Offered.Codec, FormatterCache<Queue<OfferedOnly>>.Instance);
    }

    [Fact]
    public void Add_ATypeAlreadyOffered_KeepsTheFirstOffer()
    {
        var first = new StackOfferingContext();
        var second = new StackOfferingContext();

        Assert.NotSame(first.Offered.Codec, second.Offered.Codec);
        Assert.Same(first.Offered.Codec, FormatterCache<Stack<OfferedOnly>>.Instance);
    }

    // --- helpers -------------------------------------------------------------------------------------

    private static void Refused(Action act, string fragment) =>
        AssertEx.Throws<BinaryConfigurationException>(fragment, act);

    /// <summary>The methods whose type arguments are not free: each is exercised by its own facts.</summary>
    private static readonly HashSet<string> Special =
        [nameof(SupportedTypes.Object), nameof(SupportedTypes.Union), nameof(SupportedTypes.Enum),
         nameof(SupportedTypes.MultiDimensionalArray), nameof(SupportedTypes.Collection)];

    private static string Signature(MethodInfo method) => $"{method.Name}`{method.GetGenericArguments().Length}";

    /// <summary><paramref name="argument"/>, or <c>int</c> where the parameter requires a value type.</summary>
    private static Type Admissible(Type parameter, Type argument) =>
        parameter.GenericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint) &&
        !argument.IsValueType
            ? typeof(int)
            : argument;

    private static void AssertSameCodec(SupportedType supported)
    {
        var registry = typeof(FormatterRegistry).GetMethod(nameof(FormatterRegistry.Resolve))!
            .MakeGenericMethod(supported.Type)
            .Invoke(null, null)!;

        Assert.Equal(registry.GetType(), supported.Codec.GetType());

        if (registry.GetType().GetGenericTypeDefinition() == typeof(ObjectCodec<>))
            Assert.Equal(TypeContractCache.GetUnion(supported.Type) is null, Union(supported.Codec) is null);
        else
            Assert.Equal(Parts(registry), Parts(supported.Codec));
    }

    /// <summary>The runtime types of the objects a codec holds — its shape or formatter, and theirs.</summary>
    private static IEnumerable<string> Parts(object codec) =>
        codec.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.GetValue(codec))
            .Where(value => value is not null and not Delegate)
            .Select(value => value!.GetType().FullName!)
            .Order(StringComparer.Ordinal);

    private static UnionMap? Union(object codec)
    {
        var lookup = codec.GetType().GetField("_union", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(codec);
        return (UnionMap?)lookup?.GetType().GetProperty("Map")!.GetValue(lookup);
    }

    /// <summary>A type derived from a union base whose attributes do not name it.</summary>
    public sealed class UndeclaredShape : Shape;

    /// <summary>A collection with no public instance <c>Add</c>: written, and refused when read.</summary>
    public sealed class AddlessBag : ICollection<int>
    {
        public int Count => 0;
        public bool IsReadOnly => true;
        void ICollection<int>.Add(int item) => throw new NotSupportedException();
        public void Clear() { }
        public bool Contains(int item) => false;
        public void CopyTo(int[] array, int arrayIndex) { }
        public bool Remove(int item) => false;
        public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A value-type collection over a list it shares with its copies.</summary>
    public struct StructBag : ICollection<int>
    {
        private readonly List<int> _items;

        public StructBag() => _items = [];

        public readonly int Count => _items.Count;
        public readonly bool IsReadOnly => false;
        public readonly void Add(int item) => _items.Add(item);
        public readonly void Clear() => _items.Clear();
        public readonly bool Contains(int item) => _items.Contains(item);
        public readonly void CopyTo(int[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public readonly bool Remove(int item) => _items.Remove(item);
        public readonly IEnumerator<int> GetEnumerator() => _items.GetEnumerator();
        readonly System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class OfferedOnly
    {
        public int Value { get; set; }
    }

    private sealed class OfferingContext : BinarySerializerContext
    {
        public OfferingContext()
        {
            Offered = SupportedTypes.Queue<OfferedOnly>();
            Add(Offered);
        }

        public SupportedType Offered { get; }
    }

    /// <summary>A collection of two element types; the table encodes it as the one it finds first.</summary>
    public sealed class DoubleBag : ICollection<int>, ICollection<string>
    {
        public int Count => 0;
        public bool IsReadOnly => false;
        public void Add(int item) { }
        public void Add(string item) { }
        public void Clear() { }
        public bool Contains(int item) => false;
        public bool Contains(string item) => false;
        public void CopyTo(int[] array, int arrayIndex) { }
        public void CopyTo(string[] array, int arrayIndex) { }
        public bool Remove(int item) => false;
        public bool Remove(string item) => false;
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();
        IEnumerator<string> IEnumerable<string>.GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();
    }

    private sealed class StackOfferingContext : BinarySerializerContext
    {
        public StackOfferingContext()
        {
            Offered = SupportedTypes.Stack<OfferedOnly>();
            Add(Offered);
        }

        public SupportedType Offered { get; }
    }

    private sealed class TwiceSupportedContext : BinarySerializerContext
    {
        public TwiceSupportedContext()
        {
            Add(SupportedTypes.List<int>());
            Add(SupportedTypes.List<int>());
        }
    }

    private sealed class LateSupportedContext : BinarySerializerContext
    {
        public void AddLate() => Add(SupportedTypes.List<long>());
    }

    private sealed class NullSupportedContext : BinarySerializerContext
    {
        public NullSupportedContext() => Add((SupportedType)null!);
    }
}
