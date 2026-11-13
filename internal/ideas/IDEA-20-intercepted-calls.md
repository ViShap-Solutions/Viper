# IDEA-20 — Type information supplied at the call site by interceptors

**Class: plan.** **Status:** idea.

## What and why

Native AOT, trimming and file-based apps need every serialized type listed in a `[BinaryContext]`
class and the call to pass its `BinaryTypeInfo<T>`. The first example of every README is
`new BinarySerializer().Serialize(order)`, which a file-based app refuses at run time unless it adds a
directive or a context (`v1.0.1`). A generator that sees the call can supply the type information itself:
it lists the call's `T` in a context it writes and redirects the call to the overload that takes the type
information, with C# interceptors (`[InterceptsLocation]`). The example then compiles and runs under
native AOT as written.

## Where it was written

- An outside assessment of `v1.0.0`, reviewed by the owner on 2026-11-13: "the source generator could
  pick up the types passed to `serializer.Serialize<T>(...)` through `InterceptsLocation` or call
  analysis".

## What is already fixed

- The 26 generic methods without type information and their 26 counterparts with a `BinaryTypeInfo<T>`
  exist in pairs, so every call has a target to be redirected to.
- A generated contract writes the same bytes as the reflected one (contract §14.4, `ByteIdentityTests`),
  so a redirected call changes no byte on the wire.
- The .NET SDK ships generators that intercept as an opt-in: the configuration binder
  (`EnableConfigurationBindingGenerator`) and the request delegate generator of ASP.NET Core minimal APIs.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | Nothing; a context stays explicit, and `IDEA-19` c reports the call that cannot be served | One mechanism; what a call uses is visible in the source | One context class and one argument per call under native AOT |
| b | Opt-in property `ViperInterceptCalls`: the generator writes an internal context listing every closed `T` of a call without type information, and intercepts each such call to its counterpart | The getting-started example works under native AOT and in a file-based app with one property | Code that runs is not the code written; a `T` open in a generic method is not intercepted and still needs reflection; calls inside a referenced library compiled without the property are untouched |
| c | b, on by default when `ViperReflectionEnabled` is false | Nothing to configure where reflection is off | The same call takes another path in one project than in the next; a default that changes what a build produces |

```csharp
// Written by the consumer, in a file-based app:
var bytes = new BinarySerializer().Serialize(order);

// What the compiler calls under option b:
var bytes = new BinarySerializer().Serialize(order, ViperImplicitContext.Default.Order);
```

Recommended when taken up: **a** with `IDEA-19` c first — the diagnostic names the fix in one line and
leaves the source honest — and **b** only if consumers still ask for it after that diagnostic exists.

## Open questions

1. Which context wins when the serializer's options already carry one through `WithContracts`: the
   implicit context of the call site, or the options'? A redirected call must not use another contract
   than the one the consumer configured.
2. Is the implicit context public (a consumer can pass it elsewhere) or internal and unnamed?
3. The status of interceptors in the compiler of the SDK Viper targets, and the property a consumer sets
   for them (`InterceptorsNamespaces`).
4. Does an intercepted call stay intercepted when the consumer later adds an explicit context listing the
   same type?

## Version impact

| Option | Surface | Version |
|---|---|---|
| a | none beyond `IDEA-19` | — |
| b | the generator's output under a new opt-in property; no API, no wire | **minor** |
| c | which code a default build runs | **minor**, by the same argument as b, since the bytes are identical — the owner decides whether a change of default is treated as a major |

## Preconditions

- `IDEA-19` c, so the case without interception has a diagnostic first.
- Question 1 decided: interception must never replace a context the options supply.
