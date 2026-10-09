# ViShap.Viper.Generator

The build-time source generator of [Viper](https://github.com/ViShap-Solutions/Viper). It writes the
type contracts of a `BinarySerializerContext`, so the serializer reads and writes your types through
plain generated code instead of describing them by reflection on first use, and it reports every
contract mistake — a duplicate key, an unmarked member of a keyed contract, a delegate member — as a
compiler diagnostic instead of an exception at run time.

It runs only in the compiler. Nothing of it ships with your application, and a project with no
`[BinaryContext]` class gets nothing from it.

## Use

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithContracts(AppContracts.Default)
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

The context gets a contract for every type it lists and every member-encoded type those reach —
`Line` above, through the list. Each contract describes its type exactly as the serializer does by
reflection, so the bytes are the same with the context and without it, in both directions.
`RequireGeneratedContracts()` on the options turns a type the context lacks into an error instead of
a silent fallback to reflection.

The serializer's entry points keep their trimming and native AOT annotations: a generated contract
replaces the reflection over a type's members, not the construction of the codecs around them.

## Diagnostics

| Id | Severity | Meaning |
|---|---|---|
| VPR001 | Error | a `[BinaryContext]` class is not `partial` |
| VPR002 | Error | a `[BinaryContext]` class is not a non-generic, non-abstract class deriving from `BinarySerializerContext` |
| VPR003 | Error | a `[BinaryContext]` class declares a parameterless constructor or a `Default` member |
| VPR004 | Error | `[BinaryKey]` on a type that is not `[BinaryContract]` |
| VPR005 | Error | `[BinaryInclude]` together with `[BinaryIgnore]` |
| VPR006 | Error | two members share a `[BinaryOrder]` value |
| VPR007 | Error | `[BinaryInclude]` on a member of a contract type |
| VPR008 | Error | `[BinaryOrder]` on a member of a contract type |
| VPR009 | Error | `[BinaryKey]` together with `[BinaryIgnore]` |
| VPR010 | Error | a member of a contract type has neither `[BinaryKey]` nor `[BinaryIgnore]` |
| VPR011 | Error | a negative `[BinaryKey]` |
| VPR012 | Error | two members share a `[BinaryKey]` value |
| VPR013 | Error | a delegate member |
| VPR014 | Error | a member type with no representation on the wire (a pointer, a ref struct) |
| VPR015 | Error | a `[BinaryUnion]` tag outside 0–255 |
| VPR016 | Error | a `[BinaryUnion]` tag declared twice |
| VPR017 | Error | a `[BinaryUnion]` type not assignable to the base |
| VPR018 | Warning | an abstract type or interface without `[BinaryUnion]`, whose values cannot be read back |
| VPR019 | Warning | a type that gets no generated contract and is described by reflection |

Each is explained, with its fix, in [the generator documentation](https://github.com/ViShap-Solutions/Viper/blob/main/docs/generator.md).
