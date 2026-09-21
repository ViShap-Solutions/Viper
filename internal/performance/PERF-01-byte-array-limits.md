# PERF-01 — a `byte[]` is bounded by `MaxArrayLength`, not by `MaxByteBlobBytes`

**Status:** Resolved — contract and documentation clarification, no behavior change
**Raised:** 2026-09-20, while working Benchmark-Plan B0 (corpus verification, DATA-08 and DATA-14)
**Kind:** Observation about behavior a consumer will meet
**Touches the wire / the public surface / a security boundary:** no. The resolution changes wording
only — the contract's §5, the XML documentation of three limits, and the text of the exception a
breach raises. The wire format, the limits and their defaults are untouched. The design question the
observation raised is separate, and it is [PERF-02](PERF-02-bulk-binary-accounting.md).

## What was observed

The corpus was built with a 15 MB `byte[]` because `MaxByteBlobBytes` defaults to 16,000,000. Every
profile refused it:

```text
BinaryLimitException: Array length 15000000 exceeds the configured maximum of 1000000.
```

Reading `src/` explains it, and the contract already says so:

- `MaxByteBlobBytes` (§5.6) bounds **blob-encoded values** — `ValueWriter.WriteBlob` is reached only
  from the `BigInteger` payload and the `BitArray` data (§22.4).
- A `byte[]` is an ordinary array. It is bounded by `MaxArrayLength` (§5.2), which defaults to
  1,000,000, and each element is charged against `MaxTotalElements` (§6).

Nothing here is a defect. The two clauses are consistent, and the library behaved exactly as §5
describes. It was the benchmark corpus that was built on a wrong assumption, and it was resized.

## What follows for a consumer

Two consequences that a reader of §5 will not necessarily assemble on their own:

1. **A `byte[]` member above 1,000,000 bytes is refused under the default policy**, although a limit
   named "byte blob" sits at 16,000,000 and looks like the one that governs it. The default `byte[]`
   ceiling is therefore ~1 MB, not ~16 MB.
2. **Bulk binary data spends the element budget.** One megabyte of `byte[]` costs 1,000,000 of the
   10,000,000 `MaxTotalElements` an operation has, so roughly ten megabytes of byte arrays exhausts
   it regardless of how few objects the graph contains.

An application carrying files, images or compressed blobs meets both on its first large payload, and
the fix — raising `MaxArrayLength` and `MaxTotalElements` deliberately — is a policy decision it
should make knowingly.

The second consequence is the one that matters in practice: raising `MaxArrayLength` alone does not
admit a 15 MB array. Both limits have to move together, and nothing said so.

## What was proposed

Two independent options, in increasing order of cost.

**A. Documentation only.** State the interaction where a consumer will read it: in the XML docs of
`MaxArrayLength` and `MaxByteBlobBytes`, and in §5 of the contract. Changes no behavior and no bytes.

**B. A blob path for `byte[]`.** Encode a `byte[]` as a blob rather than as an array of elements, so
that it is governed by `MaxByteBlobBytes` and charged once rather than per byte. This would make the
limit names match the intuition and would remove the per-byte element accounting for bulk data.

Option B **changes the wire format** for every payload containing a `byte[]`. It also changes which
limit a hostile payload is measured against — a security-relevant change, not only an encoding one.

## What it would cost

- **A** costs a documentation pass and nothing else.
- **B** costs the frozen v1.0.0 payload fixtures, a fresh review of the amplification bound, and a
  rule the format does not have today. On a released version it would additionally cost a format
  version; at the time this was raised no `v1.0.0` tag existed, so compatibility was not yet what
  stood in its way, and the decision below was taken on design grounds rather than on that one.

The amplification review is the real cost: a blob-encoded array is one declared length instead of a
validated element count, so the barrier that bounds it changes from §6's element budget to the blob's
own ceiling.

## What is not known

- No measurement yet exists for what per-element accounting costs on bulk `byte[]`. B0 verification
  produced sizes, not timings; DATA-14 timings arrive with B1 and §18.
- Whether any consumer scenario actually needs a `byte[]` above 1 MB often enough to justify B,
  rather than raising the limit in configuration.

## Outcome

**Option A, applied; option B rejected for the format as it stands.**

The model is correct as it is. Every limit in §5 bounds a **wire form**, not a CLR type, and the
element type never changes a container's wire form. `byte[]` is an array, as `int[]` is; the blob form
is reached by two scalar encodings and nothing else. One rule, no table of exceptions.

B was rejected on two grounds, neither of them compatibility:

- It would make the **element type** decide the wire form, and §23 supports `Memory<byte>`,
  `ReadOnlyMemory<byte>`, `ArraySegment<byte>`, `ReadOnlySequence<byte>`, `ImmutableArray<byte>` and
  `List<byte>` beside `byte[]`. Either all of them move, which is a large part of the format, or one
  does and the result is a worse inconsistency than the one it set out to fix — a consumer would then
  have to learn which byte-shaped containers get 16 MB and which get 1 MB.
- It would swap a **structural barrier** for a different one. `byte[]` is read through the array path,
  so every byte passes `ElementCount` and the bounded growth of §17 — the second of the three
  barriers. A blob is one declared length. That is a different amplification bound, and trading it
  for ergonomics is the wrong exchange.

What shipped instead, in the same change as this outcome:

- **Contract §5.2** states that `MaxArrayLength` bounds every element type, `byte[]` included, and
  names the memory-like values that behave the same way.
- **Contract §5.6** is now a closed statement: it bounds the blob wire form, whose members are the
  `BigInteger` body and the `BitArray` data, and it bounds no array.
- **Contract §5.7 and §6** state that the element budget measures structural size rather than byte
  volume, that a record costs one element whatever its members weigh while a megabyte array costs a
  million, and that carrying bulk binary data means raising `MaxArrayLength` and `MaxTotalElements`
  together.
- **Contract §21.4** carries the whole model in one place, beside the other semantic clarifications.
- **The XML documentation** of `MaxArrayLength`, `MaxByteBlobBytes` and `MaxTotalElements` says the
  same thing for a consumer reading it on hover.
- **Every `BinaryLimitException` now names the property that governs it**, so the message a consumer
  actually meets answers the question at the point it is asked:

  ```text
  Array length 15000000 exceeds the configured maximum of 1000000 (MaxArrayLength).
  ```

The design question underneath — whether bulk binary data should spend a structural budget at all —
is not answered by any of this, and is carried forward as [PERF-02](PERF-02-bulk-binary-accounting.md).
