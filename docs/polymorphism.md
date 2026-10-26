# Polymorphism

A value whose runtime type differs from its declared type can be written only when the declared type
lists the runtime types it may hold. `[BinaryUnion(tag, typeof(Derived))]` on a base class or an
interface declares one such type and the one-byte tag that stands for it on the wire.

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

Animal[] zoo =
[
    new Dog { Name = "Rex", GoodBoy = true },
    new Cat { Name = "Tom", Lives = 9 }
];

Animal[]? copy = serializer.Deserialize<Animal[]>(serializer.Serialize(zoo));

foreach (Animal animal in copy!)
    Console.WriteLine(animal.GetType().Name + " " + animal.Name);

[BinaryUnion(1, typeof(Dog))]
[BinaryUnion(2, typeof(Cat))]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Dog : Animal
{
    public bool GoodBoy { get; set; }
}

public sealed class Cat : Animal
{
    public int Lives { get; set; }
}
```

A tag is written in front of the members of the value's own layout, and the reader uses it to choose
the type to create. The declaration can sit on an interface as well:

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();

IShape[] shapes = [new Circle { Radius = 2 }, new Square { Side = 3 }];
IShape[]? copy = serializer.Deserialize<IShape[]>(serializer.Serialize(shapes));

Console.WriteLine(copy![0] is Circle);
Console.WriteLine(copy[1] is Square);

[BinaryUnion(1, typeof(Circle))]
[BinaryUnion(2, typeof(Square))]
public interface IShape;

public sealed class Circle : IShape
{
    public double Radius { get; set; }
}

public sealed class Square : IShape
{
    public double Side { get; set; }
}
```

## The declared type decides

The union belongs to the *declared* type of the slot, so write and read with the same one. A `Dog`
written as `Serialize(dog)` has the declared type `Dog`, which carries no tag, and cannot be read back
as an `Animal`. Name the declared type when you write:

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer();
Animal dog = new Dog { Name = "Rex" };

byte[] bytes = serializer.Serialize<Animal>(dog);
Animal? copy = serializer.Deserialize<Animal>(bytes);

Console.WriteLine(copy is Dog);

[BinaryUnion(1, typeof(Dog))]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Dog : Animal
{
    public bool GoodBoy { get; set; }
}
```

Inside a graph the declared type is the type of the member, element or dictionary value, so an `Animal`
property, a `List<Animal>` or a `Dictionary<string, Animal>` needs no further care.

## Only tags travel

Type names are never written, so a payload cannot name a type to construct. The set of types a reader
can create from a payload is closed, and the set is what the declarations list. There is no
"deserialize anything assignable" mode.

## Rules

- A tag fits a byte, 0 to 255, and is unique within its declared type.
- Each derived type is named once, and must be assignable to the type it is declared on.
- A tag must never be reused for a different type once payloads exist.
- The attribute is applied to a class or an interface. It is not inherited: a derived type that is itself
  the declared type of a slot needs its own declarations.
- A derived type may be a [keyed contract](contracts.md#keyed-layout) or a positional type. Its members
  are those of its own layout, inherited members included.
- With [`PreserveReferences`](references.md), the reference frame comes first and the tag after it.

## Failures

| Situation | Exception |
|---|---|
| a value's runtime type differs from the declared type and the declared type has no matching declaration — including any value written through `object` | `BinaryTypeException`, **when writing** |
| a payload carries a tag the declared type does not list | `BinaryTypeException`, when reading |
| the declarations are invalid: duplicate tags, a tag outside 0–255, a derived type that is not assignable | `BinaryTypeException`, the first time the type is used |
| a type with no parameterless constructor and no declaration naming the type to build | `BinaryTypeException`, when reading |

Writing fails rather than falling back to the declared type's layout, because a reader could not tell
that a derived layout had been reduced to its base. Without a declaration a reader always builds the
declared type, so nothing that could be read is lost by the check.

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;

var serializer = new BinarySerializer();

try
{
    serializer.Serialize<Animal>(new Bird { Name = "Tweety" });
}
catch (BinaryTypeException)
{
    Console.WriteLine("Bird is not declared on Animal");
}

[BinaryUnion(1, typeof(Dog))]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Dog : Animal;

public sealed class Bird : Animal;
```

## Populating

[`Populate`](entry-points.md#populating-an-existing-object) reads into an existing instance. When the
payload's runtime type is not the target's, an instance cannot be reused as another type and the call
is a `BinaryTypeException`.
