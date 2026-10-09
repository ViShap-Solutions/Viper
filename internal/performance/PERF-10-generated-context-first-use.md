# PERF-10 — A generated context creates every contract at once, and a generated contract compiles on first call

**Class: plan.** A performance finding and the proposal it leads to, for the owner to decide on; its status line says what became of it, and it defines no behavior.

**Status:** Open
**Raised:** 2026-10-10, while working rc.2 P4a (PROF-11, PROF-12)
**Touches the wire / the public surface / a security boundary:** no for the measurement. Option 2 below
would add a protected member to `BinarySerializerContext`, a public-surface change; option 1 changes
only what the generator emits.

## What was measured

Measurement `Measurements/v1.0.0-rc.1-8-g7da99ba-20261009T204932Z` (P4a, `--track A --filter
'*GeneratorProfile*' --soak 0`), source revision `7da99ba` with the P4a harness uncommitted; nothing in
`src/` differs from the revision. Same machine as every earlier measurement, publication job. The
context is `BenchmarkContracts`: 25 listed roots, from which the generator writes 34 contracts — every
member-encoded type of the corpus they reach.

`first-use.csv` — one process per launch, 10 launches per profile, medians in µs, [min, max]:

| Row | B-P0 (reflected) | B-P0g (generated) |
|---|---|---|
| context instantiated (`BenchmarkContracts.Default`) | — | 20 842 [19 426, 26 142], 61 248 B |
| options and serializer built | 4 685 [4 171, 5 381] | 6 565 [6 163, 8 111] |
| first serialize, sum over 27 types | 308 121 | 87 530 |
| first deserialize, sum over 27 types | 43 577 | 67 259 |
| `WideKeyed200` serialize / deserialize | 45 090 / 247 | 5 157 / 5 738 |
| `WideObject` (200 positional) serialize / deserialize | 39 661 / 205 | 5 120 / 5 474 |
| `MediumObject` serialize / deserialize | 11 716 / 991 | 2 232 / 2 039 |

Over the whole corpus a generated context costs less to bring up: 20.8 + 1.9 + 154.8 ≈ 177 ms against
351.7 ms. The first write of a type is ×0.11–0.20 of the reflected one for wide types. The first read
is the reverse: under reflection it is a fraction of a millisecond, because the contract and both of
its accessors were built by the write; under the generator it is as large as the first write.

`cold-start.csv` — the whole first operation of a fresh process, 20 launches: B-P0g is faster in every
cell, with ranges that do not overlap — DATA-01 serialize 70.0 ms [65.9, 72.4] against 83.4 ms [76.9,
155.7], DATA-02 82.1 [77.9, 87.3] against 107.0 [101.0, 111.3], DATA-13 70.3 [66.0, 79.0] against 85.3
[81.1, 91.3]. Those cells include the context's 21 ms.

## What the numbers suggest

Two costs that P4b's items do not touch:

- **The context is eager.** Its generated constructor creates every contract it reaches and adds it,
  so the first read of `Default` builds and JIT-compiles all of them — about 0.6 ms per contract over these 34 —
  whether the process will serialize one type or all of them. An application whose context lists many
  types and which uses few of them on its first request pays for all.
- **Generated code is compiled on its first call.** A generated `Write` or `ReadPositional` is one
  straight-line method per type; its first call JIT-compiles it, roughly 25 µs per member at 200
  members. Reflection compiles a getter and a setter per member when the contract is built, which
  lands on the first write; the generator moves half of that cost to the first read. The total is
  lower; it is redistributed, not removed.

## What is proposed

Each is independent:

1. **A lazy context.** The generator emits, per contract, a factory the snapshot calls when the type is
   first looked up, instead of an instance created in the constructor. The frozen table maps a type to
   a lazily created contract; the first operation on a type pays for that type alone.
2. **The same through the public seam**, if hand-written contexts should get it too: a protected
   `Add<T>(Func<TypeContract<T>>)` beside `Add<T>(TypeContract<T>)`. A surface change, so the owner's.
3. **ReadyToRun for the consumer's assembly.** The generated contracts live in the consumer's
   assembly, so a consumer who publishes it ReadyToRun gets them precompiled. Nothing in the library
   changes; `docs/generator.md` could say so once it is measured. With native AOT (P4d) the question
   disappears.

## What it would cost

(1) is a change to the emitter and to `ContractSet`, which would hold a factory or a lazily published
instance per type: a benign race (two threads may create the same contract; one wins) and an
indirection on the first lookup only, if the steady-state cache P4b adds sits in front of it. It keeps
INV-7: the context is still per configuration. (2) is public API and documentation. (3) is a build
setting on the consumer's side.

## What is not known

How the 21 ms divides between the JIT of the context's constructor, the JIT of each contract's
constructor and member description, and `ToFrozenDictionary` — an ETW JIT trace of one launch would
say. How much ReadyToRun recovers, since the harness does not publish ReadyToRun. Whether a context of
five types behaves as a fifth of this one: the measurement has one context size.

## Outcome

Filled in by the repository owner.
