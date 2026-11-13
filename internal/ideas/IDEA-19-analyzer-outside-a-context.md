# IDEA-19 — Diagnostics for types and calls a context does not reach

**Class: plan.** **Status:** idea.

## What and why

The generator already reports every contract mistake at compile time — VPR004…VPR018: `[BinaryKey]`
without `[BinaryContract]`, a duplicate key or order, a contract member with neither `[BinaryKey]` nor
`[BinaryIgnore]`, a union tag outside a byte or twice, an abstract type without a union map, a delegate
member — but only for the types a `[BinaryContext]` class reaches. Two cases stay without a diagnostic:

1. **A type serialized through the reflection path.** The first example of `docs/getting-started.md`
   uses no context. A duplicate `[BinaryKey]` on such a type is found when the program first serializes
   it, as `BinaryConfigurationException`.
2. **A call the application cannot serve.** With `ViperReflectionEnabled` false — under `PublishAot`,
   `PublishTrimmed`, and by default in a file-based app (`dotnet run app.cs`) — `Serialize<T>(value)`
   for a `T` no context lists fails at run time with `BinaryConfigurationException`. The trimming
   analyser does warn at that call (IL2026/IL3050, from the method's `[RequiresUnreferencedCode]`), but
   the warning names trimming, not the fix: list the type in a context, pass its `BinaryTypeInfo<T>`, or
   turn the path on. `v1.0.1` documented the case for file-based apps; a diagnostic would state it at
   the line that fails.

## Where it was written

- An outside assessment of `v1.0.0`, reviewed by the owner on 2026-11-13, which asked for contract
  errors "underlined in red" before the program runs. The review found that the generator already does
  this for every type a context reaches; this idea is the remainder.

## What is already fixed

- The rules are one set: `ReflectedContract` and the generator's `Planner` apply the same rejections
  (contract §14.4), and VPR004…VPR018 are their compile-time form.
- The generator ships in `analyzers/dotnet/cs` of its package and flows to the consumer through the
  meta-package; an analyser in the same assembly reaches the same consumers.
- VPR021 already reports a type the context reaches but cannot serve while `ViperReflectionEnabled` is
  false, and the generator already sees that property.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | Nothing; the documents state both cases | No code | The mistake is found at run time |
| b | A `DiagnosticAnalyzer` in the generator assembly applying the type rules to every type that carries a Viper attribute, whether a context reaches it or not, under the same ids; a type a context reaches is reported once | Case 1 at compile time, with the diagnostics a context already gives | The planner's checks factored so the analyser and the generator share them; one analyser to keep equal to the rules |
| c | b, and a call-site diagnostic: an invocation of a generic `BinarySerializer` or `BinaryFormatDumper` method without type information, whose `T` is closed, no context of the compilation lists, and `ViperReflectionEnabled` is false | Case 2 at the line that fails, naming the three ways out | A new id; `T` open in a generic method is not decidable and is not reported |
| d | c with code fixes: add the missing `[BinaryKey(n)]` with the next free key, mark the context `partial`, add `[BinaryContext(typeof(T))]` to the compilation's only context, pass `Context.Default.T` at the call | One click from the diagnostic to the fix | A `CodeFixProvider` lives in a workspace-dependent assembly of its own (`Microsoft.CodeAnalysis.Workspaces`), a second analyser assembly in the package |

```text
warning VPR022: 'Order' is serialized here without type information, and the application turns the
reflection path off (ViperReflectionEnabled is false, as it is in a file-based app or when published
with native AOT). List 'Order' in a [BinaryContext] class and pass Context.Default.Order, or set
ViperReflectionEnabled to true.
```

Recommended when taken up: **c** — b alone leaves the case that `v1.0.1` had to document — and d for
the call-site fix once c has shown which fix consumers take.

## Open questions

1. Severity: a warning, or an error for the type rules, which fail at run time without exception? An
   error breaks the build of a type that is never serialized.
2. Does b report a rule on a type that carries no Viper attribute at all but is reached only at run time
   through the reflection path? Without an attribute nothing marks it as serialized, so b cannot see it.
3. Is VPR021 superseded by c for types a context reaches, or do both stay?

## Version impact

| Option | Surface | Version |
|---|---|---|
| b, c | behaviour of the build: new warnings, no API, no wire | **minor** — a consumer building with warnings as errors meets them on upgrade |
| d | a new analyser assembly in the generator's package | **minor** |

## Preconditions

- The generator's tests extended with analyser tests of the same shape (`GEN-*`), and `ByteIdentityTests`
  unchanged: the analyser emits nothing.
