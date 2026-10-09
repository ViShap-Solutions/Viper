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

## Supplying a contract

The serializer describes each member-encoded type by reflection the first time it meets it. A
*type contract* can be supplied instead: a `TypeContract<T>` that states the type's layout and members
and reads and writes them in plain code. The serializer uses it for every operation of the options it
is given to, and keeps doing everything around the members itself — nulls, object references, union
tags, field keys and lengths, limits.

Contracts are supplied through a `BinarySerializerContext`. A derived class adds one contract per type
from its constructor, and `WithContracts` gives the context to the options:

```csharp
using ViShap.Viper;
using ViShap.Viper.Contracts;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithContracts(AppContracts.Default)
    .Build());

Customer? copy = serializer.Deserialize<Customer>(serializer.Serialize(new Customer { Name = "Ada", Age = 36 }));
Console.WriteLine($"{copy!.Name}, {copy.Age}");   // Ada, 36

[BinaryContract]
public sealed class Customer
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public int Age { get; set; }
}

public sealed class AppContracts : BinarySerializerContext
{
    public static AppContracts Default { get; } = new();

    private AppContracts() => Add(new CustomerContract());

    private sealed class CustomerContract() : TypeContract<Customer>(
        MemberLayout.Keyed,
        [new("Name", typeof(string), 1), new("Age", typeof(int), 2)],
        canBeConstructed: true)
    {
        public override Customer Create() => new();

        public override void Write(ref MemberWriter writer, in Customer value)
        {
            writer.Member(1, value.Name);
            writer.Member(2, value.Age);
        }

        public override void ReadPositional(ref MemberReader reader, ref Customer value) =>
            throw new NotSupportedException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Customer value)
        {
            switch (key)
            {
                case 1: value.Name = reader.Member<string>(); return true;
                case 2: value.Age = reader.Member<int>(); return true;
                default: return false;
            }
        }
    }
}
```

A contract implements four methods:

| Method | What it does |
|---|---|
| `Create` | the instance a payload is read into: a new object, or `default` for a struct |
| `Write` | one `writer.Member(value)` per member in order under a positional layout; one `writer.Member(key, value)` per member in ascending key order under a keyed one |
| `ReadPositional` | positional layout: one `reader.Member<T>()` per member, in the same order |
| `ReadKeyed` | keyed layout: called once for each field of the payload, with its key; a known key reads its member with one `reader.Member<T>()` and returns `true`, an unknown one returns `false` and the field is skipped |

The serializer checks every call against the description given to the constructor — the member, its
exact type, its key, how many calls — and a call that disagrees is a `BinaryTypeException` naming the
type and the member, never a damaged payload. A contract that describes its type as reflection does —
the same layout, the same members in the same order, the same types and keys — writes exactly the bytes
reflection writes, so payloads move freely between serializers with and without the context.

Rules worth knowing:

- Use the `MemberWriter` and `MemberReader` through the reference you are handed. Passing one to a
  helper by value, or keeping a copy, is refused with `BinaryTypeException` as soon as either copy is
  used after the other; pass it on by `ref`.
- A description that cannot be checked — a key under a positional layout, a keyed member without a key,
  keys out of order or repeated, a negative key — is a `BinaryConfigurationException` from the
  constructor. So is a second contract for the same type in one context, or a contract added after
  options were built with the context.
- A type the context does not hold is described by reflection. `RequireGeneratedContracts()` makes that
  a `BinaryConfigurationException` naming the type instead, so a forgotten type cannot pass unnoticed.
- A contract is shared by every operation and thread that uses the options: keep no per-call state in
  it.
- A member that is not public is reached the way any code outside the type reaches it — for example
  through `[UnsafeAccessor]`.
