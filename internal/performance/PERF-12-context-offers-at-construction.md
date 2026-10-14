# PERF-12 — A generated context builds every codec it reaches when it is created

**Class: plan.** A performance finding and the proposal it leads to, for the owner to decide on; its status line says what became of it, and it defines no behavior.

**Status:** Open — deferred past `v1.0.0` with PERF-10 by the owner on 2026-10-14
**Raised:** 2026-10-14, while working rc.2 P4d (PROF-11, PROF-12, R-07)
**Touches the wire / the public surface / a security boundary:** no for the measurement. Option 2 below
would add a protected member to `BinarySerializerContext` or a method shape to `SupportedTypes`, a
public-surface change; option 1 changes only what the generator emits.

## What was measured

Two runs of `--track A --filter '*GeneratorProfile*' --soak 0` on 2026-10-14, one after the other on the
same machine: the P4b tree (`146f03a`, `Measurements/v1.0.0-rc.1-14-g146f03a-20261014T131828Z`) and the
P4d tree (`Measurements/v1.0.0-rc.1-14-g146f03a-20261014T134616Z`). The context is `BenchmarkContracts`:
25 listed roots, 34 contracts, and — under P4d — every type they reach made supported.

`first-use.csv`, one process per launch, 10 launches, medians in µs [min, max]:

| Row | P4b | P4d |
|---|---|---|
| context instantiated (`BenchmarkContracts.Default`) | 18 695 [17 675, 20 900], 61 248 B | 39 546 [38 877, 40 481], 108 992 B |
| options and serializer built (B-P0g) | 6 238 [5 954, 7 156] | 3 564 [3 478, 3 772] |
| first serialize, sum over 27 types (B-P0g) | 76 066 | 66 472 |
| first deserialize, sum over 27 types (B-P0g) | 58 460 | 56 228 |
| **all four, per corpus** | **159 459** | **165 810** |

The reflected rows (B-P0), whose code P4d did not change, moved by ×0.93–0.98 between the two runs.

`cold-start.csv`, the whole first operation of a fresh process, 20 launches, medians in ms: B-P0g DATA-01
66.7 → 59.7, DATA-02 79.6 → 67.3, DATA-03 68.1 → 77.5; B-P0, unchanged code, 73.1 → 53.3, 96.9 → 92.6,
78.9 → 73.1 — the cold cells drift more than any change between them.

## What the numbers suggest

The context's constructor now creates, besides its contracts, the codec of every type it reaches: one
`SupportedTypes` call per type, each closing and JIT-compiling the codec's generic instantiation, and for
`Object<T>`/`Union<T>`/`Collection<T>` classifying the type (`TypeRules.Classify`, which reads its
interfaces) and checking it for `[BinaryUnion]`. That work used to happen on the first use of each type,
by reflection, which costs more per type — hence the cheaper first uses — but only for the types a process
actually uses. The constructor now pays for all of them at once: +20.9 ms here, of which about 14 ms comes
back on the first uses of the corpus, so a process that touches every type pays about 6 ms more (×1.04), and
one that touches a few pays up to the whole 21 ms.

Under native AOT the JIT share disappears — every instantiation is precompiled — and the classification is
the only remaining work; the harness does not measure a native build.

## What is proposed

Independent of each other:

1. **Lazy offers, generated.** The generator emits, per supported type, a static factory the engine calls
   when the type's `FormatterCache<T>` is first read, instead of a codec built in the constructor; the
   context records only the type. This is PERF-10's option 1 applied to codecs as well as contracts, and
   one change could do both.
2. **The same through the public seam** — `SupportedTypes` methods that return a deferred registration —
   if hand-written contexts should get it. A surface change, so the owner's.
3. **Skip the classification for generated calls.** The generator already classifies by the same tables
   (GEN-10); an internal path that trusts it would leave `Object<T>`'s check to hand-written contexts. The
   check is part of the public contract of `SupportedTypes` (§14.5), so this needs a second, non-public
   entry the generated code can reach — which only an `InternalsVisibleTo` to the consumer's assembly or a
   public "unchecked" member could give. Not recommended.

## What it would cost

(1) moves the offer to the first read of `FormatterCache<T>`, so the cache's initializer must find the
factory without a dictionary keyed by `Type` (CN-20): a write-once generic static `Func<Codec<T>>` per type,
filled by the context's constructor, keeps that. It must still be in place before the type's first use,
which the constructor guarantees as today. (2) is public API and documentation. (3) as above.

## What is not known

How the 21 ms divides between JIT, classification and allocation — an ETW trace of one launch would say.
How a native AOT build behaves, which the harness does not publish. Whether a context of five types costs a
fifth: the measurement has one context size.

## Outcome

2026-10-14 — accepted for `v1.0.0` as recorded; option 1 deferred past `v1.0.0`, to be made together with
PERF-10 option 1 as one internal change to how a generated context offers its contracts and codecs.
