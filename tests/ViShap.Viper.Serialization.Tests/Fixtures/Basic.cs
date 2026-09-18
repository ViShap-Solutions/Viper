namespace ViShap.Viper.Serialization.Tests.Fixtures;

public class Person
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

public struct PointStruct
{
    public int X { get; set; }
    public int Y { get; set; }
}
public class Tree : List<Tree>;

public class Node
{
    public int Value { get; set; }
}

public class SharedLists
{
    public List<int>? A { get; set; }
    public List<int>? B { get; set; }
}

[BinaryContract]
public class EmptyContract;

[BinaryContract]
public class NewSchema
{
    [BinaryKey(1)] public Node? Removed { get; set; }
    [BinaryKey(2)] public Node? Kept { get; set; }
}

[BinaryContract]
public class OldSchema
{
    [BinaryKey(2)] public Node? Kept { get; set; }
}

public class Base
{
    public int Z { get; set; }
}

public class Derived : Base
{
    public int A { get; set; }
}

[BinaryUnion(1, typeof(UnionDerived))]
public class UnionBase
{
    public int Z { get; set; }
}

public class UnionDerived : UnionBase
{
    public int A { get; set; }
}

public class Cyclic
{
    public string Name { get; set; } = string.Empty;
    public Cyclic? Next { get; set; }
}

[BinaryContract]
public class Contradictory
{
    [BinaryKey(1), BinaryIgnore] public string? Secret { get; set; }
}

public class ContradictoryPositional
{
    [BinaryInclude, BinaryIgnore] private int _secret = 42;

    public int Visible { get; set; }
}
