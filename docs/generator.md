# The source generator

`ViShap.Viper.Generator` writes the [type contracts](contracts.md#supplying-a-contract) of your types at
build time. The serializer then reads and writes them through plain generated code instead of
describing them by reflection the first time it meets them, and every contract mistake — a duplicate
key, an unmarked member of a keyed contract, a delegate member — becomes a compiler diagnostic instead
of an exception at run time.

The generator ships with the `ViShap.Viper` package, or on its own as `ViShap.Viper.Generator`. It runs
only in the compiler: nothing of it reaches your application, and a project with no `[BinaryContext]`
class gets nothing from it.

## A context

List the root types on a `partial` class deriving from `BinarySerializerContext`, and give its
`Default` instance to the options:

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithContracts(AppContracts.Default)
    .RequireGeneratedContracts()
    .Build());

byte[] bytes = serializer.Serialize(new Order { Id = 7, Lines = [new Line { Sku = "A-1", Quantity = 2 }] });
Order? copy = serializer.Deserialize<Order>(bytes);
Console.WriteLine($"{copy!.Id}: {copy.Lines![0].Sku} x{copy.Lines[0].Quantity}");   // 7: A-1 x2

public sealed class Order
{
    public int Id { get; set; }
    public List<Line>? Lines { get; set; }
}

public sealed class Line
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}

[BinaryContext(typeof(Order))]
public partial class AppContracts : BinarySerializerContext;
```

The generator writes a contract for each listed type and for every member-encoded type it reaches —
through members, array and collection elements, dictionary keys and values, nullable values and
`[BinaryUnion]` arms. `Line` above needs no entry of its own. It also writes the context's public
parameterless constructor and its `Default` property, so the class declares neither.

Each contract describes its type exactly as the serializer does by reflection: the same members, in
the same order, with the same keys. Payloads are the same bytes with the context and without it, so a
service that uses the generator and one that does not read each other's data.

`RequireGeneratedContracts()` makes a member-encoded type the context does not hold an error —
`BinaryConfigurationException` naming it — instead of a quiet fallback to reflection.

## What is reached, and how

Public members are read and assigned directly. A non-public member marked `[BinaryInclude]` or
`[BinaryKey]`, a property with an `init` or a non-public setter, and a constructor that is not public or
that would leave `required` members unset are reached through `[UnsafeAccessor]`. A type does not have
to be `partial` and does not have to be yours to change.

A type gets no generated contract, and is described by reflection as before, when the generator cannot
see or name all of it: a type declared in another assembly or deriving from one, a type not accessible
from the context, an open generic type, or a type with a member that implements an interface
explicitly. Warning [VPR019](#vpr019) names each one and why.

## Trimming and native AOT

A generated contract removes the reflection over a type's members. The codecs around the members are
still built at run time over the declared types, so the serializer's entry points keep their trimming
and AOT annotations with or without a context — see [Trimming and native AOT](aot.md).

## Diagnostics

### VPR001

**A `[BinaryContext]` class must be partial** (error). The generator adds the contracts, the constructor
and `Default` to the class, which it can do only to a `partial` class — and only if every type it is
nested in is `partial` too. Add `partial`.

### VPR002

**A `[BinaryContext]` class must be a non-generic, non-abstract class deriving from
`BinarySerializerContext`** (error). Derive it from `BinarySerializerContext`, and make it a concrete,
non-generic class that is not nested in a generic type.

### VPR003

**A `[BinaryContext]` class must not declare what the generator writes** (error): its parameterless
constructor or a member named `Default`. Remove them; to add contracts of your own, write a separate
context.

### VPR004

**`[BinaryKey]` on a type that is not `[BinaryContract]`** (error). Keys belong to keyed contracts: mark
the type `[BinaryContract]`, or remove the key and use `[BinaryOrder]`.

### VPR005

**`[BinaryInclude]` together with `[BinaryIgnore]`** (error). Keep the one you mean.

### VPR006

**Duplicate `[BinaryOrder]` value** (error). Give each ordered member its own position.

### VPR007

**`[BinaryInclude]` on a member of a contract type** (error). In a keyed contract `[BinaryKey]` already
includes a member whatever its visibility; remove `[BinaryInclude]`.

### VPR008

**`[BinaryOrder]` on a member of a contract type** (error). A keyed contract is ordered by its keys;
remove `[BinaryOrder]`.

### VPR009

**`[BinaryKey]` together with `[BinaryIgnore]`** (error). Keep the one you mean.

### VPR010

**Contract member without `[BinaryKey]` or `[BinaryIgnore]`** (error). Every member of a keyed contract
— a private field included, an inherited one too — states whether it travels: give it a key or mark it
`[BinaryIgnore]`.

### VPR011

**Negative `[BinaryKey]` value** (error). Keys are non-negative.

### VPR012

**Duplicate `[BinaryKey]` value** (error). Keys are one space across the whole hierarchy; give each
member its own, and never reuse one that payloads already carry.

### VPR013

**Delegate member** (error). A delegate is behaviour, not data. Mark the member `[BinaryIgnore]`.

### VPR014

**Member type with no representation on the wire** (error): a pointer, a function pointer or a
`ref struct`. Mark the member `[BinaryIgnore]`.

### VPR015

**`[BinaryUnion]` tag outside 0–255** (error). A tag is one byte.

### VPR016

**Duplicate `[BinaryUnion]` tag** (error). Give each derived type its own tag, and never reuse one that
payloads already carry.

### VPR017

**`[BinaryUnion]` type not assignable to the base** (error). Name a type that derives from the class, or
implements the interface, the attribute is on.

### VPR018

**Abstract type or interface without `[BinaryUnion]`** (warning). A value declared as such a type cannot
be read back — the serializer would not know what to create. Declare the types it may hold with
`[BinaryUnion]`, or declare the member as a concrete type.

### VPR019

**Type described by reflection** (warning). The type gets no generated contract, for the reason the
message gives, and is described by reflection at run time — or refused, under
`RequireGeneratedContracts()`. Move the type into the project, make it accessible from the context, or
mark the member that stands in the way `[BinaryIgnore]`.
