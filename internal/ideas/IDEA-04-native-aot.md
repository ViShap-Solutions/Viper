# IDEA-04 — Native AOT through the context

**Class: plan.** **Status:** idea. Scope option S4-B of `rc2/RC2-Plan.md` §3, not taken for `v1.0.0`
(S4-A chosen, 2026-10-09).

## What and why

The generator writes type contracts, so member access needs no reflection. The codecs around the members
— collections, arrays, nullables, the object codec itself — are still closed at run time with
`MakeGenericType` and `Activator.CreateInstance` (`Engine/FormatterRegistry.cs`), and union maps are read
from attributes. The entry points therefore keep `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`
with or without a context (`docs/aot.md`). A consumer publishing with native AOT gets warnings, and a
codec not rooted by anything may be missing at run time.

## Where it was written

- `rc2/RC2-Plan.md` §3 S4 — the check against the code and the three scopes.
- `rework/Rework-Plan.md` §13.1, whose sentence "the generated path carries none" was corrected per S4-A.
- `Audit-Future.md` §5, §8-C.

## What is already fixed

- The context (`BinarySerializerContext`) was designed so B is additive: it can carry more than contracts
  without a change to what it carries today.
- The traversal protocol (shapes, codecs) is internal and stays so (`CLAUDE.md`, "Adding a formatter";
  contract §21.3).

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | The context also supplies a closed codec for every reachable declared type; the generator emits `ContextCodecs.Get<List<Line>>() => new SequenceCodec<…>(…)` calls into internal factories through an internal-but-generated seam (`InternalsVisibleTo` is impossible for consumer assemblies) | Native AOT without warnings on entry points that take a context | A public façade over codec construction, or the codecs published — the protocol `CLAUDE.md` keeps internal; a second set of entry points without annotations |
| b | Root the closed generic types through `[DynamicDependency]`/`DynamicallyAccessedMembers` emitted by the generator, keep runtime construction | Trimming safe | `MakeGenericType` over value types is still not AOT-safe; the warnings stay |
| c | A public, frozen "codec factory" façade: one method per shape that takes element codecs | The smallest public addition that lets generated code build the graph | Freezes the shape vocabulary (sequence, map, array, composite) at the version it ships |

Recommended when taken up: a design stage with `viper_generator` before any code, comparing a and c on
what they freeze.

## Open questions

1. Is native AOT a requirement of a known consumer?
2. Which of the shapes may be frozen publicly, if any?

## Version impact

| Surface | Version |
|---|---|
| New entry points or overloads that take a context; new public façade types; existing entry points unchanged | **minor** (§5.3) |
| Removing the annotations from the existing generic entry points | not possible: they still reach reflection without a context |

## Preconditions

- A measurement of the generated path (P6 `benchmark/generator-profiles`) to know what AOT would add
  beyond it.
