using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reflection;
using ViShap.Viper.Engine;
using ViShap.Viper.Formatters;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-20, CTR-23 and CTR-24: what makes a type supported. A type the reader cannot construct is
/// refused rather than approximated, a type §23 does not cover never reaches the wire at all, and the
/// registry resolves a shape or the object codec — there is no catch-all shape shadowing a specific one.
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

    [Theory]
    [InlineData(typeof(Person))]
    [InlineData(typeof(Catalogue))]
    [InlineData(typeof(object))]
    [InlineData(typeof(PointStruct))]
    public void Resolve_MemberEncodedType_IsTheObjectCodec(Type type)
    {
        // The object codec is reached only when no rule claims the type: it is the only way to
        // member encoding.
        Assert.Equal(typeof(ObjectCodec<>), CodecOf(type).GetType().GetGenericTypeDefinition());
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(string))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DayOfWeek))]
    public void Resolve_ScalarType_ReturnsTheScalarShape(Type type)
    {
        Assert.Equal(CodecShape.Scalar, ShapeOf(type));
    }

    [Theory]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(List<int>))]
    [InlineData(typeof(HashSet<int>))]
    [InlineData(typeof(ReadOnlyCollection<int>))]
    [InlineData(typeof(ImmutableList<int>))]
    [InlineData(typeof(ImmutableArray<int>))]
    [InlineData(typeof(FrozenSet<int>))]
    [InlineData(typeof(Bag))]
    public void Resolve_SequenceType_ReturnsTheSequenceShape(Type type)
    {
        Assert.Equal(CodecShape.Sequence, ShapeOf(type));
    }

    [Theory]
    [InlineData(typeof(Dictionary<string, int>))]
    [InlineData(typeof(SortedDictionary<string, int>))]
    [InlineData(typeof(FrozenDictionary<string, int>))]
    public void Resolve_MapType_ReturnsTheMapShape(Type type)
    {
        Assert.Equal(CodecShape.Map, ShapeOf(type));
    }

    [Theory]
    [InlineData(typeof(KeyValuePair<string, int>))]
    [InlineData(typeof(ValueTuple<int, string>))]
    [InlineData(typeof(Lazy<int>))]
    [InlineData(typeof(int[,]))]
    public void Resolve_CompositeType_ReturnsTheCompositeShape(Type type)
    {
        Assert.Equal(CodecShape.Composite, ShapeOf(type));
    }

    [Theory]
    [InlineData(typeof(int?))]
    [InlineData(typeof(PointStruct?))]
    public void Resolve_Nullable_TakesTheShapeOfItsUnderlyingType(Type type)
    {
        Assert.Equal(typeof(NullableCodec<>), CodecOf(type).GetType().GetGenericTypeDefinition());
        Assert.Equal(ShapeOf(Nullable.GetUnderlyingType(type)!), ShapeOf(type));
    }

    [Fact]
    public void Resolve_TypeAGeneralShapeWouldAlsoClaim_PrefersTheSpecificOne()
    {
        // Bag is reached by the last-resort ICollection<T> shape. List<T> and ObservableCollection<T>
        // satisfy that shape too, so each of them driven by a shape of its own is what proves the
        // general one never ran first.
        Assert.Equal(typeof(CustomCollectionShape<,>), ShapeObjectOf(typeof(Bag)).GetType().GetGenericTypeDefinition());
        Assert.Equal(typeof(ListShape<>), ShapeObjectOf(typeof(List<int>)).GetType().GetGenericTypeDefinition());
        Assert.Equal(
            typeof(ObservableCollectionShape<>),
            ShapeObjectOf(typeof(ObservableCollection<int>)).GetType().GetGenericTypeDefinition());
    }

    [Fact]
    public void Resolve_Delegate_IsClaimedBeforeAnyOtherShape()
    {
        // The rejection is a codec of its own and comes first, so no collection or object shape can
        // claim a delegate on the way past.
        Assert.Equal(typeof(RejectedCodec<>), CodecOf(typeof(Func<int>)).GetType().GetGenericTypeDefinition());
        Assert.Equal(typeof(RejectedCodec<>), CodecOf(typeof(Action)).GetType().GetGenericTypeDefinition());
    }

    /// <summary>The codec <see cref="FormatterCache{T}"/> holds for <paramref name="type"/>.</summary>
    private static object CodecOf(Type type) =>
        typeof(FormatterCache<>).MakeGenericType(type).GetField(nameof(FormatterCache<int>.Instance))!.GetValue(null)!;

    private static CodecShape ShapeOf(Type type)
    {
        var codec = CodecOf(type);
        return (CodecShape)codec.GetType().GetProperty(nameof(Codec<int>.Shape))!.GetValue(codec)!;
    }

    /// <summary>The sequence shape a sequence codec drives.</summary>
    private static object ShapeObjectOf(Type type)
    {
        var codec = CodecOf(type);
        var field = codec.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.FieldType.IsGenericType &&
                                 candidate.FieldType.GetGenericTypeDefinition() == typeof(ISequenceShape<,,,>));

        return field.GetValue(codec)!;
    }
}
