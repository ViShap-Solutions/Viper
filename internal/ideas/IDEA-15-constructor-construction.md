# IDEA-15 — Construction through a constructor with parameters

**Class: plan.** **Status:** idea.

## What and why

A member-encoded class needs a parameterless constructor; a class that only has constructors with
parameters is `BinaryTypeException` on read (`docs/contracts.md`, Construction). That excludes the shape
modern C# encourages for data: a positional record (`record Point(int X, int Y)`), a class with a
primary constructor, an immutable type whose invariants its constructor checks. `init` and `required`
members are already reached (`docs/generator.md`); the constructor is the gap.

## Where it was written

- The owner's review of an outside assessment of `v1.0.0`, 2026-10-27.

## What is already fixed

- A member-encoded value is created through its contract (`TypeContract<T>.Create`) and registered
  before its members are read, so a cycle back to it resolves (contract §16.1).
- The public seam `TypeContract<T>` is an abstract class (contract §14.3), so a virtual member with a
  default implementation can be added without breaking a contract already written against it
  (`Development-Workflow.md` §5.3).
- A type without a parameterless constructor is `BinaryTypeException` on read (contract §23, its list of
  what is not supported); that line moves when this is built.
- The wire carries members, not constructor arguments: positional order and keys stay as they are.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | Nothing; the type declares a parameterless constructor, of any visibility | No change | Records and primary constructors need a second constructor written for Viper |
| b | The single public constructor with parameters, matched to members by name (ordinal, ignoring case), as `System.Text.Json` does | Records and primary constructors work unannotated | Members are read into locals before the instance exists, so a back reference into the object from its own members cannot resolve: a cycle through such a type is refused under `PreserveReferences`; a rule for several constructors |
| c | b, chosen explicitly with `[BinaryConstructor]` | No guessing among constructors | A new attribute in Core |
| d | b or c in the generator only, with the reflection path keeping a. | Straight-line code reads locals and calls the constructor; nothing compiled at run time | The two paths stop being one rule set — against contract §14.4, where the generator reproduces the reflected contract rule for rule. Not recommended |

```csharp
public sealed record Point(int X, int Y);          // today: BinaryTypeException on read; b: X, Y → new Point(x, y)

[BinaryContract]
public sealed record Customer([property: BinaryKey(1)] string Name,
                              [property: BinaryKey(2)] string? Email);   // a missing key passes default
```

Recommended when taken up: **c** with b as its default when one public constructor exists — explicit
when there is a choice, unannotated for the common record.

## Open questions

1. A member the constructor does not take but that has a setter: assigned after construction, as
   `System.Text.Json` does, or refused?
2. A keyed field absent from the payload: the parameter's default value, or `default(T)`?
3. Under `PreserveReferences`, is a cycle through a constructed type refused at write time (the writer
   sees the cycle) or only at read time?
4. Does `Populate` refuse such a type, since it cannot construct in place?

## Version impact

| Surface | Version |
|---|---|
| Behaviour: a type that was `BinaryTypeException` becomes supported (§5.3) | **minor** |
| API: a member of `TypeContract<T>` for construction from read values, with a default implementation; `[BinaryConstructor]` for c | **minor** |
| Wire: none — the bytes of every type supported today are unchanged | — |

## Preconditions

- The design of the construction member of `TypeContract<T>`: how a contract receives member values
  before the instance exists without boxing, and how the engine's per-call checks (contract §14.3) hold
  over it.
- `ByteIdentityTests` and the conformance suites extended to constructed types on both paths.
