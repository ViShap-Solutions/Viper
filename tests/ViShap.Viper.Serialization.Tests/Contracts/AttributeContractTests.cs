using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-12, CTR-18, CTR-21: a declaration that contradicts itself is refused when the contract is
/// built, and a type the reader could not construct is refused rather than guessed at.
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
    public void Deserialize_InterfaceWithoutUnion_ThrowsType()
    {
        byte[] payload = _serializer.Serialize(new Person { Name = "Alice", Age = 1 });

        Assert.Throws<BinaryTypeException>(() => _serializer.Deserialize<IComparable>(payload));
    }
}
