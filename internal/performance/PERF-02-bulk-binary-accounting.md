# PERF-02 — should bulk binary data spend the structural element budget?

**Status:** Open — a design question for a future format version, not a v1.x change
**Raised:** 2026-09-21, out of [PERF-01](PERF-01-byte-array-limits.md), while working Benchmark-Plan
A5 (SCALE-02 and SCALE-09)
**Kind:** Architectural question, carried forward
**Touches the wire / the public surface / a security boundary:** yes, any answer other than "leave it"
would. A binary wire form for byte-shaped containers changes the bytes, changes which limit bounds a
hostile payload, and probably adds public limits. It is a format-version decision, and nothing here
proposes acting on it in v1.x.

## What was observed

`MaxTotalElements` is charged one per element, and the element type does not change that. A record
spends one element whatever its members weigh; a `byte[]` of a million bytes spends a million. So an
operation's 10,000,000-element budget admits roughly ten megabytes of byte arrays however the bytes
are split, and no number of smaller arrays gets around it.

This showed up twice while building the Track A corpus, in both cases as an obstacle rather than a
finding:

- **DATA-14 (ByteBlob)** was sized at 0.9 MB and 3.6 MB rather than at the 16 MB the blob limit
  suggested, because `MaxArrayLength` refuses more (PERF-01).
- **SCALE-02**, the payload-size curve, could not reach its 16 MB and 64 MB points with array data at
  all. It reaches them with a batch of records instead, which spend one element each, and
  **SCALE-09** re-runs that large end under Server GC. Both points therefore measure traversal over
  many small values, not the carrying cost of one large buffer.

The second is the one worth keeping: the curve the plan asks for exists, but the harness cannot
express "one payload that is mostly bytes" at the top of it under the default policy, and that is a
property of the accounting model rather than of the harness.

## What the question is

`MaxTotalElements` bounds two things that are not the same dimension:

```text
structural complexity   how many values the operation must traverse, materialize and account for
byte volume             how many bytes it must carry
```

For `List<Person>`, `Dictionary<K,V>` or `int[]` the two move together closely enough that one budget
serves. For a buffer that is semantically one value — a file, an image, a compressed block, an
encrypted body, a nested payload — they do not: the traversal cost is one value, and the element
charge is one per byte.

The question is therefore **not** "why doesn't `byte[]` use `MaxByteBlobBytes`". PERF-01 settled
that: `byte[]` is an array, the blob form belongs to two scalar encodings, and the model is
consistent. The question is:

> Should a serializer that already separates a per-container limit, a cumulative element budget, a
> node budget and a phase byte budget also separate **bulk binary volume** from structural count —
> and if so, which containers are bulk binary?

## What a future version would have to decide

- **Which CLR shapes.** `byte[]` alone would be arbitrary; §23 supports `Memory<byte>`,
  `ReadOnlyMemory<byte>`, `ArraySegment<byte>`, `ReadOnlySequence<byte>`, `ImmutableArray<byte>` and
  `List<byte>` beside it. A rule that covers some and not others is worse than the rule today.
- **What bounds it instead.** A blob-shaped encoding replaces a validated element count with one
  declared length, so the amplification bound moves from §6's budget to a byte ceiling. That review
  is the cost, not the encoding.
- **Whether the element budget is then renamed**, since it would have become explicitly a structural
  budget.
- **Whether it earns anything measurable.** Per-element dispatch over a large `byte[]` is the obvious
  candidate for a real cost, and it has not been measured. Until A4 and §18 produce a figure for it,
  the case for the change is ergonomic, not performance.

## What is not known

- The cost of per-element accounting on bulk `byte[]`, in time and allocation. It is exactly what a
  `ValueWriter`/`ValueReader` microbenchmark (MICRO-01) plus DATA-14 timings would answer, and
  neither has been run on the recorded machine.
- Whether a real consumer payload is bulk-binary-dominated often enough to matter, or whether raising
  two limits in configuration is a complete answer in practice.
- Whether a bulk path would interact usefully with the entry points v1.0 deliberately does not have —
  `IBufferWriter<byte>`, `ReadOnlySequence<byte>` streaming, pooled buffers. That is a separate
  deferral (Contract §21.3) and should not be folded into this one without its own measurement.

## Outcome

*(the repository owner decides)*
