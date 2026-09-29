# Trimming and native AOT

Viper builds the encoding of a type by reflection, the first time the type is used. It finds the type's
members, constructors and generic definitions at run time, and closes its generic encoders over the
types it meets. Trimming can remove exactly the members and constructors reflection looks for, and
native AOT cannot guarantee that a generic instantiation over your type was compiled.

Instead of failing at run time on a member or an instantiation that was not kept, Viper states the
requirement at the call, where the decision is yours.

## Which members carry it

Every public entry point that encodes or decodes a value of your type is marked with both
`[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`:

- the twenty-two generic methods of `BinarySerializer` — every `Serialize<T>`, `SerializePooled<T>`,
  `SerializeAsync<T>`, `Deserialize<T>`, `DeserializeAsync<T>`, `DeserializeAsyncEnumerable<T>`,
  `Populate<T>` and `PopulateAsync<T>` overload;
- the four generic methods of `BinaryFormatDumper`: both `Dump<T>` overloads, `DumpValue<T>` and
  `Compare<T>`.

No other public member carries either attribute. The options and their builder, the algorithms, the
keys, `PooledPayload`, `BinaryFormatInspector`, `BinaryFormatDumper.DumpHeader` and the untyped
`BinaryFormatDumper.Dump` never build an encoding for your type, so they raise no warning.

Both packages are built with `IsAotCompatible`, so the analysis reports nothing inside them. What a
project that enables the trimming or AOT analyzers sees is one `IL2026` and one `IL3050` at each call to
one of the members above, and nothing else from Viper.

## What to do about the warnings

The warnings appear only when the analyzers run — when the project sets `PublishTrimmed`,
`PublishAot`, `IsTrimmable`, `IsAotCompatible`, `EnableTrimAnalyzer` or `EnableAotAnalyzer`. An
application that does none of these needs to do nothing.

An application that does has two honest choices for each call:

- **Pass the requirement on.** Mark the method that makes the call with the same attributes, and its
  callers see the same warning. A library that serializes on its caller's behalf usually does this.
- **Take responsibility for it.** Suppress the warning at the call, with a justification, once you have
  made sure that the serialized types are kept: their members and constructors, and every type they
  reach. Trimming keeps a type when it is referenced from code the trimmer can see, when it is rooted
  with a `[DynamicDependency]` attribute or a trimmer root descriptor, or when it is preserved with
  `[DynamicallyAccessedMembers]`. Under native AOT, generic instantiations over your types have to be
  present in the compiled program.

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

There is no source generator in v1.0: the encoding of every type is built by reflection, and the
requirement above is how that is made visible rather than hidden.
