# References

By default every value is written where it is met. An object that is reachable along two paths is
written twice and read back as two objects, and a graph that leads back to itself cannot be written at
all. `PreserveReferences()` changes that: shared objects keep their identity and cycles round-trip.

```csharp
using ViShap.Viper;

var shared = new Address { City = "London" };
var pair = new Pair { First = shared, Second = shared };

var plain = new BinarySerializer();
var preserving = new BinarySerializer(BinarySerializerOptions.Configure()
    .PreserveReferences()
    .Build());

Pair? withoutIdentity = plain.Deserialize<Pair>(plain.Serialize(pair));
Pair? withIdentity = preserving.Deserialize<Pair>(preserving.Serialize(pair));

Console.WriteLine(ReferenceEquals(withoutIdentity!.First, withoutIdentity.Second));
Console.WriteLine(ReferenceEquals(withIdentity!.First, withIdentity.Second));

public sealed class Address
{
    public string City { get; set; } = "";
}

public sealed class Pair
{
    public Address? First { get; set; }
    public Address? Second { get; set; }
}
```

## What is tracked

Identity covers every structural **reference** type: objects written member by member, arrays,
collections, dictionaries and the other containers. It is decided by reference and never by an
overridden `Equals`.

Value types are never tracked, since a boxed struct cannot be shared. Scalars, strings included, are not
tracked either: two equal strings are written twice and read back as equal, not identical, values.

## Cycles

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;

var parent = new Node { Name = "parent" };
var child = new Node { Name = "child", Parent = parent };
parent.Children.Add(child);

try
{
    new BinarySerializer().Serialize(parent);
}
catch (BinaryTypeException)
{
    Console.WriteLine("a cycle needs PreserveReferences");
}

var preserving = new BinarySerializer(BinarySerializerOptions.Configure()
    .PreserveReferences()
    .Build());

Node? copy = preserving.Deserialize<Node>(preserving.Serialize(parent));
Console.WriteLine(ReferenceEquals(copy, copy!.Children[0].Parent));

public sealed class Node
{
    public string Name { get; set; } = "";
    public Node? Parent { get; set; }
    public List<Node> Children { get; set; } = [];
}
```

Without `PreserveReferences`, a cycle is found by searching the path from the root to the value being
written, and is a `BinaryTypeException` with the same message at every depth. That is a check on the
path, not on the whole graph: a value that appears more than once without being its own ancestor —
shared between siblings, or reachable along two paths — is not a cycle. It is simply written again.

## What the option changes

- **V1 only.** The frame records in its header whether the payload carries references, and a reader
  follows the frame, whatever the reader itself is configured with. A [V0 payload](formats.md) has no
  header, so `PreserveReferences` does not apply to it and a cycle is a `BinaryTypeException`.
- **Every structural reference gets a reference frame** in front of it saying whether it is null, a first
  occurrence, or a back reference to an earlier one. Ids are written explicitly rather than implied by
  the order of first occurrences, which is what keeps a reader that skips an unknown keyed field in step
  with the writer.
- **A payload that misuses the frames is rejected** with a `BinaryFormatException`: an unknown id, an id
  declared a second time, or a back reference to an object the declared type cannot hold.
- The reference tables used by a call come from pools and go back cleared, so no object of one call is
  reachable from the next.

## Where a cycle can close

A container that exists before its children are read — an object written member by member, a mutable
collection or dictionary — is registered first, so a cycle that passes through it resolves. A container
that cannot exist until its children are known — an array, an immutable or frozen collection, a tuple —
is registered only once it is complete. A back reference that resolves to one of those while it is still
being built is a `BinaryFormatException` on read, never a silently half-built instance.

## References and keyed contracts

A [keyed contract](contracts.md#keyed-layout) is meant to survive a reader that skips fields it does not
know. To keep `PreserveReferences` compatible with that, reference ids are unique across the payload but
visible only along the chain of ancestors: entering a keyed field opens a scope, and leaving it closes
one. Two consequences follow:

- a cycle back to an ancestor still resolves, because ancestors stay visible;
- an object shared between two *sibling* keyed fields is written twice and read as two instances. That is
  the cost of a reader being able to skip a field without ever meeting a reference into it.

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .PreserveReferences()
    .Build());

var shared = new Address { City = "London" };
var record = new Record { Home = shared, Work = shared };

Record? copy = serializer.Deserialize<Record>(serializer.Serialize(record));
Console.WriteLine(ReferenceEquals(copy!.Home, copy.Work));

[BinaryContract]
public sealed class Record
{
    [BinaryKey(1)] public Address? Home { get; set; }
    [BinaryKey(2)] public Address? Work { get; set; }
}

public sealed class Address
{
    public string City { get; set; } = "";
}
```

Use a positional type, as in the first example of this page, when identity between siblings matters.
