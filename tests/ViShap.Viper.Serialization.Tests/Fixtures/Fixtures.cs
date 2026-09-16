namespace ViShap.Viper.Serialization.Tests.Fixtures;

public sealed class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public Address Address { get; set; } = new();
}

public sealed class Address
{
    public string City { get; set; } = "";
    public string Street { get; set; } = "";
}

public readonly record struct PointStruct(int X, int Y);

public sealed class DeepNode
{
    public int Value { get; set; }
    public DeepNode? Next { get; set; }
}

public sealed class SharedReferenceGraph
{
    public Person? Home { get; set; }
    public Person? Work { get; set; }
}

public sealed class CyclicNode
{
    public string Name { get; set; } = "";
    public CyclicNode? Next { get; set; }
}

[BinaryUnion(1, typeof(Dog))]
[BinaryUnion(2, typeof(Cat))]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Dog : Animal
{
    public int BarkVolume { get; set; }
}

public sealed class Cat : Animal
{
    public int Lives { get; set; }
}

[BinaryContract]
public sealed class ContractV1
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public int Age { get; set; }
}

[BinaryContract]
public sealed class ContractV2
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public int Age { get; set; }
    [BinaryKey(3)] public string NewField { get; set; } = "default";
}

public sealed class PrivateMemberFixture
{
    public int PublicValue { get; set; }
    [BinaryInclude] private string PrivateValue { get; set; } = "";
    public string ReadPrivate() => PrivateValue;
    public void SetPrivate(string value) => PrivateValue = value;
}


public sealed class PrivateFieldFixture
{
    [BinaryInclude] private int PrivateValue;
    public int ReadPrivate() => PrivateValue;
    public void SetPrivate(int value) => PrivateValue=value;
}

public sealed class IgnoredMemberFixture
{
    public int Included { get; set; }
    [BinaryIgnore] public int Ignored { get; set; }
}


public sealed class CombinedMemberFixture
{
    [BinaryIgnore] public int Ignored {get;set;}
    [BinaryInclude] private int IncludedPrivate;
    public int ReadIncluded() => IncludedPrivate;
    public void SetIncluded(int value)=>IncludedPrivate=value;
}

public sealed class OrderedMemberFixture
{
    [BinaryOrder(2)] public int B { get; set; }
    [BinaryOrder(1)] public int A { get; set; }
    public int C { get; set; }
}

public sealed class DuplicateOrderFixture
{
    [BinaryOrder(1)] public int A { get; set; }
    [BinaryOrder(1)] public int B { get; set; }
}

public sealed class InvalidKeyWithoutContractFixture
{
    [BinaryKey(1)] public int A { get; set; }
}

[BinaryContract]
public sealed class InvalidContractFixture
{
    public int MissingDecision { get; set; }
}

[BinaryContract]
public sealed class ContractIncludeFixture
{
    [BinaryInclude] public int A { get; set; }
}

[BinaryContract]
public sealed class ContractOrderFixture
{
    [BinaryOrder(1)] [BinaryKey(1)] public int A { get; set; }
}

[BinaryContract]
public sealed class DuplicateKeyFixture
{
    [BinaryKey(1)] public int A { get; set; }
    [BinaryKey(1)] public int B { get; set; }
}

public sealed class InvalidUnionDerived : Animal { }

public sealed class CustomCollection<T> : ICollection<T>
{
    private readonly List<T> _items = [];
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public void Add(T item) => _items.Add(item);
    public void Clear() => _items.Clear();
    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public bool Remove(T item) => _items.Remove(item);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class UnsupportedTypeFixture
{
    public Action Callback { get; set; } = () => { };
}

public sealed class LargeRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<int> Values { get; set; } = [];
    public Dictionary<string,string> Metadata { get; set; } = new();
}

public sealed class UnicodeFixture
{
    public string Text { get; set; } = "Привет世界 e\u0301 😀";
    public string Combining { get; set; } = "a\u0301o\u0308";
}

[BinaryUnion(1, typeof(UnrelatedUnionType))]
public abstract class InvalidUnionFixture { }
public sealed class UnrelatedUnionType { }

public sealed class CollectionFixtures
{
    public List<int> Values { get; set; } = [];
}

public sealed class DictionaryFixtures
{
    public Dictionary<string,int> Values { get; set; } = new();
}
