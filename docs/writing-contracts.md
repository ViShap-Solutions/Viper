# Writing contracts by hand

The serializer describes each member-encoded type by reflection the first time it meets it, and the
[source generator](generator.md) writes that description at build time instead. A contract can also be
written by hand: a class deriving from `TypeContract<T>` that states the type's layout and members and
reads and writes them in plain code, added to a `BinarySerializerContext` of your own. Everything around
the members stays with the serializer — nulls, object references, union tags, field keys and lengths,
depth, limits — and every call the contract makes is checked.

Write one by hand for a type the generator leaves to reflection — a type declared in another assembly,
or one with a member that implements an interface explicitly ([VPR019](generator.md#vpr019)) — or when
the context has to be built without the generator.

## A context with its contracts

A context adds three things from its constructor, for every member-encoded type it describes: the type's
contract with `Add(TypeContract<T>)`, the type itself as supported with `Add(SupportedTypes.Object<T>())`,
and, for each type you pass to the serializer, its type information with `TypeInfo<T>()`:

```csharp
using ViShap.Viper;
using ViShap.Viper.Contracts;

var context = new GeometryContracts();
var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithContracts(context)
    .RequireGeneratedContracts()
    .Build());

var segment = new Segment { From = new Point { X = 0, Y = 0 }, To = new Point { X = 3, Y = 4 }, Label = "diagonal" };
Segment? copy = serializer.Deserialize(serializer.Serialize(segment, context.Segment), context.Segment);
Console.WriteLine($"{copy!.Label}: ({copy.To.X}, {copy.To.Y})");   // diagonal: (3, 4)

public struct Point
{
    public int X;
    public int Y;
}

public sealed class Segment
{
    public Point From { get; set; }
    public Point To { get; set; }
    public string Label { get; set; } = "";
}

public sealed class GeometryContracts : BinarySerializerContext
{
    public GeometryContracts()
    {
        Add(new PointContract());
        Add(SupportedTypes.Object<Point>());
        Add(new SegmentContract());
        Add(SupportedTypes.Object<Segment>());
        Segment = TypeInfo<Segment>();
    }

    public BinaryTypeInfo<Segment> Segment { get; }

    private sealed class PointContract() : TypeContract<Point>(
        MemberLayout.Positional,
        [new("X", typeof(int), null), new("Y", typeof(int), null)],
        canBeConstructed: true)
    {
        public override Point Create() => default;

        public override void Write(ref MemberWriter writer, in Point value)
        {
            writer.Member(value.X);
            writer.Member(value.Y);
        }

        public override void ReadPositional(ref MemberReader reader, ref Point value)
        {
            value.X = reader.Member<int>();
            value.Y = reader.Member<int>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Point value) =>
            throw new NotSupportedException();
    }

    private sealed class SegmentContract() : TypeContract<Segment>(
        MemberLayout.Positional,
        [new("From", typeof(Point), null), new("Label", typeof(string), null), new("To", typeof(Point), null)],
        canBeConstructed: true)
    {
        public override Segment Create() => new();

        public override void Write(ref MemberWriter writer, in Segment value)
        {
            writer.Member(value.From);
            writer.Member(value.Label);
            writer.Member(value.To);
        }

        public override void ReadPositional(ref MemberReader reader, ref Segment value)
        {
            value.From = reader.Member<Point>();
            value.Label = reader.Member<string>();
            value.To = reader.Member<Point>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Segment value) =>
            throw new NotSupportedException();
    }
}
```

`Segment`'s members are listed as reflection orders them — by ordinal name, since none carries
`[BinaryOrder]` — so the context writes the bytes a serializer without it writes. A struct is created as
`default` and read in place: `ReadPositional` and `ReadKeyed` receive it by reference.

`RequireGeneratedContracts()` applies to a context written by hand as it does to a generated one: a
member-encoded type the context holds no contract for is a `BinaryConfigurationException` naming it,
instead of a description by reflection.

## A contract created on first use

`Add(new PointContract())` creates the contract with the context. A context that lists many types and a
process that uses a few of them can instead add each contract as the function that creates it: the
serializer calls the function the first time an operation looks the type up, keeps the contract it
returns, and never calls it again. The source generator adds its contracts this way.

```csharp
using ViShap.Viper;
using ViShap.Viper.Contracts;

public sealed class Reading
{
    public int Value { get; set; }
}

public sealed class SensorContracts : BinarySerializerContext
{
    public SensorContracts()
    {
        Add(static () => new ReadingContract());
        Add(SupportedTypes.Object<Reading>());
    }

    private sealed class ReadingContract() : TypeContract<Reading>(
        MemberLayout.Positional,
        [new("Value", typeof(int), null)],
        canBeConstructed: true)
    {
        public override Reading Create() => new();

        public override void Write(ref MemberWriter writer, in Reading value) => writer.Member(value.Value);

        public override void ReadPositional(ref MemberReader reader, ref Reading value) =>
            value.Value = reader.Member<int>();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Reading value) =>
            throw new NotSupportedException();
    }
}
```

An exception the function throws — a `BinaryConfigurationException` from a description the contract's
constructor refuses, for one — is raised by the operation that first uses the type, not by the context's
constructor, and the next use calls the function again. Threads that use the type for the first time at
once may each call it; one contract is kept.

## What a contract implements

The constructor of `TypeContract<T>` takes the description the serializer checks every call against:

| Argument | What it states |
|---|---|
| `layout` | `MemberLayout.Positional` or `MemberLayout.Keyed` |
| `members` | one `MemberDescription(name, memberType, key)` per member, in the order the contract writes them: position order under a positional layout with every key `null`, ascending key order under a keyed one with every key given |
| `canBeConstructed` | whether `Create` produces an instance to read into; `false` for a type that is written but never read, or one read through a constructor with parameters (`UseConstructor`) — reading a type that is neither is a `BinaryTypeException` |

and the type implements four methods:

| Method | What it does |
|---|---|
| `Create` | the instance a payload is read into, before any member is read: a new object, or `default` for a struct |
| `Write` | one `writer.Member(value)` per member in order under a positional layout; one `writer.Member(key, value)` per member in ascending key order under a keyed one |
| `ReadPositional` | positional layout: one `reader.Member<T>()` per member, in the same order |
| `ReadKeyed` | keyed layout: called once for each field of the payload, with its key; a known key reads its member with one `reader.Member<T>()` and returns `true`, an unknown one returns `false` and the field is skipped |

The serializer checks every call against the description — the member, its exact type, its key, how
many calls — and a call that disagrees is a `BinaryTypeException` naming the type and the member, never
a damaged payload. The member type is the exact type the member is written and read as: a member
declared `List<Line>` is described as `typeof(List<Line>)` and read with `reader.Member<List<Line>>()`.

## An accessor table

A contract can also hand the serializer its members as a table of typed accessors: one getter and one
setter per member, in the order of the description. The serializer then reads and assigns the members
through the table itself, instead of calling `Write` and the read methods for each one. Build the table
with `MemberAccessors<T>`, whose `Member` takes a `MemberGetter<T, TMember>` and a
`MemberSetter<T, TMember>`, and hand it over with `UseAccessors` from the contract's constructor:

```csharp
using ViShap.Viper;
using ViShap.Viper.Contracts;

var context = new PriceContracts();
var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

var price = new Price { Amount = 12.50m, Currency = "EUR" };
Price? copy = serializer.Deserialize(serializer.Serialize(price, context.Price), context.Price);
Console.WriteLine($"{copy!.Amount} {copy.Currency}");   // 12.50 EUR

public sealed class Price
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
}

public sealed class PriceContracts : BinarySerializerContext
{
    public PriceContracts()
    {
        Add(new PriceContract());
        Add(SupportedTypes.Object<Price>());
        Price = TypeInfo<Price>();
    }

    public BinaryTypeInfo<Price> Price { get; }

    private sealed class PriceContract : TypeContract<Price>
    {
        public PriceContract()
            : base(
                MemberLayout.Positional,
                [new("Amount", typeof(decimal), null), new("Currency", typeof(string), null)],
                canBeConstructed: true)
        {
            UseAccessors(new MemberAccessors<Price>()
                .Member(static (in Price o) => o.Amount, static (ref Price o, decimal v) => o.Amount = v)
                .Member(static (in Price o) => o.Currency, static (ref Price o, string v) => o.Currency = v));
        }

        public override Price Create() => new();

        public override void Write(ref MemberWriter writer, in Price value)
        {
            writer.Member(value.Amount);
            writer.Member(value.Currency);
        }

        public override void ReadPositional(ref MemberReader reader, ref Price value)
        {
            value.Amount = reader.Member<decimal>();
            value.Currency = reader.Member<string>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Price value) =>
            throw new NotSupportedException();
    }
}
```

The table is checked against the description when it is handed over: an entry for each member, each
for the member's exact type, handed over once and before the contract is added to a context — anything
else is a `BinaryConfigurationException`. A member is written and read through its entry exactly as
`writer.Member` and `reader.Member` write and read it, under a positional layout and a keyed one alike.
The contract keeps implementing `Write` and the read methods, and they must agree with the table: the
serializer still calls them when a read is traced for diagnostics ([`BinaryFormatDumper`](diagnostics.md)).
The source generator writes a table for every contract it emits.

## A constructor with parameters

A type whose members a constructor takes is read through `UseConstructor`, called from the contract's
constructor before `UseAccessors`. The serializer reads the members into a value of your own —
`TValues`, one place per member — through a second table, then calls a `MemberConstructor<TValues, T>`
that makes the instance from them. `start` creates the values before anything is read, so the value it
gives a member is the one that member keeps when a keyed payload leaves it out. The table over `T` then
needs only getters: `Member` with a single `MemberGetter<T, TMember>` adds a member that is written and
never assigned.

```csharp
using ViShap.Viper;
using ViShap.Viper.Contracts;

var context = new RateContracts();
var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

Rate? copy = serializer.Deserialize(serializer.Serialize(new Rate("EUR", 1.08m), context.Rate), context.Rate);
Console.WriteLine($"{copy!.Currency} {copy.Value}");   // EUR 1.08

public sealed class Rate(string currency, decimal value)
{
    public string Currency { get; } = currency;
    public decimal Value { get; } = value;
}

public sealed class RateContracts : BinarySerializerContext
{
    public RateContracts()
    {
        Add(new RateContract());
        Add(SupportedTypes.Object<Rate>());
        Rate = TypeInfo<Rate>();
    }

    public BinaryTypeInfo<Rate> Rate { get; }

    private struct Values
    {
        public string Currency;
        public decimal Value;
    }

    private sealed class RateContract : TypeContract<Rate>
    {
        public RateContract()
            : base(
                MemberLayout.Positional,
                [new("Currency", typeof(string), null), new("Value", typeof(decimal), null)],
                canBeConstructed: false)
        {
            UseConstructor(
                static () => new Values { Currency = "" },
                new MemberAccessors<Values>()
                    .Member(static (in Values v) => v.Currency, static (ref Values v, string m) => v.Currency = m)
                    .Member(static (in Values v) => v.Value, static (ref Values v, decimal m) => v.Value = m),
                static (ref Values v) => new Rate(v.Currency, v.Value));

            UseAccessors(new MemberAccessors<Rate>()
                .Member(static (in Rate o) => o.Currency)
                .Member(static (in Rate o) => o.Value));
        }

        public override Rate Create() => throw new NotSupportedException();

        public override void Write(ref MemberWriter writer, in Rate value)
        {
            writer.Member(value.Currency);
            writer.Member(value.Value);
        }

        public override void ReadPositional(ref MemberReader reader, ref Rate value) =>
            throw new NotSupportedException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Rate value) =>
            throw new NotSupportedException();
    }
}
```

The values table is held to the description as the accessor table is, and each of its entries must
assign; `UseConstructor` is handed over once. An accessor table with an entry that has no setter is a
`BinaryConfigurationException` unless the contract reads through a constructor. The serializer never
calls `Create`, `ReadPositional` or `ReadKeyed` of such a contract, refuses a cycle through its type and
refuses to populate it, and a `MemberConstructor` that returns `null` is a `BinaryTypeException`. The
source generator writes all of this for a type the serializer reads through a
[constructor](contracts.md#construction), with a struct of its own as the values.

## A keyed contract

Under a keyed layout `Write` names each member's key, and `ReadKeyed` answers the key of each field the
payload holds. Returning `false` for a key the contract does not know is what lets a newer writer add a
field an older reader skips:

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

This context adds a contract and nothing else, so the serializer still builds the encoding of
`Customer` itself — by reflection, which the generic methods without type information allow. Adding
`SupportedTypes.Object<Customer>()` and passing `TypeInfo<Customer>()` to the serializer removes that,
as in the first example.

## Members that are not public

A contract reaches a member the way any code outside the type does. A non-public member — one marked
`[BinaryInclude]`, a property with an `init` or a private setter — is reached through
`[UnsafeAccessor]`:

```csharp
using System.Runtime.CompilerServices;
using ViShap.Viper;
using ViShap.Viper.Contracts;

var context = new TicketContracts();
var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

Ticket? copy = serializer.Deserialize(serializer.Serialize(Ticket.Open("Login fails", 2), context.Ticket), context.Ticket);
Console.WriteLine($"{copy!.Title} (priority {copy.Priority})");   // Login fails (priority 2)

public sealed class Ticket
{
    public string Title { get; set; } = "";

    [BinaryInclude] private int _priority;

    public int Priority => _priority;

    public static Ticket Open(string title, int priority) => new() { Title = title, _priority = priority };
}

public sealed class TicketContracts : BinarySerializerContext
{
    public TicketContracts()
    {
        Add(new TicketContract());
        Add(SupportedTypes.Object<Ticket>());
        Ticket = TypeInfo<Ticket>();
    }

    public BinaryTypeInfo<Ticket> Ticket { get; }

    private sealed class TicketContract() : TypeContract<Ticket>(
        MemberLayout.Positional,
        [new("Title", typeof(string), null), new("_priority", typeof(int), null)],
        canBeConstructed: true)
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_priority")]
        private static extern ref int Priority(Ticket ticket);

        public override Ticket Create() => new();

        public override void Write(ref MemberWriter writer, in Ticket value)
        {
            writer.Member(value.Title);
            writer.Member(Priority(value));
        }

        public override void ReadPositional(ref MemberReader reader, ref Ticket value)
        {
            value.Title = reader.Member<string>();
            Priority(value) = reader.Member<int>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Ticket value) =>
            throw new NotSupportedException();
    }
}
```

## The types a contract reaches

A contract covers the members of one type. Every other type the context's types reach is made
supported with the method of `SupportedTypes` named after its entry in the
[table of supported types](supported-types.md) — `SupportedTypes.List<Line>()`,
`SupportedTypes.Dictionary<string, int>()`, `SupportedTypes.Enum<Status>()`,
`SupportedTypes.Nullable<Point>()` — and a member-encoded type with `SupportedTypes.Object<T>()` beside its
contract. The scalars need nothing.

Each of those methods returns a `SupportedType`: an opaque value that names the type it makes supported in
its `Type` property and carries how the serializer encodes it. Only `SupportedTypes` creates one, and a
context adds it from its constructor with `Add(SupportedType)` — once per type, since adding the same type
twice, or adding after options were built with the context, is a `BinaryConfigurationException`. The
encoding of a type is the same whichever context supplies it, so the first context of the process that adds
a type supplies it for every serializer, and a type added by two contexts is encoded alike by both.

A `[BinaryUnion]` base is made supported with its arms, which are supported and given contracts of their
own; the base needs no contract, because its values are always one of its arms:

```csharp
using ViShap.Viper;
using ViShap.Viper.Contracts;

var context = new ShapeContracts();
var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithContracts(context)
    .RequireGeneratedContracts()
    .Build());

Shape[] shapes = [new Circle { Label = "wheel", Radius = 1.5 }];
Shape[]? copy = serializer.Deserialize(serializer.Serialize(shapes, context.Shapes), context.Shapes);
Console.WriteLine(copy![0] is Circle { Radius: 1.5 });   // True

[BinaryUnion(1, typeof(Circle))]
public abstract class Shape
{
    public string Label { get; set; } = "";
}

public sealed class Circle : Shape
{
    public double Radius { get; set; }
}

public sealed class ShapeContracts : BinarySerializerContext
{
    public ShapeContracts()
    {
        Add(SupportedTypes.Array<Shape>());
        Add(SupportedTypes.Union<Shape>((1, typeof(Circle))));
        Add(new CircleContract());
        Add(SupportedTypes.Object<Circle>());
        Shapes = TypeInfo<Shape[]>();
    }

    public BinaryTypeInfo<Shape[]> Shapes { get; }

    private sealed class CircleContract() : TypeContract<Circle>(
        MemberLayout.Positional,
        [new("Label", typeof(string), null), new("Radius", typeof(double), null)],
        canBeConstructed: true)
    {
        public override Circle Create() => new();

        public override void Write(ref MemberWriter writer, in Circle value)
        {
            writer.Member(value.Label);
            writer.Member(value.Radius);
        }

        public override void ReadPositional(ref MemberReader reader, ref Circle value)
        {
            value.Label = reader.Member<string>();
            value.Radius = reader.Member<double>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Circle value) =>
            throw new NotSupportedException();
    }
}
```

`SupportedTypes.Union` takes exactly the arms the type's `[BinaryUnion]` attributes declare, in any order:
other tags, a missing or an extra arm, and a type without the attributes are a
`BinaryConfigurationException`, so the union map a context supplies is always the one the attributes give.
`SupportedTypes.Collection<TCollection, T>()` takes nothing but the two types: the collection is created
with its public parameterless constructor and filled through its public `Add(T)`, as without a context.

A context that adds a contract and `SupportedTypes.Object<T>()` for every member-encoded type it reaches,
and makes every other reached type supported, is what an application published with native AOT needs;
see [Trimming and native AOT](aot.md).

## Checking a contract against reflection

A contract that describes its type as reflection does — the same layout, the same members in the same
order, the same types and keys — writes exactly the bytes reflection writes, so payloads move freely
between serializers with and without the context. Comparing the two is a test worth keeping beside a
contract written by hand:

```csharp
using ViShap.Viper;
using ViShap.Viper.Contracts;

var context = new NoteContracts();
var withContext = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());
var byReflection = new BinarySerializer();

var note = new Note { Text = "check the keys", Pinned = true };
bool same = withContext.Serialize(note, context.Note).AsSpan().SequenceEqual(byReflection.Serialize(note));
Console.WriteLine(same);   // True

public sealed class Note
{
    public bool Pinned { get; set; }
    public string Text { get; set; } = "";
}

public sealed class NoteContracts : BinarySerializerContext
{
    public NoteContracts()
    {
        Add(new NoteContract());
        Add(SupportedTypes.Object<Note>());
        Note = TypeInfo<Note>();
    }

    public BinaryTypeInfo<Note> Note { get; }

    private sealed class NoteContract() : TypeContract<Note>(
        MemberLayout.Positional,
        [new("Pinned", typeof(bool), null), new("Text", typeof(string), null)],
        canBeConstructed: true)
    {
        public override Note Create() => new();

        public override void Write(ref MemberWriter writer, in Note value)
        {
            writer.Member(value.Pinned);
            writer.Member(value.Text);
        }

        public override void ReadPositional(ref MemberReader reader, ref Note value)
        {
            value.Pinned = reader.Member<bool>();
            value.Text = reader.Member<string>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Note value) =>
            throw new NotSupportedException();
    }
}
```

A contract that describes its type otherwise — other members, another order, another layout — is
accepted: it decides how its type is written. Its payloads are then read only by serializers given the
same contract.

## Rules

- Use the `MemberWriter` and `MemberReader` through the reference you are handed. Passing one to a
  helper by value, keeping a copy, or creating one with `default` is refused with `BinaryTypeException`
  as soon as either copy is used after the other; pass it on by `ref`. The operation then leaves nothing
  in its destination.
- A description that cannot be checked — an undefined layout, a null member, a key under a positional
  layout, a keyed member without a key, keys out of order or repeated, a negative key, an empty name — is
  a `BinaryConfigurationException` from the constructor.
- A context holds one contract per type, added as an instance or as the function that creates it, and adds
  each supported type once; a second one, and anything added after options were built with the context,
  is a `BinaryConfigurationException`. A function that returns `null` is one too, raised where the type is
  first used. `Add(null)` names neither overload; cast the `null` to say which one you mean. `TypeInfo<T>()`
  for a type the context did not make supported, other than a scalar, is one too.
- A context, and every contract in it, is shared by every operation and thread that uses the options:
  keep no per-call state in either.
- A contract held for a type the serializer encodes by a dedicated encoding — a collection, a string, a
  scalar — is never consulted.
- An exception your contract throws — from `Create`, `Write`, `ReadPositional` or `ReadKeyed` — is yours
  and reaches the caller unchanged; a write it interrupts leaves nothing in its destination. A class
  contract whose `Create` returns `null` is a `BinaryTypeException`.
- The encoding a supported type receives is the one the serializer would build for it by reflection,
  whichever context adds it, so the first context of the process that adds a type supplies it for every
  serializer, and nothing a context adds changes how another configuration writes or reads.
