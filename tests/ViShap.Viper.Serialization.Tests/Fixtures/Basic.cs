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

[BinaryContract]
public class NestedSchema
{
    [BinaryKey(1)] public NewSchema? Inner { get; set; }
    [BinaryKey(2)] public string? Tag { get; set; }
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

[BinaryUnion(0, typeof(TaggedBase))]
[BinaryUnion(1, typeof(TaggedDerived))]
public class TaggedBase
{
    public int Z { get; set; }
}

public class TaggedDerived : TaggedBase
{
    public int A { get; set; }
}

public class WithDelegate
{
    public int Value { get; set; }
    public Func<int>? Callback { get; set; }
}

public class WithIgnoredDelegate
{
    public int Value { get; set; }

    [BinaryIgnore] public Func<int>? Callback { get; set; }
}

public class WithDelegateField
{
    public Action? Handler;
}

public class WithEvent
{
    public int Value { get; set; }

    public event EventHandler? Changed;

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

[BinaryContract]
public class ContractWithKeyedDelegate
{
    [BinaryKey(1)] public Func<int>? Callback { get; set; }
}

[BinaryContract]
public class ContractWithIgnoredDelegate
{
    [BinaryKey(1)] public int Value { get; set; }

    [BinaryIgnore] public Func<int>? Callback { get; set; }
}

[BinaryContract]
public class ContractWithUnmarkedDelegate
{
    [BinaryKey(1)] public int Value { get; set; }

    public Func<int>? Callback { get; set; }
}
