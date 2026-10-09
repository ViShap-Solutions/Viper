# IDEA-04 — Native AOT through the context

**Class: plan.** **Status:** decided — `rc2/RC2-Plan.md` P4c (design) and P4d (build), option a, the
owner on 2026-10-10. Scope option S4-B of `rc2/RC2-Plan.md` §3, first set aside for S4-A (2026-10-09),
then taken into `rc.2`.

## What and why

The generator writes type contracts, so member access needs no reflection. The codecs around the members
— collections, arrays, nullables, enums, the object codec itself — are still closed at run time with
`MakeGenericType` and `Activator.CreateInstance`, and union maps are read from attributes. The entry
points therefore keep `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` with or without a context
(`docs/aot.md`). A consumer publishing with native AOT gets warnings, and a codec nothing roots may be
missing at run time.

What blocks native AOT today:

| Where | What it does |
|---|---|
| `Engine/FormatterRegistry.cs` `Create`, `ForArray`, `CodecFor` | Every codec closed with `MakeGenericType` and created with `Activator.CreateInstance` |
| `Engine/Contracts/ReflectedContract.cs` | `Expression.Compile` — unused under a generated contract, still reachable |
| `Engine/Contracts/TypeContractCache.cs` `GetUnion` | `[BinaryUnion]` read from attributes at run time |
| `Engine/Codecs/ObjectCodec.cs` `RuntimeContract` | The contract of a runtime type in a polymorphic slot, by reflection when the context does not hold it |

## Where it was written

- `rc2/RC2-Plan.md` §3 S4 — the check against the code and the three scopes; §3.9 "S4 revisited".
- `rework/Rework-Plan.md` §13.1, whose sentence "the generated path carries none" was corrected per S4-A.
- `Audit-Future.md` §5, §8-C.

## What is already fixed

- The context (`BinarySerializerContext`) is abstract with protected registration, so it can carry more
  than contracts without changing what it carries today.
- The traversal protocol (shapes, codecs) is internal and stays so (`CLAUDE.md`, "Adding a formatter";
  contract §21.3; INV-5).

## The finding that reopened it

S4 assumed native AOT would mean publishing the codec and shape layer. It does not have to. Generated code
needs no shape: it needs only to **name** each closed type, so that the engine builds its codec through
statically typed code instead of `MakeGenericType`. The names of the supported types are already frozen by
the contract (§23). A façade of names publishes nothing of the protocol:

```csharp
// written by the generator into the context's constructor
AddCodec(Codecs.List<Line>());                 // inside: new SequenceCodec<…>(new ListShape<Line>())
AddCodec(Codecs.Dictionary<string, int>());
Add(new OrderContract());

// an entry point without annotation, in the manner of JsonTypeInfo<T>
byte[] bytes = serializer.Serialize(order, AppContracts.Default.Order);   // a typed handle
```

Every reflective resolution is then guarded by a feature check the analysers understand, so under native
AOT a type the context does not hold is `BinaryConfigurationException`, never reflection.

## Options

| | Option | Gives | Costs | Status |
|---|---|---|---|---|
| **a** | Façade of names (one statically typed factory per §23 generic definition, returning an opaque registration) + typed handles + guarded reflection | Native AOT without warnings; shapes and codecs stay internal | About sixty public factory members; a second resolution path that must equal `FormatterRegistry` (held by a test, as `TypeShapes` is); the generator emits a codec registration for everything it reaches; the AOT consumer published **and run** in CI | **chosen** |
| b | Publish the shapes and codecs | The same | Freezes the traversal protocol | rejected |
| c | `[UnsafeAccessor]` / `UnsafeAccessorType` from generated code to internal factories, by name | No public surface | A binding by strings across package versions: an internal rename breaks consumers' builds | rejected |
| d | Root the closed types with `[DynamicDependency]`, keep runtime construction | Trimming safe | `MakeGenericType` over value types is still not AOT-safe; the warnings stay | rejected |
| e | Leave it (S4-A) | Nothing to do | No native AOT | superseded |

## Open questions — answered in P4c's brief

1. Where the factories live (protected members of the context, or a public static class) and their names.
2. The typed handle and which entry points take it (sync, async, stream, pipe, `Populate`, the dumper).
3. Seeding the process-wide `FormatterCache<T>` from a context versus a per-context codec table, and why
   either is or is not a process-wide mutable registry in the sense of INV-7.
4. The CI job that publishes and runs the AOT consumer.

## Version impact

| Surface | Version |
|---|---|
| Before `v1.0.0` (P4d) | none — `Development-Workflow.md` §5.6 |
| The same added after `v1.0.0` | **minor** (§5.3): new types, members and overloads; existing entry points unchanged |
| Removing the annotations from the existing generic entry points | not possible: without a handle they still reach reflection |

## Preconditions

- P4a's profile and P4b, so P4d is measured against the fast path.
- The owner's approval of P4c's brief.
