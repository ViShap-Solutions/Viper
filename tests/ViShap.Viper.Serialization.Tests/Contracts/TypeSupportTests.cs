using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using ViShap.Viper.Formatters;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-20, CTR-23 and CTR-24: what makes a type supported. A type the reader cannot construct is
/// refused rather than approximated, a type §23 does not cover never reaches the wire at all, and the
/// registry resolves a shape or nothing — there is no catch-all formatter shadowing a specific one.
/// </summary>
public class TypeSupportTests
{
    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void Deserialize_MemberEncodedTypeWithoutAParameterlessConstructor_ThrowsType()
    {
        byte[] payload = _serializer.Serialize(new RequiresArguments(5));

        AssertEx.Throws<BinaryTypeException>(
            "cannot be constructed", () => _serializer.Deserialize<RequiresArguments>(payload));
    }

    [Fact]
    public void Serialize_MemberEncodedTypeWithoutAParameterlessConstructor_StillWrites()
    {
        // The write path has an instance already; only the reader needs to build one, which is why
        // §23 places the failure on the read side.
        Assert.NotEmpty(_serializer.Serialize(new RequiresArguments(5)));
    }

    [Fact]
    public void Serialize_TypeOutsideTheSupportedSet_ThrowsTypeAtTheFirstUse()
    {
        using var destination = new MemoryStream();

        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize<Type>(typeof(int)));
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize<Stream>(destination));
    }

    [Fact]
    public void Serialize_TypeOutsideTheSupportedSet_WritesNothing()
    {
        // Refused, not member-encoded into an empty object that would read back as nothing.
        using var destination = new MemoryStream();

        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize<Type>(destination, typeof(int)));

        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public void Deserialize_TypeOutsideTheSupportedSet_ThrowsType()
    {
        byte[] payload = Wire.Frame(Wire.Payload(writer => writer.Write(true)));

        Assert.Throws<BinaryTypeException>(() => _serializer.Deserialize<Type>(payload));
    }

    [Fact]
    public void Resolve_MemberEncodedType_ReturnsNull()
    {
        // A null result is the object shape, and it is the only way to reach member encoding.
        Assert.Null(FormatterRegistry.Resolve(typeof(Person)));
        Assert.Null(FormatterRegistry.Resolve(typeof(Catalogue)));
        Assert.Null(FormatterRegistry.Resolve(typeof(object)));
        Assert.Null(FormatterRegistry.Resolve(typeof(PointStruct)));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(string))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(DateTime))]
    public void Resolve_ScalarType_ReturnsTheScalarShape(Type type)
    {
        Assert.IsAssignableFrom<IScalarFormatter>(FormatterRegistry.Resolve(type));
    }

    [Theory]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(List<int>))]
    [InlineData(typeof(HashSet<int>))]
    [InlineData(typeof(ReadOnlyCollection<int>))]
    [InlineData(typeof(ImmutableList<int>))]
    [InlineData(typeof(FrozenSet<int>))]
    [InlineData(typeof(Bag))]
    public void Resolve_SequenceType_ReturnsTheSequenceShape(Type type)
    {
        Assert.IsAssignableFrom<ISequenceFormatter>(FormatterRegistry.Resolve(type));
    }

    [Theory]
    [InlineData(typeof(Dictionary<string, int>))]
    [InlineData(typeof(SortedDictionary<string, int>))]
    [InlineData(typeof(FrozenDictionary<string, int>))]
    public void Resolve_MapType_ReturnsTheMapShape(Type type)
    {
        Assert.IsAssignableFrom<IMapFormatter>(FormatterRegistry.Resolve(type));
    }

    [Theory]
    [InlineData(typeof(KeyValuePair<string, int>))]
    [InlineData(typeof(ValueTuple<int, string>))]
    [InlineData(typeof(Lazy<int>))]
    [InlineData(typeof(ImmutableArray<int>))]
    [InlineData(typeof(int[,]))]
    public void Resolve_CompositeType_ReturnsTheCompositeShape(Type type)
    {
        Assert.IsAssignableFrom<ICompositeFormatter>(FormatterRegistry.Resolve(type));
    }

    [Fact]
    public void Resolve_TypeAGeneralFormatterWouldAlsoClaim_PrefersTheSpecificOne()
    {
        // Bag is reached by the last-resort ICollection<T> shape. List<T>, ObservableCollection<T>
        // and Stack<T> satisfy that shape too, so each of them resolving to a different formatter is
        // what proves the general one never ran first.
        var lastResort = FormatterRegistry.Resolve(typeof(Bag));

        Assert.NotNull(lastResort);
        Assert.NotSame(lastResort, FormatterRegistry.Resolve(typeof(List<int>)));
        Assert.NotSame(lastResort, FormatterRegistry.Resolve(typeof(ObservableCollection<int>)));
        Assert.NotSame(lastResort, FormatterRegistry.Resolve(typeof(Stack<int>)));
    }

    [Fact]
    public void Resolve_Delegate_IsClaimedBeforeAnyOtherShape()
    {
        // The rejection is a formatter of its own and comes first, so no collection or object shape
        // can claim a delegate on the way past.
        Assert.NotNull(FormatterRegistry.Resolve(typeof(Func<int>)));
        Assert.NotNull(FormatterRegistry.Resolve(typeof(Action)));
    }
}
