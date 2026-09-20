# PERF-01 — a `byte[]` is bounded by `MaxArrayLength`, not by `MaxByteBlobBytes`

**Status:** Open
**Raised:** 2026-09-20, while working Benchmark-Plan B0 (corpus verification, DATA-08 and DATA-14)
**Kind:** Observation about behavior a consumer will meet, plus a possible change that would touch the wire
**Touches the wire / the public surface / a security boundary:** the observation touches nothing; the
possible change in *What is proposed* would change the wire format and the limit semantics, so it is
a different decision from the rest of this file.

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

## What is proposed

Two independent options, in increasing order of cost. Neither is applied.

**A. Documentation only.** State the interaction where a consumer will read it: in the XML docs of
`MaxArrayLength` and `MaxByteBlobBytes`, and in §5 of the contract. Changes no behavior and no bytes.

**B. A blob path for `byte[]`.** Encode a `byte[]` as a blob rather than as an array of elements, so
that it is governed by `MaxByteBlobBytes` and charged once rather than per byte. This would make the
limit names match the intuition and would remove the per-byte element accounting for bulk data.

Option B **changes the wire format** for every payload containing a `byte[]`, so it belongs behind a
new format version rather than in v1.x, and it changes which limit a hostile payload is measured
against — a security-relevant change, not only an encoding one.

## What it would cost

- **A** costs a documentation pass and nothing else.
- **B** costs a format version, a compatibility story for the frozen v1.0.0 payloads, and a fresh
  review of the amplification bound: a blob-encoded array is one declared length instead of a
  validated element count, so the barrier that bounds it changes from §6's element budget to the
  blob's own ceiling. That review is the real cost, not the encoding.

## What is not known

- No measurement yet exists for what per-element accounting costs on bulk `byte[]`. B0 verification
  produced sizes, not timings; DATA-14 timings arrive with B1 and §18.
- Whether any consumer scenario actually needs a `byte[]` above 1 MB often enough to justify B,
  rather than raising the limit in configuration.

Until those exist, only option A is supported by evidence.

## Outcome

*(the repository owner decides)*
