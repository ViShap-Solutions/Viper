# Trimming and native AOT

Viper can encode a type in two ways. A [context](generator.md) describes the type at build time: its
contracts, and the encoding of every type it reaches, are plain generated code. Without one, Viper
describes the type by reflection the first time it meets it — finding its members, constructors and
generic definitions at run time and closing its generic encoders over the types it meets. Trimming can
remove exactly what reflection looks for, and native AOT cannot guarantee that a generic instantiation
built at run time was compiled.

An application published trimmed or with native AOT therefore goes through a context, and Viper turns
the reflection path off for it.

## Through a context

List the root types on a context and pass the **type information** of a listed type to the serializer.
The generated context exposes one property of type information per type it lists:

```csharp
using ViShap.Viper;

var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithContracts(AppContracts.Default)
    .Build());

byte[] bytes = serializer.Serialize(new Order { Id = 7, Lines = ["a", "b"] }, AppContracts.Default.Order);
Order? copy = serializer.Deserialize(bytes, AppContracts.Default.Order);
Console.WriteLine($"{copy!.Id}: {string.Join(", ", copy.Lines)}");   // 7: a, b

public sealed class Order
{
    public int Id { get; set; }
    public List<string> Lines { get; set; } = [];
}

[BinaryContext(typeof(Order))]
public partial class AppContracts : BinarySerializerContext;
```

Every method of `BinarySerializer` and of `BinaryFormatDumper` that encodes or decodes your type has a
form that takes type information — `Serialize`, `SerializePooled`, `SerializeAsync`, `Deserialize`,
`DeserializeAsync`, `DeserializeAsyncEnumerable`, `Populate`, `PopulateAsync`, `Dump`, `DumpValue` and
`Compare`, over every source and destination. Those forms carry no trimming or AOT requirement: the
type information states that the context describes the type and everything it reaches, so nothing is
built by reflection. The serializer's options must have been built with the same context;
type information of another context is `BinaryConfigurationException` before anything is written.

A type the generated context reaches but does not list has no property; `TryGetTypeInfo<T>()` returns
its type information, or `null` for a type the context does not describe.

## The reflection path

Whether a type no context describes may still be described by reflection is one runtime switch,
`ViShap.Viper.ReflectionPath.IsEnabled`, set from the MSBuild property `ViperReflectionEnabled`:

| Application | `ViperReflectionEnabled` by default |
|---|---|
| `PublishAot` or `PublishTrimmed` set — in its published build and in its JIT runs alike | `false` |
| a file-based app (`dotnet run app.cs`), which sets `PublishAot` unless it says otherwise | `false` |
| any other | `true` |

With the switch off, a type no context offered — a member type, a collection element, a union arm, the
runtime type of a polymorphic value — is `BinaryConfigurationException` naming it, raised where the type
is first used and before the destination is touched, never reflection. The source generator reports
such a type at build time as [VPR021](generator.md#vpr021). The trimming and AOT tools understand the
switch: with it off, the reflection path is removed from the published application, and the packages
raise no trimming or AOT warning of their own.

Set the property to keep the reflection path in a trimmed application whose types you keep yourself:

```xml
<PropertyGroup>
  <ViperReflectionEnabled>true</ViperReflectionEnabled>
</PropertyGroup>
```

A file-based app sets it with a directive at the top of the file:

```text
#:property ViperReflectionEnabled=true
```

## Which members carry a requirement

The methods that encode or decode your type **without** type information — the twenty-two generic
methods of `BinarySerializer` and the four of `BinaryFormatDumper` — carry `[RequiresUnreferencedCode]`
and `[RequiresDynamicCode]`, because they reach the reflection path for any type no context describes.
A project that enables the trimming or AOT analysers sees one `IL2026` and one `IL3050` at each call to
them, and nothing else from Viper. Their counterparts that take type information, the options and their
builder, the algorithms, the keys, `PooledPayload`, `BinaryFormatInspector`, `DumpHeader`, the untyped
`Dump`, the context and `SupportedTypes` carry neither.

A call without type information has two honest answers. Use the form that takes type information, which
is what an application published with native AOT does. Or, in a library that serializes on its caller's
behalf, pass the requirement on:

```csharp
using System.Diagnostics.CodeAnalysis;
using ViShap.Viper;

var codec = new OrderCodec(new BinarySerializer());
byte[] bytes = codec.Pack(new Order { Id = 7 });
Console.WriteLine(codec.Unpack(bytes)!.Id);

public sealed class Order
{
    public int Id { get; set; }
}

public sealed class OrderCodec(BinarySerializer serializer)
{
    [RequiresUnreferencedCode("Serializes Order by reflection over its members.")]
    [RequiresDynamicCode("Serializes Order through generic instantiations built at run time.")]
    public byte[] Pack(Order order) => serializer.Serialize(order);

    [RequiresUnreferencedCode("Deserializes Order by reflection over its members.")]
    [RequiresDynamicCode("Deserializes Order through generic instantiations built at run time.")]
    public Order? Unpack(byte[] bytes) => serializer.Deserialize<Order>(bytes);
}
```

## A context written by hand

A context does not have to be generated. Its constructor adds a contract per member-encoded type
(see [Writing contracts by hand](writing-contracts.md)) and makes every type it reaches supported with a
method of `SupportedTypes` named after the entry of the [table of supported types](supported-types.md)
that type is — `SupportedTypes.List<Line>()`, `SupportedTypes.Dictionary<string, int>()`,
`SupportedTypes.Enum<Status>()`, `SupportedTypes.Object<Order>()`, `SupportedTypes.Union<Shape>(…)`; the
scalars need nothing. `TypeInfo<T>()` then gives the type information of a type it made supported:

```csharp
using ViShap.Viper;
using ViShap.Viper.Contracts;

var context = new NumbersContext();
var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

byte[] bytes = serializer.Serialize(new List<int> { 1, 2, 3 }, context.Numbers);
Console.WriteLine(string.Join(", ", serializer.Deserialize(bytes, context.Numbers)!));   // 1, 2, 3

public sealed class NumbersContext : BinarySerializerContext
{
    public NumbersContext()
    {
        Add(SupportedTypes.List<int>());
        Numbers = TypeInfo<List<int>>();
    }

    public BinaryTypeInfo<List<int>> Numbers { get; }
}
```

The encoding a context supplies is the one Viper would build by reflection for the same type, so the
bytes are the same either way, and a payload written by one application reads in the other.
