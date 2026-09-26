namespace ViShap.Viper.Serialization.Tests.Fixtures;

// --- NX-03: a member hidden by another of the same name --------------------------------------------

/// <summary>A base whose field a derived type hides with <c>new</c>.</summary>
public class ShadowedFieldBase
{
    public int Value = 1;
}

/// <summary>Both fields are part of the value, and the base one is written first.</summary>
public class ShadowedFieldDerived : ShadowedFieldBase
{
    public new int Value = 2;
}

/// <summary>The same shape for a property, which reflection hides by default.</summary>
public class ShadowedPropertyBase
{
    public int Value { get; set; } = 1;
}

public class ShadowedPropertyDerived : ShadowedPropertyBase
{
    public new int Value { get; set; } = 2;
}

// --- NX-04: a non-public base member the derived type inherits -------------------------------------

/// <summary>A base holding state that only <c>[BinaryInclude]</c> makes part of the value.</summary>
public class IncludedBase
{
    [BinaryInclude] private int _hidden = 5;

    public int Open = 1;

    public int Hidden => _hidden;

    public void SetHidden(int value) => _hidden = value;
}

/// <summary>Adding a member must not drop the one the base declared.</summary>
public class IncludedDerived : IncludedBase
{
    public int Added = 2;
}

// --- overriding, which is one member and not two ---------------------------------------------------

public class OverriddenBase
{
    public virtual int Value { get; set; } = 1;
}

public class OverriddenDerived : OverriddenBase
{
    public override int Value { get; set; } = 2;
}

/// <summary>An override carrying the attribute the plan must honour.</summary>
public class OverrideIgnoredBase
{
    public virtual int Value { get; set; } = 1;

    public int Other { get; set; } = 2;
}

public class OverrideIgnoredDerived : OverrideIgnoredBase
{
    [BinaryIgnore] public override int Value { get; set; } = 3;
}

// --- NX-05: a keyed contract extended by a derived type --------------------------------------------

/// <summary>A keyed contract meant to be inherited.</summary>
[BinaryContract]
public class KeyedContractBase
{
    [BinaryKey(1)] public int Base { get; set; }
}

/// <summary>The contract is inherited, so the derived member needs a key of its own.</summary>
public class KeyedContractDerived : KeyedContractBase
{
    [BinaryKey(2)] public string Added { get; set; } = string.Empty;
}

/// <summary>A derived member left unmarked, which the inherited contract refuses.</summary>
public class KeyedContractUnmarked : KeyedContractBase
{
    public int Unmarked { get; set; }
}

/// <summary>A derived member reusing a key the base already claimed.</summary>
public class KeyedContractColliding : KeyedContractBase
{
    [BinaryKey(1)] public int Collides { get; set; }
}
