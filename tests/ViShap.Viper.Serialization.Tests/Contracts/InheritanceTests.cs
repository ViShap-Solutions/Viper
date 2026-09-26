using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins the member plan across an inheritance chain: which members belong to it, how many times each
/// one appears, and in what order. Asking the most derived type alone — which is what reflection does
/// by default — answers all three wrongly, and none of the errors has a symptom until the day a
/// payload written by one process is read by another.
/// </summary>
public class InheritanceTests
{
    private readonly BinarySerializer _serializer = new();

    private byte[] Payload<T>(T value) => _serializer.Serialize(value)[Wire.PlainHeaderLength..];

    // --- a hidden member is a second member, and the order of the two is fixed --------------------

    [Fact]
    public void Serialize_AFieldHiddenByAnother_WritesBothWithTheBaseFirst()
    {
        var value = new ShadowedFieldDerived { Value = 7 };
        ((ShadowedFieldBase)value).Value = 9;

        byte[] payload = Payload(value);

        // Presence flag, then the base declaration, then the one that hides it.
        Assert.Equal([1, 9, 0, 0, 0, 7, 0, 0, 0], payload);
    }

    [Fact]
    public void Deserialize_AFieldHiddenByAnother_RestoresBoth()
    {
        var value = new ShadowedFieldDerived { Value = 7 };
        ((ShadowedFieldBase)value).Value = 9;

        var restored = _serializer.Deserialize<ShadowedFieldDerived>(_serializer.Serialize(value));

        Assert.NotNull(restored);
        Assert.Equal(7, restored.Value);
        Assert.Equal(9, ((ShadowedFieldBase)restored).Value);
    }

    [Fact]
    public void Serialize_APropertyHiddenByAnother_WritesBothWithTheBaseFirst()
    {
        var value = new ShadowedPropertyDerived { Value = 7 };
        ((ShadowedPropertyBase)value).Value = 9;

        byte[] payload = Payload(value);

        Assert.Equal([1, 9, 0, 0, 0, 7, 0, 0, 0], payload);
    }

    [Fact]
    public void Deserialize_APropertyHiddenByAnother_RestoresBoth()
    {
        var value = new ShadowedPropertyDerived { Value = 7 };
        ((ShadowedPropertyBase)value).Value = 9;

        var restored = _serializer.Deserialize<ShadowedPropertyDerived>(_serializer.Serialize(value));

        Assert.NotNull(restored);
        Assert.Equal(7, restored.Value);
        Assert.Equal(9, ((ShadowedPropertyBase)restored).Value);
    }

    [Fact]
    public void Serialize_AHiddenMember_ProducesTheSameLayoutEveryTime()
    {
        // The plan may not depend on the order reflection happened to return members in, so the same
        // type built again must produce the same bytes.
        var value = new ShadowedFieldDerived { Value = 7 };

        Assert.Equal(_serializer.Serialize(value), new BinarySerializer().Serialize(value));
    }

    // --- an overridden member is one member ------------------------------------------------------

    [Fact]
    public void Serialize_AnOverriddenProperty_WritesItOnce()
    {
        byte[] payload = Payload(new OverriddenDerived { Value = 42 });

        Assert.Equal([1, 42, 0, 0, 0], payload);
    }

    [Fact]
    public void Serialize_AnOverrideMarkedIgnore_HonoursTheOverridesAttribute()
    {
        // The attribute sits on the override, so that is the declaration the plan must consult.
        byte[] payload = Payload(new OverrideIgnoredDerived { Value = 3, Other = 8 });

        Assert.Equal([1, 8, 0, 0, 0], payload);
    }

    // --- a non-public base member stays part of the value ----------------------------------------

    [Fact]
    public void Serialize_ANonPublicBaseMember_StaysInThePlanOfTheDerivedType()
    {
        var value = new IncludedDerived { Added = 2, Open = 1 };
        value.SetHidden(5);

        byte[] payload = Payload(value);

        // Ordinal name order: Added, Open, then the base's _hidden.
        Assert.Equal([1, 2, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0], payload);
    }

    [Fact]
    public void Deserialize_ANonPublicBaseMember_RoundTrips()
    {
        var value = new IncludedDerived { Added = 2 };
        value.SetHidden(5);

        var restored = _serializer.Deserialize<IncludedDerived>(_serializer.Serialize(value));

        Assert.NotNull(restored);
        Assert.Equal(5, restored.Hidden);
        Assert.Equal(2, restored.Added);
    }

    [Fact]
    public void Serialize_TheBaseAlone_KeepsItsOwnPlan()
    {
        var value = new IncludedBase { Open = 1 };
        value.SetHidden(5);

        Assert.Equal([1, 1, 0, 0, 0, 5, 0, 0, 0], Payload(value));
    }

    // --- a keyed contract is inherited -----------------------------------------------------------

    [Fact]
    public void Serialize_ADerivedContractType_IsKeyedLikeItsBase()
    {
        var value = new KeyedContractDerived { Base = 1, Added = "x" };

        var restored = _serializer.Deserialize<KeyedContractDerived>(_serializer.Serialize(value));

        Assert.NotNull(restored);
        Assert.Equal(1, restored.Base);
        Assert.Equal("x", restored.Added);
    }

    [Fact]
    public void Deserialize_ADerivedContractPayload_ReadsAsTheBaseSkippingTheDerivedKey()
    {
        // The key space is shared across the hierarchy, which is what lets a reader holding only the
        // base skip a member it does not know.
        byte[] payload = _serializer.Serialize(new KeyedContractDerived { Base = 1, Added = "x" });

        var restored = _serializer.Deserialize<KeyedContractBase>(payload);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.Base);
    }

    [Fact]
    public void Serialize_ADerivedContractMemberWithoutAKey_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "Unmarked", () => _serializer.Serialize(new KeyedContractUnmarked()));
    }

    [Fact]
    public void Serialize_ADerivedContractKeyTheBaseAlreadyClaims_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "duplicate [BinaryKey]", () => _serializer.Serialize(new KeyedContractColliding()));
    }
}
