using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-22: a delegate carries behaviour rather than data, so it has no wire representation. It
/// is refused as a root value, as an element, and as a member — a member that holds one must say so
/// with <c>[BinaryIgnore]</c> rather than being dropped in silence.
/// </summary>
public class DelegateMemberTests
{
    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void Serialize_DelegateAsRoot_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize<Func<int>>(() => 1));
    }

    [Fact]
    public void Serialize_DelegateAsAnElement_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize(new List<Func<int>> { () => 1 }));
    }

    [Fact]
    public void Serialize_DelegateProperty_ThrowsTypeNamingTheMember()
    {
        AssertEx.Throws<BinaryTypeException>(
            "Callback",
            () => _serializer.Serialize(new WithDelegate { Value = 1, Callback = () => 1 }));
    }

    [Fact]
    public void Serialize_DelegateField_ThrowsTypeNamingTheMember()
    {
        AssertEx.Throws<BinaryTypeException>(
            "Handler", () => _serializer.Serialize(new WithDelegateField()));
    }

    [Fact]
    public void Serialize_NullDelegateProperty_StillThrows()
    {
        // The plan is built from the type, so the outcome cannot depend on whether the caller
        // happened to leave the callback unset.
        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(new WithDelegate()));
    }

    [Fact]
    public void Serialize_IgnoredDelegateProperty_RoundTripsTheRest()
    {
        var source = new WithIgnoredDelegate { Value = 42, Callback = () => 1 };

        var result = _serializer.Deserialize<WithIgnoredDelegate>(_serializer.Serialize(source))!;

        Assert.Equal(42, result.Value);
        Assert.Null(result.Callback);
    }

    [Fact]
    public void Serialize_TypeWithAnEvent_IsUnaffected()
    {
        // An event's backing field is private, so it was never an eligible member to begin with.
        var source = new WithEvent { Value = 7 };
        source.Changed += (_, _) => { };

        Assert.Equal(7, _serializer.Deserialize<WithEvent>(_serializer.Serialize(source))!.Value);
    }

    [Fact]
    public void Serialize_KeyedDelegateMember_ThrowsTypeNamingTheMember()
    {
        AssertEx.Throws<BinaryTypeException>(
            "Callback", () => _serializer.Serialize(new ContractWithKeyedDelegate()));
    }

    [Fact]
    public void Serialize_KeyedContractIgnoringItsDelegate_RoundTripsTheRest()
    {
        var source = new ContractWithIgnoredDelegate { Value = 9, Callback = () => 1 };

        var result = _serializer.Deserialize<ContractWithIgnoredDelegate>(
            _serializer.Serialize(source))!;

        Assert.Equal(9, result.Value);
        Assert.Null(result.Callback);
    }

    [Fact]
    public void Serialize_KeyedContractWithAnUnmarkedDelegate_AsksForADecision()
    {
        // The contract rule fires first and already tells the caller what to add.
        AssertEx.Throws<BinaryTypeException>(
            "exactly one", () => _serializer.Serialize(new ContractWithUnmarkedDelegate()));
    }
}
