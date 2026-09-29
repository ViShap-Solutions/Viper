# Contracts and schema evolution

A class or struct that no dedicated encoding claims is written member by member. Two layouts decide
which members, in which order, and how a reader finds them again:

| | Positional (the default) | Keyed (`[BinaryContract]`) |
|---|---|---|
| members are found by | their position | an explicit number, their key |
| which members are written | the public read/write ones, plus those marked `[BinaryInclude]` | every member marked `[BinaryKey(n)]` |
| what a reader does with an unknown member | nothing: the layouts must match | skips it by its declared length |
| a member the payload lacks | there is none: the layouts must match | keeps its default value |
| use it for | data read by the code that wrote it | data that is stored or exchanged, and will outlive the code that wrote it |

A type is written the same way under V0 and under V1, and to any destination.

## Positional layout

With no annotation, the members written are the public properties with a getter and a setter and the
public fields that are not `readonly`. Compiler-generated fields and indexers are not members; a
get-only property is not written, and keeps whatever its initializer gives it.

Members are written in this order: those carrying `[BinaryOrder(n)]` first, by ascending `n`, then the
rest by ordinal name. **The order is the format.** Without `[BinaryOrder]`, renaming a member changes
what is written, and adding, removing or reordering a member breaks every payload that already exists.

| Attribute | Effect |
|---|---|
| `[BinaryOrder(n)]` | pins the position of a member, independently of its name; two members of a type may not share a value |
| `[BinaryIgnore]` | leaves a member out; it keeps its default value after reading |
| `[BinaryInclude]` | includes a member that is not public |

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

var account = new Account { Id = 7, Owner = "Ada", Balance = 12.5m, DisplayName = "Ada L." };
account.Revise(3);

Account? copy = serializer.Deserialize<Account>(serializer.Serialize(account));

Console.WriteLine(copy!.Owner + " " + copy.Balance);
Console.WriteLine(copy.Revision);
Console.WriteLine(copy.DisplayName is null);

public sealed class Account
{
    [BinaryOrder(1)] public long Id { get; set; }
    [BinaryOrder(2)] public string Owner { get; set; } = "";
    public decimal Balance { get; set; }

    [BinaryIgnore] public string? DisplayName { get; set; }

    [BinaryInclude] private int _revision;

    public int Revision => _revision;
    public void Revise(int revision) => _revision = revision;
}
```

The plan covers the whole inheritance chain, level by level from the concrete type up to `object`. A
non-public member of a base class that carries `[BinaryInclude]` belongs to the plan exactly as it does
when the base class is serialized alone. A member that overrides another is one member, with the
attributes written on the override. A member that hides another with `new` is a second member: both are
written, the base declaration first.

### Delegates

A delegate carries behaviour, not data, so a member of a delegate type is rejected with
`BinaryTypeException` naming it — whether or not it is null at the time. Mark it `[BinaryIgnore]`.
An event is unaffected: its backing field is private and is not a member.

### Construction

The reader creates the object itself. A class needs a parameterless constructor, of any visibility; a
struct is created as `default`. A type that has none — an interface, an abstract class, a class that only
has constructors with parameters — is a `BinaryTypeException` on read, unless a
[`[BinaryUnion]`](polymorphism.md) map names the concrete type to build.

## Keyed layout

`[BinaryContract]` switches a type to keys. Every eligible member — the ones the positional layout would
consider — must then carry exactly one decision: `[BinaryKey(n)]` to write it under the key `n`, or
`[BinaryIgnore]` to leave it out. Each field is written with its key and its length, and fields appear
in ascending key order.

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

// what an older service writes
byte[] fromOld = serializer.Serialize(new CustomerV1 { Name = "Grace", Age = 45 });

// what a newer service writes
byte[] fromNew = serializer.Serialize(new CustomerV2
{
    Name = "Ada",
    Age = 36,
    Email = "ada@example.org"
});

// the newer reader reads old data: the missing key keeps its default
CustomerV2? upgraded = serializer.Deserialize<CustomerV2>(fromOld);
Console.WriteLine(upgraded!.Name + " " + (upgraded.Email is null));

// the older reader reads new data: the key it does not know is skipped
CustomerV1? downgraded = serializer.Deserialize<CustomerV1>(fromNew);
Console.WriteLine(downgraded!.Name + " " + downgraded.Age);

[BinaryContract]
public sealed class CustomerV1
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public int Age { get; set; }
}

[BinaryContract]
public sealed class CustomerV2
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public int Age { get; set; }
    [BinaryKey(3)] public string? Email { get; set; }
    [BinaryIgnore] public string? CachedDisplayName { get; set; }
}
```

The type's name is never written, so the two versions above may live in different assemblies under any
names.

### Rules for evolving a keyed type

- **Add a member with a new key.** Old readers skip it; new readers reading old data leave it at its
  default.
- **Remove a member by no longer writing it.** Leave its key retired: never reuse a key for a different
  member, or a payload that still carries the old one would be read into the new member.
- **Keep a key's type.** Changing the type of the member behind a key is not tolerated; give the new
  type a new key.
- A field the reader knows is read exactly: a field payload longer or shorter than its member's encoding
  is a `BinaryFormatException`.
- Unknown fields are skipped by their length, without being copied. They still count against
  `MaxKeyedFields` and `MaxTotalKeyedFields`.
- Keys are non-negative and written as compact integers, so small keys cost marginally less.

A payload that repeats a key or lists keys out of ascending order is malformed and is rejected with a
`BinaryFormatException`, whether or not the reader knows the key.

### Inheritance

`[BinaryContract]` is inherited. A type that extends a contract type is itself a contract type, and each
member it adds needs its own `[BinaryKey]` or `[BinaryIgnore]`. The hierarchy shares one key space, so a
key the base class claims cannot be reused below it. That is what makes a hierarchy useful: a reader
that holds only the base class skips the members a derived class added, exactly as it skips any key it
does not know.

### What is rejected

Every contradiction is a `BinaryTypeException`, raised the first time the type is used and again on every
later use, since a type that fails to build is not kept:

- `[BinaryKey]` on a type that does not have `[BinaryContract]`;
- `[BinaryOrder]` or `[BinaryInclude]` on a member of a contract type;
- a member of a contract type with no `[BinaryKey]` and no `[BinaryIgnore]`;
- `[BinaryKey]` together with `[BinaryIgnore]`, or `[BinaryInclude]` together with `[BinaryIgnore]`;
- two members with the same key, or the same order;
- a member of a delegate type.

### Keyed layouts and the destination

Each field's length is written ahead of the field and filled in once the field's size is known. That is
done in the serializer's own buffer, before anything reaches the destination, so a keyed type can be
written to a stream that cannot seek, to a pipe, or to a buffer writer, under V0 or V1.

### Keyed layouts and `Populate`

[`Populate`](entry-points.md#populating-an-existing-object) reads a payload into an existing instance.
With a keyed contract a member whose key the payload does not carry keeps the value it already has.
