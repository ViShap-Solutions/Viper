namespace ViShap.Viper.Serialization.Tests.Fixtures;

// The object shapes of the contract conformance suite (Contracts/ConformanceTests). Each type is
// partial, so a contract generated beside it can be run against the same cases as the reflected one.

// --- positional -------------------------------------------------------------------------------------

/// <summary>
/// Every inclusion rule of a positional layout: an explicit order, the ordinal fallback, a public
/// field, a non-public field under <c>[BinaryInclude]</c>, a nested struct, and two members that are
/// not part of the value — one ignored, one without a setter.
/// </summary>
public partial class ConformancePositional
{
    [BinaryInclude] private double _ratio;

    public bool Flag;

    [BinaryOrder(1)] public int Id { get; set; }

    public string? Name { get; set; }

    public ConformancePoint Origin { get; set; }

    [BinaryIgnore] public int Skipped { get; set; }

    public int IdView => Id;

    public double GetRatio() => _ratio;

    public void SetRatio(double ratio) => _ratio = ratio;
}

// --- keyed ------------------------------------------------------------------------------------------

/// <summary>
/// A keyed layout: keys declared out of order, a key that takes two varint bytes, a non-public keyed
/// field, an ignored member, and a member whose constructor default survives a payload that omits it.
/// </summary>
[BinaryContract]
public partial class ConformanceKeyed
{
    [BinaryKey(2)] private short _code;

    [BinaryKey(3)] public string? Name { get; set; }

    [BinaryKey(1)] public int Id { get; set; }

    [BinaryKey(200)] public long Big { get; set; }

    [BinaryKey(5)] public int Defaulted { get; set; } = 42;

    [BinaryIgnore] public int Skipped { get; set; }

    public short GetCode() => _code;

    public void SetCode(short code) => _code = code;
}

// --- inherited --------------------------------------------------------------------------------------

/// <summary>A positional base whose members interleave with a derived type's by name, one of them non-public.</summary>
public partial class ConformanceBase
{
    [BinaryInclude] private int _hidden;

    public int Alpha { get; set; }

    public int Zulu { get; set; }

    public int GetHidden() => _hidden;

    public void SetHidden(int hidden) => _hidden = hidden;
}

public partial class ConformanceDerived : ConformanceBase
{
    public int Mike { get; set; }
}

/// <summary>A keyed base; the hierarchy shares one key space.</summary>
[BinaryContract]
public partial class ConformanceKeyedBase
{
    [BinaryKey(1)] public int First { get; set; }

    [BinaryKey(3)] public int Third { get; set; }
}

public partial class ConformanceKeyedDerived : ConformanceKeyedBase
{
    [BinaryKey(2)] public int Second { get; set; }
}

// --- shadowed ---------------------------------------------------------------------------------------

public partial class ConformanceShadowBase
{
    public int Value { get; set; }
}

/// <summary>A member hidden with <c>new</c> is a second member of another type; the base declaration comes first.</summary>
public partial class ConformanceShadowed : ConformanceShadowBase
{
    public new string? Value { get; set; }
}

// --- overridden -------------------------------------------------------------------------------------

public partial class ConformanceVirtualBase
{
    public virtual int Value { get; set; }

    public int Other { get; set; }
}

/// <summary>An override is one member, and the order written on the override is the one that applies.</summary>
public partial class ConformanceOverridden : ConformanceVirtualBase
{
    [BinaryOrder(0)] public override int Value { get; set; }
}

// --- union ------------------------------------------------------------------------------------------

/// <summary>An abstract base whose values travel as one of two tagged runtime types.</summary>
[BinaryUnion(1, typeof(ConformanceCircle))]
[BinaryUnion(2, typeof(ConformanceSquare))]
public abstract partial class ConformanceShape
{
    public string? Label { get; set; }
}

public sealed partial class ConformanceCircle : ConformanceShape
{
    public double Radius { get; set; }
}

public sealed partial class ConformanceSquare : ConformanceShape
{
    public int Side { get; set; }
}

// --- struct -----------------------------------------------------------------------------------------

/// <summary>A positional struct: created as <see langword="default"/>, assigned in place.</summary>
public partial struct ConformancePoint
{
    public int X;

    public int Y;
}

/// <summary>A keyed struct, which cannot be null, so its field count is written as it is.</summary>
[BinaryContract]
public partial struct ConformanceKeyedPoint
{
    [BinaryKey(1)] public int X;

    [BinaryKey(2)] public string? Tag;
}
