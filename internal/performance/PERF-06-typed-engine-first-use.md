# PERF-06 — Win back the first operation the typed engine made slower

**Class: plan.** A performance finding and the proposal it leads to, for the owner to decide on; its status line says what became of it, and it defines no behavior.

**Status:** Open
**Raised:** 2026-09-28, while working rework R4 (COLD against `Baselines/pre-rework/`, MICRO-04 cold half)
**Touches the wire / the public surface / a security boundary:** no

## What was measured

Measurement `Measurements/48c7bf5-20260928T082209Z` against `Baselines/pre-rework/`, through
`--compare` (BASE-02). Same machine, publication job, idle.

`cold-start.csv` — a fresh process per launch, 20 launches per cell. Every one of the 18 cells is
slower, with non-overlapping ranges; process startup itself is unchanged (56 ms → 56–60 ms):

| Cell | pre-rework, median [min, max] | R4, median [min, max] | Ratio |
|---|---|---|---|
| B-P0 DATA-01 serialize | 41.9 ms [40.5, 45.0] | 70.8 ms [69.0, 78.4] | 1.69 |
| B-P0 DATA-01 deserialize | 45.7 ms [44.6, 49.5] | 70.2 ms [66.5, 80.3] | 1.54 |
| B-P0 DATA-02 serialize | 56.5 ms [55.5, 60.3] | 90.3 ms [86.9, 99.4] | 1.60 |
| B-P6b DATA-02 serialize | 64.2 ms [61.3, 66.6] | 99.2 ms [95.6, 101.5] | 1.55 |
| B-P7 DATA-03 deserialize | 47.4 ms [45.4, 51.2] | 67.3 ms [65.1, 77.7] | 1.42 |

The range over all 18 cells is ×1.34 to ×1.69, about +20 to +35 ms per first operation.

`contract-cold.csv` — the construction of one member plan with the path warm, 200 samples:

| Row | pre-rework median (p95) | R4 median (p95) | Allocated |
|---|---|---|---|
| `ColdShape<T>`, 8 members | 1 568 µs (1 853) | 2 065 µs (2 701) | 94 064 → 136 784 B |
| `ColdKeyedShape<T>`, 8 members | 1 572 µs (1 829) | 2 300 µs (2 918) | 99 296 → 140 816 B |

The single-observation rows of the SCALE-04 series move the same way where they are not noise
(`WidePositional100` 19.5 → 28.7 ms, `WideKeyed020` 3.7 → 6.2 ms).

In steady state the same code is faster everywhere that matters: WL-01 median ×0.49, WL-04 ×0.41,
allocation ×0.11–0.25 (the same comparison, `ProfileMatrixBenchmarks`).

## What the numbers suggest

The first operation now pays for things the object-typed engine did not have:

- **JIT per closed generic type.** Every declared type gets its own codec class
  (`SequenceCodec<…>`, `ObjectCodec<T>`, …), every member its own `MemberAccessor<T, TMember>`, and
  every value-type instantiation is compiled separately. The old engine compiled one set of
  object-typed methods for all types.
- **Two compiled delegates per member** (getter and setter, `Expression.Compile`), each a dynamic
  method JIT-compiled on first call, plus `MakeGenericType` and `Activator.CreateInstance` per member.
- **The registry's frozen tables**, built on first use of the engine (`ToFrozenDictionary` over
  about 110 entries), and the `FrozenDictionary` of keys per keyed contract.

The contract row isolates the second point (+30–45 % per plan, +40 KB); the cold-start cells add the
first and third, which the contract row does not include. The measurement does not separate them.

## What is proposed

In order of expected gain per unit of risk; each is independent:

1. **Compile the library ReadyToRun** (`PublishReadyToRun` for the package): the engine's own
   methods and the instantiations over BCL types (`ScalarCodec<int>`, `ListShape<string>`, …)
   ship precompiled. Instantiations over a consumer's value types still JIT.
2. **Build members lazily**: compile the setter of a member on the first read, not when the
   contract is built, so a process that only writes never compiles setters (and vice versa for
   getters).
3. **Replace the frozen registry tables with plain dictionaries** built once: lookups happen once
   per type, so a frozen dictionary's faster lookup buys nothing and its construction costs
   milliseconds on the first operation.
4. **Emit accessors with `DynamicMethod` instead of `Expression.Compile`**, which avoids loading and
   warming the expression compiler on the first contract.
5. After the release, the source generator (`Rework-Plan.md` §13) removes the reflective contract
   build for generated types altogether; it is the structural answer, the four above are the
   interim one.

## What it would cost

(1) is a build setting, larger assemblies, no code change. (2) adds a lazily-initialised field per
accessor and a race that must stay benign (two threads may both compile; one wins). (3) is a
one-line change per table. (4) replaces readable expression code with IL emission in one class,
which must keep assigning a struct owner through `ref` and must keep skipping visibility for
`[BinaryInclude]` members. None touches the wire, a barrier or an invariant.

## What is not known

How the +20–35 ms divides between JIT, accessor compilation and the frozen tables — a per-phase
cold profile (ETW JIT events per method) would say. Whether ReadyToRun helps enough to matter for
a consumer whose types are mostly its own value types. The steady-state cost of (2), which adds a
check per member access unless done carefully.

## Outcome

Filled in by the repository owner.
