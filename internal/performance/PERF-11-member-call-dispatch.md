# PERF-11 — Scalar members without interface dispatch, reference-typed members without generic-dictionary lookups

**Class: plan.** A performance finding and the proposal it leads to, for the owner to decide on; its status line says what became of it, and it defines no behavior.

**Status:** Open — deferred past `v1.0.0` by the owner on 2026-10-14
**Raised:** 2026-10-14, while working rc.2 P4b (items 2 and 3 of its plan), PROF-10, R-05, R-06
**Touches the wire / the public surface / a security boundary:** no. Both changes are internal to the
engine; a public overload of `MemberWriter.Member` or `MemberReader.Member` for `string` is named below
only as the alternative that would be a surface change, and is not proposed.

## What was measured

Nothing isolates either cost yet, and this file claims no speedup. What exists is the reason the plan's
condition for the two items could not be met:

- rc.2 P4b admitted items 2 and 3 "only if P4a's steady-state numbers show" their cost. P4a
  (`GeneratorProfileBenchmarks`, PROF-10) measures whole operations under a generated contract beside
  the same operations under the reflected one. Both contracts reach their members through the same
  calls — `ReflectedContract<T>` writes `writer.Member(…)` and reads `reader.Member<TMember>()` exactly
  as a generated contract does — and both reach the same scalar codecs, so a cost on that path is the
  same on both sides of the comparison and cancels out of it. P4a cannot show these costs by design.
- Comparing two runs instead, before and after a change, is limited by R-06: on this machine a cell
  moves by up to ×1.18 between two runs on the same day on code it does not exercise differently,
  more than BenchmarkDotNet's error interval, so a gain of a few percent on an operation is not
  separable from the drift.

## What the numbers suggest

The mechanisms are read from the code, not from a profile:

- **Scalars.** A scalar member is written through `FormatterCache<TMember>.Instance.Write`, a virtual
  call on `Codec<TMember>`, and `ScalarCodec<T>` then calls `IScalarFormatter<T>.Write`, an interface
  call on the formatter object it holds (`Engine/Codecs/ScalarCodecs.cs`). Neither call can be inlined,
  around an encoding that is a handful of instructions for an `int`. The read side is the same.
- **Reference-typed members.** For a reference-typed `TMember` — `string`, a collection, a nested
  class — the runtime compiles `MemberWriter.Member<TMember>` and `MemberReader.Member<TMember>` once,
  shared by every reference type. Inside the shared body, `FormatterCache<TMember>.Instance` and the
  member check against `typeof(TMember)` are resolved through the runtime's generic dictionary on every
  call. A value-typed member gets a specialised body and no lookup.

Expected order of magnitude: a few nanoseconds per member call, the same under reflection and under
the generator. It would matter on wide types and on batches of many small objects (DATA-04, the
200-member types of SCALE-04), and not at all on blob-dominated payloads. This is an estimate, not a
measurement.

## What is proposed

First the measurement, then each change only if its cells show the cost:

1. **Measure.** A component suite under §18.1 (the grant of Q1 already lets the harness see internals):
   one positional and one keyed member of `int`, `long`, `double`, `Guid`, `string` and a nested class,
   written and read through a `MemberWriter`/`MemberReader` in isolation, nanosecond cells within one
   run. To compare two trees, both builds in one BenchmarkDotNet session, which also answers R-06 for
   this comparison.
2. **Scalars specialised.** `ScalarCodec<T, TFormatter> where TFormatter : struct, IScalarFormatter<T>`,
   the scalar formatters becoming structs, so the inner call is direct and can be inlined; the
   registry closes the codec over the formatter's struct type. The outer virtual call on `Codec<T>`
   stays; the JIT's guarded devirtualization may or may not remove it, which the cells of (1) show.
3. **Reference-typed members without the lookup.** One of, decided by (1):
   - **3a.** A per-contract table of codecs by member index, filled once from the contract's
     description when the contract is first used; `Member<TMember>` takes `_codecs[index]` and the
     check compares the description's type with the codec's, so the shared body does no
     generic-dictionary lookup per call.
   - **3b.** A static generic helper the shared body calls once per member type and caches in the
     writer's state — cheaper to build, weaker if the JIT still resolves the helper per call.
   - **3c.** Public non-generic overloads (`Member(string)`, `Member(int, string)`) — specialised by
     construction, but a public-surface change, the generator would emit them, and every other
     reference type still takes the shared path. Named for completeness; not proposed.

## What it would cost

(2) touches every scalar formatter and the registry's scalar table; the formatters keep their
encodings, so the frozen fixtures, `CONF-*` and the byte-identity tests are the arbiters, and the
three structural barriers are untouched — a formatter still reads no count and runs no loop. Each
closed `ScalarCodec<T, TFormatter>` is one more generic instantiation, which matters for P4d (native
AOT), where every one must be reachable statically. (3a) adds one array per contract, built once and
immutable after; it must not become a second place that knows a member's encoding — it holds the
engine's own codecs, chosen by the same `FormatterCache<T>`. The per-call checks of INV-2 and INV-5
stay exactly as they are; neither change may skip one.

## What is not known

The size of either cost on this runtime, since nothing has measured it in isolation. Whether dynamic
PGO and guarded devirtualization already remove part of the scalar dispatch in steady state. How (2)
interacts with the context-supplied codecs P4d introduces for native AOT — the change is cheaper made
after P4d's design than before it.

## Outcome

2026-10-14 — deferred past `v1.0.0`: items 2 and 3 of rc.2 P4b are not made in `rc.2`. The changes are
internal and touch neither the wire nor the public surface, so they may come in any `v1.x`, measured
first as (1) says.
