# Benchmark-Plan.md — changes required

**Applies to:** `internal/Benchmark-Plan.md` as of 2026-09-26.
**Scope rule unchanged:** benchmark work is read-only over `src/` and the contract; it writes only to
`benchmarks/`, the plan and `internal/performance/`. The method stays in the `viper_bencher` skill.

Three parts: §1 is owed now; §2 is owed to the rework, stage by stage (`Rework-Plan.md` §12); §3 is
Track B once the format is final. Every new ID continues its prefix from the highest number in use or
reserved here, so nothing is renumbered:

```text
highest in Benchmark-Plan.md   CMP-08 MICRO-14 ALLOC-08 SIZE-08 PROF-08 COLD-08 WL-17 DIFF-07 SEC-08
reserved by §1                 CMP-09 CMP-10 MICRO-15 MICRO-16
```

Plan sections are cited as "plan §n" (`Rework-Plan.md`).

---

# 1. Owed now

The NX fixes changed three paths the plan does not measure.

| New ID | Section | Checkpoint | Why |
|---|---|---|---|
| CMP-09 | §15 | incremental decompression for `Deflate` and `Brotli`: time and allocation against the previous single-buffer path, at every corpus size | NX-01 replaced the path; its cost on a legitimate payload is unknown, and a 64 KiB probe promoted once is a copy on every payload above 64 KiB |
| CMP-10 | §15 | the `MaxDecompressionRatio` check, isolated | expected to be negligible; a number makes it a fact |
| MICRO-15 | §18.1 | `CollectionCountCache` lookup, and the write-side fast path it enables for sets, frozen and immutable sets against the old materialising path | NX-02 removed an intermediate list per set written; it may be the largest incidental gain of the NX changes and nothing records it |
| MICRO-16 | §18.1 | the duplicate check after `Complete` — the count read on every container read | NX-02 put it on the read path of every collection |

Also owed now:

- **FAIR-07** — keep today's wording, which reports the missing `IBufferWriter` entry point as a
  fact, until R3; then rewrite it (§2, R3).
- **MICRO-09** — names the three metered streams; valid until R2.

---

# 2. Owed to the rework, by stage

## R0 — the one run taken before the rework

- **Track A baseline on the `pre-rework` commit**: the entry commit is tagged locally `pre-rework`
  (not `v*`, so CD never sees it), the complete Track A runs so the harness records a Baseline, the
  tag is deleted, and the raw results are committed under `Baselines/pre-rework/`. This is the "before"
  of every stage below; without it no stage can show an improvement, and any claim it made would be
  what §28 forbids.
- **§25**: add the rule that a rework stage compares against `pre-rework`, and a release against the
  previous release.
- **PROF-09** — the same profile set on `pre-rework`, so the matrices compare cell by cell.

## R1 — Wire primitives on buffers

- **MICRO-01** rewritten for `WireReader`/`WireWriter`, compared cell by cell with the `pre-rework`
  `ValueReader`/`ValueWriter` numbers.
- **MICRO-17** — a positional record of many booleans and small integers: the path where a virtual
  call per byte dominates, so R1's gain is visible rather than averaged away.

## R2 — Pipeline on pooled buffers

- **ALLOC-02**, **ALLOC-03** re-attributed: `MemoryStream` and `ToArray` disappear from the phase list.
- **ALLOC-05** rewritten: V0 and V1 now buffer alike (plan §5.1); measure what the header adds.
- **ALLOC-06** — re-measure the LOH threshold crossings; pooled buffers should remove most of them for
  payloads under the pool's largest bucket.
- **MICRO-09** replaced by the metering and windowing of the reader and writer.
- **ALLOC-09** — cycle detection without references: the ancestor stack against the removed
  per-operation `HashSet`, at depths 4, 32 and 500 (plan §11).

## R3 — New entry points

- **§10** workload catalog: add the buffer family (`IBufferWriter<byte>` in; `ReadOnlySpan<byte>` and
  `ReadOnlySequence<byte>` out), the pooled family (`SerializePooled`), and the asynchronous family
  (`Stream`, `PipeReader`, `PipeWriter`).
- **ALLOC-04** — allocation per family, now including the buffer, pooled and asynchronous families.
- **FAIR-07** rewritten: the buffer family is directly comparable with other serializers' buffer
  entry points.
- **WL-18** — a non-seekable stream, the path that did not exist before.
- **WL-19** — an encrypted frame written to an `IBufferWriter<byte>`: the path with no final copy
  (plan §5.1), against the same frame to `byte[]`.

## R4 — Typed engine

- **ALLOC-10 … ALLOC-21** — one checkpoint per line of plan §11's target table, each stating its
  target and its measured value. A target not met stays open with its number and is never rounded to a
  claim.
- **COLD-*** — cold start against `pre-rework`. The typed engine builds more per type (a generic
  closure per member, JIT per value-type instantiation); this is where that cost shows. A regression
  is written up in `internal/performance/` before the stage closes.
- **MICRO-04**, **MICRO-05**, **MICRO-06** rewritten for `FormatterCache<T>`, the codecs and
  `ReflectedContract<T>`.
- **MICRO-18** — a primitive array read into an array of its final length against the pooled path
  (plan §10.1), at sizes 16, 4 096 and 1 000 000.

## R5 — Algorithm contracts

- **§15** unchanged in scope — ZLib is not added. **§16**: add `XxHash3Checksum`,
  `XxHash128Checksum` and `ChaCha20Poly1305Encryption`; the renamed built-ins (`Crc32Checksum`,
  `DeflateCompression`, `BrotliCompression`, `Aes256GcmEncryption`) keep their existing cells. **§8** profiles: one profile per new phase choice.
- **MICRO-10** extended to every built-in.
- Note in **§16**: ChaCha20-Poly1305 against AES-GCM depends on AES hardware support; the manifest
  records the CPU, and the result must name it.

## R6 — The final format

- **§14 re-measured entirely.** SIZE-02 (envelope cost), SIZE-03 (reference framing) and SIZE-05
  (per-string, per-element, per-null costs) are the direct evidence for plan §6; each is published as
  `pre-rework` → R6.
- **SIZE-09** — the service-record header against today's fixed header, for no service, one, two and
  three services (plan §6.1).
- **SIZE-10** — the null fold on a wide nullable record and on a list of strings (plan §6.3.2).
- **MICRO-08** rewritten: header write and parse over service records; the separate AAD image is gone
  (the associated data is the header bytes).
- **SEC-04** rewritten for the same reason.

## R8 — Generator ground

- No new measurement. §18 notes that the conformance suite fixes the contracts a later generated path
  must match, so a later generated-versus-reflected comparison is like for like.

## R9 — Release

- Track A publication run on the release tag, per §29.1.
- §28 publication gate evaluated. Only after it may an adjective appear in a package description or a
  README.

---

# 3. Track B, once the format is final

- Starts after the release, on the `v1.0.0` tag. Nothing from Track B is measured against an
  intermediate stage.
- §5 roster re-verified on the day (RST-03).
- The buffer family (R3) makes FAIR-07 comparisons direct against MemoryPack and MessagePack-CSharp,
  which the pre-rework format could not enter.
- §6 capability tiers regenerated from the probes of §7.5 (TIER-04). V0 carries no phase and no
  references, so its tier membership is unchanged by the rework.
- §17 composed baselines: V1 with its phases against a hand-composed payload plus the same algorithms,
  as today.
