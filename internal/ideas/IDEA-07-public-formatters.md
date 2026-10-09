# IDEA-07 — Public formatter contract

**Class: plan.** **Status:** idea. Recorded, not scheduled, in `rc2/RC2-Plan.md` §2.

## What and why

A consumer cannot today teach the serializer a new container or scalar type: the shapes
(`IScalarFormatter<T>`, `ISequenceShape<…>`, `IMapShape<…>`, `IArrayShape<…>`, `ICompositeFormatter<T>`)
and the registry are internal. A type the registry does not claim is member-encoded through its contract,
which fits records but not, say, a third-party immutable collection.

## Where it was written

- Contract §21.3; `rework/Rework-Plan.md` §15; `CLAUDE.md`, "Adding a formatter".

## What is already fixed

- The shapes receive no count and no primitive; the engine owns every loop (barriers 2 and 3).
- Publishing them freezes the traversal protocol: any later change to a shape interface is a major.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | Publish `ISequenceShape` and `IMapShape` only, registered through the context | Custom collections, with the engine still owning count, loop, depth, identity | Two interfaces frozen with their builder/enumerator generics |
| b | Publish all five shapes | Full extensibility | The whole protocol frozen; `IScalarFormatter` exposes `WireReader`/`WireWriter`, which would have to become public too — the byte monopoly would gain public callers |
| c | A surrogate: `[BinarySurrogate(typeof(ListOfX))]` maps an unsupported type to a supported one | No protocol published | An allocation per value; conversion code by the consumer |

Recommended when taken up: **c** first; **a** only with a demonstrated need. Never b.

## Open questions

1. Which types do consumers actually miss? (Evidence from issues after the release.)

## Version impact

| Surface | Version |
|---|---|
| New public interfaces or a new attribute | **minor** to add |
| Any later change to a published shape | **major** |

## Preconditions

- Requests from consumers that the surrogate (c) does not answer.
