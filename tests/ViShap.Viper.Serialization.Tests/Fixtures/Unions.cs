namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>A base with three registered implementations, each identified by its own tag.</summary>
[BinaryUnion(1, typeof(Circle))]
[BinaryUnion(2, typeof(Square))]
[BinaryUnion(3, typeof(Triangle))]
public abstract class Shape
{
    public string? Label { get; set; }
}

public class Circle : Shape
{
    public double Radius { get; set; }
}

public class Square : Shape
{
    public double Side { get; set; }
}

public class Triangle : Shape
{
    public double Base { get; set; }
}

/// <summary>The tags at both ends of the admitted byte range.</summary>
[BinaryUnion(0, typeof(TagZero))]
[BinaryUnion(255, typeof(TagMax))]
public abstract class TagRange;

public class TagZero : TagRange
{
    public int Value { get; set; }
}

public class TagMax : TagRange
{
    public int Value { get; set; }
}

/// <summary>Two implementations claiming one tag.</summary>
[BinaryUnion(1, typeof(FirstClaim))]
[BinaryUnion(1, typeof(SecondClaim))]
public abstract class DuplicateTagBase;

public class FirstClaim : DuplicateTagBase;

public class SecondClaim : DuplicateTagBase;

/// <summary>A tag one above the byte range.</summary>
[BinaryUnion(256, typeof(AboveRange))]
public abstract class AboveRangeBase;

public class AboveRange : AboveRangeBase;

/// <summary>A tag one below the byte range.</summary>
[BinaryUnion(-1, typeof(BelowRange))]
public abstract class BelowRangeBase;

public class BelowRange : BelowRangeBase;

/// <summary>A declared known type that does not derive from the base it is declared on.</summary>
[BinaryUnion(1, typeof(Person))]
public abstract class UnrelatedKnownTypeBase;

/// <summary>
/// A union touched by nothing but the concurrency test, so the first resolution of its map really is
/// the one the parallel workers race for.
/// </summary>
[BinaryUnion(1, typeof(RacedDerived))]
public class RacedBase
{
    public int Z { get; set; }
}

public class RacedDerived : RacedBase
{
    public int A { get; set; }
}

/// <summary>A union whose members can close a cycle.</summary>
[BinaryUnion(1, typeof(CyclicDerived))]
public class CyclicBase
{
    public CyclicBase? Next { get; set; }
}

public class CyclicDerived : CyclicBase
{
    public int A { get; set; }
}
