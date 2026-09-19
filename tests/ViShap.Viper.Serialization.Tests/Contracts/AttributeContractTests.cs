using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-12, CTR-14…CTR-19 and CTR-21: a declaration that contradicts itself is refused when the
/// contract is built, and a type the reader could not construct is refused rather than guessed at.
/// </summary>
public class AttributeContractTests
{
    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void Serialize_MemberWithBothKeyAndIgnore_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "exactly one",
            () => _serializer.Serialize(new Contradictory { Secret = "a secret" }));
    }

    [Fact]
    public void Serialize_MemberWithBothKeyAndIgnore_NeverLeaksTheMemberOntoTheWire()
    {
        // The contradiction must be refused, not resolved in the member's favour.
        var value = new Contradictory { Secret = "a secret" };

        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(value));
    }

    [Fact]
    public void Serialize_MemberWithBothIncludeAndIgnore_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize(new ContradictoryPositional()));
    }

    [Fact]
    public void Serialize_ContractMemberWithNeitherKeyNorIgnore_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "Unmarked", () => _serializer.Serialize(new UnmarkedContractMember { Keyed = 1 }));
    }

    [Fact]
    public void Serialize_ContractWithInclude_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "[BinaryInclude]", () => _serializer.Serialize(new ContractWithInclude { Keyed = 1 }));
    }

    [Fact]
    public void Serialize_ContractWithOrder_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "[BinaryOrder]", () => _serializer.Serialize(new ContractWithOrder { Keyed = 1 }));
    }

    [Fact]
    public void Serialize_DuplicateKeys_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "duplicate [BinaryKey]", () => _serializer.Serialize(new DuplicateKeys()));
    }

    [Fact]
    public void Serialize_NegativeKey_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "negative [BinaryKey]", () => _serializer.Serialize(new NegativeKey { Value = 1 }));
    }

    [Fact]
    public void Serialize_ContractContradiction_IsRefusedWhateverTheInstanceHolds()
    {
        // The contract is built from the type, so an empty instance is refused exactly like a
        // populated one: the decision never depends on a member's value.
        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(new Contradictory()));
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize(new Contradictory { Secret = "a secret" }));
    }

    [Fact]
    public void Deserialize_InterfaceWithoutUnion_ThrowsType()
    {
        byte[] payload = _serializer.Serialize(new Person { Name = "Alice", Age = 1 });

        Assert.Throws<BinaryTypeException>(() => _serializer.Deserialize<IComparable>(payload));
    }

    [Fact]
    public void Deserialize_AbstractClassWithoutUnion_ThrowsType()
    {
        byte[] payload = _serializer.Serialize(new Person { Name = "Alice", Age = 1 });

        Assert.Throws<BinaryTypeException>(() => _serializer.Deserialize<AbstractPerson>(payload));
    }
}
