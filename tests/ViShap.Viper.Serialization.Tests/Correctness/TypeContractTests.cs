using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Correctness;

/// <summary>
/// Acceptance gate for the correctness findings (C01–C05, A01–A03): the type contract is symmetric,
/// so a layout that could not be read back is refused while writing instead of silently corrupting.
/// </summary>
public class TypeContractTests
{
    private readonly BinarySerializer _serializer = new();

    private static BinarySerializer WithReferences() =>
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    // --- C01 / A02: value types restored through the ref overload -------------------------------

    [Fact]
    public void Deserialize_RefPrimitive_RestoresTheValue()
    {
        int value = 0;
        _serializer.Deserialize(_serializer.Serialize(123), ref value);

        Assert.Equal(123, value);
    }

    [Fact]
    public void Deserialize_RefStruct_RestoresEveryMember()
    {
        var source = new PointStruct { X = 5, Y = 7 };
        var target = new PointStruct();

        _serializer.Deserialize(_serializer.Serialize(source), ref target);

        Assert.Equal(source, target);
    }

    [Fact]
    public void Deserialize_RefStructWithPreserveReferences_RestoresEveryMember()
    {
        var serializer = WithReferences();
        var source = new PointStruct { X = 5, Y = 7 };
        var target = new PointStruct();

        serializer.Deserialize(serializer.Serialize(source), ref target);

        Assert.Equal(source, target);
    }

    // --- C02: reference identity covers containers ---------------------------------------------

    [Fact]
    public void Deserialize_SharedCollection_PreservesIdentity()
    {
        var serializer = WithReferences();
        var shared = new List<int> { 1, 2, 3 };
        var source = new SharedLists { A = shared, B = shared };

        var result = serializer.Deserialize<SharedLists>(serializer.Serialize(source))!;

        Assert.Same(result.A, result.B);
        Assert.Equal(shared, result.A);
    }

    [Fact]
    public void Deserialize_SelfReferencingList_RoundTrips()
    {
        var serializer = WithReferences();
        var source = new List<object>();
        source.Add(source);

        var result = serializer.Deserialize<List<object>>(serializer.Serialize(source))!;

        Assert.Single(result);
        Assert.Same(result, result[0]);
    }

    [Fact]
    public void Serialize_CycleWithoutPreserveReferences_ThrowsType()
    {
        var first = new Cyclic { Name = "a" };
        first.Next = first;

        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(first));
    }

    [Fact]
    public void Deserialize_CycleWithPreserveReferences_RoundTrips()
    {
        var serializer = WithReferences();
        var first = new Cyclic { Name = "a" };
        first.Next = first;

        var result = serializer.Deserialize<Cyclic>(serializer.Serialize(first))!;

        Assert.Same(result, result.Next);
    }

    // --- C03: dropping a key stays compatible with reference framing ----------------------------

    [Fact]
    public void Deserialize_OldSchema_SkipsARemovedFieldThatHeldASharedObject()
    {
        var serializer = WithReferences();
        var shared = new Node { Value = 7 };

        byte[] payload = serializer.Serialize(new NewSchema { Removed = shared, Kept = shared });

        var result = serializer.Deserialize<OldSchema>(payload)!;

        Assert.NotNull(result.Kept);
        Assert.Equal(7, result.Kept!.Value);
    }

    [Fact]
    public void Deserialize_KeyedContract_RoundTripsWithReferences()
    {
        var serializer = WithReferences();
        var shared = new Node { Value = 11 };

        var result = serializer.Deserialize<NewSchema>(
            serializer.Serialize(new NewSchema { Removed = shared, Kept = shared }))!;

        Assert.Equal(11, result.Removed!.Value);
        Assert.Equal(11, result.Kept!.Value);
    }

    // --- C04 / A03: no implicit positional polymorphism -----------------------------------------

    [Fact]
    public void Serialize_DerivedValueThroughBaseWithoutUnion_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize<Base>(new Derived { A = 11, Z = 22 }));
    }

    [Fact]
    public void Serialize_ValueThroughObjectWithoutUnion_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize<object>(new Person { Name = "Alice", Age = 30 }));
    }

    [Fact]
    public void Serialize_DerivedValueInsideCollectionWithoutUnion_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize(new List<Base> { new Derived { A = 1, Z = 2 } }));
    }

    [Fact]
    public void Deserialize_DerivedValueThroughDeclaredUnion_KeepsTheRuntimeType()
    {
        var source = new UnionDerived { A = 11, Z = 22 };

        var result = _serializer.Deserialize<UnionBase>(_serializer.Serialize<UnionBase>(source));

        var derived = Assert.IsType<UnionDerived>(result);
        Assert.Equal(11, derived.A);
        Assert.Equal(22, derived.Z);
    }

    // --- C05: contradictory attributes are a contract error -------------------------------------

    [Fact]
    public void Serialize_MemberWithBothKeyAndIgnore_ThrowsType()
    {
        var error = Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize(new Contradictory { Secret = "audit-secret" }));

        Assert.Contains("exactly one", error.Message);
    }

    [Fact]
    public void Serialize_MemberWithBothIncludeAndIgnore_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize(new ContradictoryPositional()));
    }

    // --- A01: populate-in-place only applies to member-encoded types -----------------------------

    [Fact]
    public void Deserialize_PopulateCollectionInPlace_ThrowsType()
    {
        byte[] payload = _serializer.Serialize(new List<int> { 1, 2, 3 });

        Assert.Throws<BinaryTypeException>(
            () => _serializer.Deserialize(payload, new List<int>()));
    }

    [Fact]
    public void Deserialize_PopulateObjectInPlace_ReusesTheInstance()
    {
        byte[] payload = _serializer.Serialize(new Person { Name = "Alice", Age = 30 });
        var target = new Person();

        var result = _serializer.Deserialize(payload, target);

        Assert.Same(target, result);
        Assert.Equal("Alice", target.Name);
        Assert.Equal(30, target.Age);
    }

    // --- Constructing types the payload names ---------------------------------------------------

    [Fact]
    public void Deserialize_InterfaceWithoutUnion_ThrowsType()
    {
        byte[] payload = _serializer.Serialize(new Person { Name = "Alice", Age = 1 });

        Assert.Throws<BinaryTypeException>(() => _serializer.Deserialize<IComparable>(payload));
    }
}
