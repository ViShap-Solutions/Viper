# Benchmark-Plan.md — changes required

**Class: historical.** The record of the pre-release rework. It binds nothing that ships, and its rules stop applying when `v1.0.0` is released; until then it is the evidence that R9 reconciles the system against.

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

# 1. Owed now — applied

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

**The harness lives with the code** [D9.27]. At the end of every stage R1–R6 the benchmark project
builds, `--verify` passes every pair and `--smoke` passes. A checkpoint whose measured type or entry
point the stage removes is rewritten against what replaced it, or retired, in that same stage — never
left naming something that no longer exists. A retired checkpoint stays in the plan, marked
`retired in Rn` with the reason, as in the QA plan, so the history of what was measured stays readable.

## R0 — the one run taken before the rework — applied

- **Track A baseline on the `pre-rework` commit**: the entry commit is tagged locally `pre-rework`
  (not `v*`, so CD never sees it), the complete Track A runs so the harness records a Baseline, the
  tag is deleted, and the raw results are committed under `Baselines/pre-rework/`. This is the "before"
  of every stage below; without it no stage can show an improvement, and any claim it made would be
  what §28 forbids.
- **§25**: add the rule that a rework stage compares against `pre-rework`, and a release against the
  previous release.
- **PROF-09** — the same profile set on `pre-rework`, so the matrices compare cell by cell.

## R1 — Wire primitives on buffers — applied

- **MICRO-01** rewritten for `WireReader`/`WireWriter`, compared cell by cell with the `pre-rework`
  `ValueReader`/`ValueWriter` numbers.
- **MICRO-17** — a positional record of many booleans and small integers: the path where a virtual
  call per byte dominates, so R1's gain is visible rather than averaged away.
- MICRO-06 and MICRO-08 drive their formatter and header through `WireWriter`/`WireReader`; their
  checkpoint text names no removed type and does not change.

## R2 — Pipeline on pooled buffers — applied

- **ALLOC-02**, **ALLOC-03** re-attributed: `MemoryStream` and `ToArray` disappear from the phase list.
- **ALLOC-05** rewritten: V0 and V1 now buffer alike (plan §5.1); measure what the header adds.
- **ALLOC-06** — re-measure the LOH threshold crossings; pooled buffers should remove most of them for
  payloads under the pool's largest bucket.
- **MICRO-09** replaced by the metering and windowing of the reader and writer.
- **ALLOC-09** — cycle detection without references: the ancestor stack against the removed
  per-operation `HashSet`, at depths 4, 32 and 500 (plan §11).
- **WL-03** rewritten: a V0 keyed write to a non-seekable destination succeeds (plan §5.1), so it is a
  timed workload, no longer a supported refusal.
- **MICRO-02**, **MICRO-03**, **MICRO-07** — element-count charging, depth scopes and reference
  tracking are measured through `OperationState` and the pooled reference tables. Whatever of them
  moves into the codecs in R4 is rewritten again there. *(Applied in R2 through `SerializationOperation`, which
  becomes `OperationState` in R4 — see `Claude-Changes.md` R4; MICRO-07 rents and returns the pooled
  tables.)*

## R3 — New entry points — applied

- **§10** workload catalog: add the buffer family (`IBufferWriter<byte>` in; `ReadOnlySpan<byte>` and
  `ReadOnlySequence<byte>` out), the pooled family (`SerializePooled`), and the asynchronous family
  (`Stream`, `PipeReader`, `PipeWriter`).
- **ALLOC-04** — allocation per family, now including the buffer, pooled and asynchronous families.
- **FAIR-07** rewritten: the buffer family is directly comparable with other serializers' buffer
  entry points.
- **WL-18** — a non-seekable stream, the path that did not exist before.
- **WL-20** — a long stream of small frames read with `DeserializeAsyncEnumerable` from a pipe:
  throughput and allocation per frame.
- **WL-19** — an encrypted frame written to an `IBufferWriter<byte>`: the path with no final copy
  (plan §5.1), against the same frame to `byte[]`.
- **Adapter interface.** `IBufferedSerializer` and `IStreamingSerializer` know only `byte[]` and
  `Stream`. A buffer family is added — `IBufferWriter<byte>` in, `ReadOnlySpan<byte>` and
  `ReadOnlySequence<byte>` out — and `ViperAdapter` implements it, so the Track B adapters written
  after R6 declare it or are recorded `Unsupported` for it (FAIR-07).
- **PROF-06** and **WL-07** rewritten for the entry points contract §3 lists after this stage:
  `Populate` for an existing instance (plan §9.6), and whatever `ref` form remains.
- **PROF-07** rewritten: a serializer reused across operations against one constructed per
  operation. The clause about the per-call `StreamExtensions` path is retired with `StreamExtensions`.
- Every reference to contract §3 in the plan re-pointed at the new surface.
- *Applied in R3.* The families received workload IDs of their own, continuing the prefix: **WL-21**
  (serialize → `IBufferWriter<byte>`), **WL-22** (deserialize ← span and ← sequence), **WL-23**
  (`SerializePooled`), **WL-24** (the asynchronous family). Suites: `ProfileBufferBenchmarks`
  (WL-21…WL-23), `ProfileAsyncWriteBenchmarks` and `ProfileFramedReadBenchmarks` (WL-18, WL-24),
  `FrameStreamBenchmarks` (WL-20). WL-19 has no suite of its own: it is WL-21 under B-P5 and B-P6
  read against WL-01 of the same profile, and the path with no final copy it is meant to show only
  exists once R5 gives encryption its exact ciphertext length — the plan's WL-19 row says so. The
  verifier runs every pair through the new entry points before any timing. The adapter interface is
  `IBufferWriterSerializer`, declared beside the other two in `Adapters/ISerializerAdapter.cs`.
  By the owner's decision of 2026-09-27 (`Owner-Review.md` log 56) the new suites run over the
  representative profiles B-P0, B-P1, B-P5, B-P6b and B-P7 (`ViperProfiles.Representative`), and
  R3's stage measurement covers only the suites `pre-rework` can be compared with —
  `ProfileMatrixBenchmarks` and `ProfileStreamBenchmarks` — with the soak taken on its own; the new
  suites get their first numbers in the next full `--track A`.

## R4 — Typed engine — applied

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
- **MICRO-02**, **MICRO-03**, **MICRO-07** rewritten again for what moved into the codecs.
- **BASE-02** — the comparison tool is built before this stage closes, and the COLD and ALLOC
  comparisons above are taken through it. It is a gate line of R4 [D9.25].
- *Applied in R4.* ALLOC-10…ALLOC-21 are one table in §13 with target, value and state; MICRO-02…
  MICRO-07 rewritten; MICRO-15 rewritten as well, because it named `CollectionCountCache`, which R4
  deleted; MICRO-18 is `ArrayMaterializationBenchmarks`; BASE-02 is `--compare` and is ticked. The
  cold comparison is a regression, written up as PERF-06; the fixed allocations outside the engine are
  PERF-07; the slower cells are R-03 of §27.3. Measurement: `Measurements/48c7bf5-20260928T082209Z`.

## R5 — Algorithm contracts — applied

- **§15** unchanged in scope — ZLib is not added. **§16**: add `XxHash3Checksum`,
  `XxHash128Checksum` and `ChaCha20Poly1305Encryption`; the renamed built-ins (`Crc32Checksum`,
  `DeflateCompression`, `BrotliCompression`, `Aes256GcmEncryption`) keep their existing cells. **§8** profiles: one profile per new phase choice.
- **MICRO-10** extended to every built-in.
- `ViperProfiles` and every suite that names a built-in move to the family-suffixed names. The plan
  IDs of the profiles (`B-P3d`, `B-P3b`, `B-P4`, `B-P5`, `B-P6b`, `B-P6d`) do not change, so a cell
  keeps its identity across the rename.
- Note in **§16**: ChaCha20-Poly1305 against AES-GCM depends on AES hardware support; the manifest
  records the CPU, and the result must name it.
- *Applied in R5.* New profiles **B-P4x3**, **B-P4x128**, **B-P5c**; **SEC-09** (the XXH3 checksums)
  and **SEC-10** (ChaCha20-Poly1305) added to §16; MICRO-10 covers every built-in; the enum members of
  `ViperProfile` keep their names, since a cell's parameters record them and BASE-02 matches on them —
  the plan IDs alone would not have kept the identity. The stage measurement is
  `Measurements/c215131-20260928T131001Z` (six suites), compared with `pre-rework` as R-04, with the
  regression in PERF-08; ALLOC-12…ALLOC-14 carry their R5 values.

## R6 — The final format — applied

Applied: MICRO-08 and SEC-04 rewritten, SIZE-09, SIZE-10 and MICRO-19 added (`format-sizes.csv`, `DumperBenchmarks`); the sizes, the header and the seam published in `internal/performance/PERF-09`; the seam's read cost accepted by the owner (`Owner-Review.md` log 63).

- **§14 re-measured entirely.** SIZE-02 (envelope cost), SIZE-03 (reference framing) and SIZE-05
  (per-string, per-element, per-null costs) are the direct evidence for plan §6; each is published as
  `pre-rework` → R6.
- **SIZE-09** — the service-record header against today's fixed header, for no service, one, two and
  three services (plan §6.1).
- **SIZE-10** — the null fold on a wide nullable record and on a list of strings (plan §6.3.2).
- **MICRO-08** rewritten: header write and parse over service records; the separate AAD image is gone
  (the associated data is the header bytes).
- **SEC-04** rewritten for the same reason.
- **The trace seam of plan §9.7, switched off.** The read cells of `ProfileMatrixBenchmarks` (WL-02)
  are measured twice in R6: before the diagnostics step and after it, same machine, nothing else
  running. The comparison is published in `internal/performance/`; a difference outside error stops
  the stage for the owner.
- **MICRO-19** — the dumper itself, informational: `Dump<T>` of DATA-01, DATA-04 and DATA-08, time and
  allocation per node. It gates nothing — diagnostics is not a hot path — but it states what a dump of
  a large frame costs, and it catches an accidental quadratic rendering.

## R8 — Generator ground — applied

- No new measurement. §18 notes that the conformance suite fixes the contracts a later generated path
  must match, so a later generated-versus-reflected comparison is like for like.

## R9 — Release

- Track A publication run on the release tag, per §29.1.
- §28 publication gate evaluated. Only after it may an adjective appear in a package description or a
  README.

---

# 3. Track B, once the format is final

- Measured after the release, on the `v1.0.0` tag. Nothing from Track B is measured against an
  intermediate stage.
- Built earlier: the adapters, the model variants and the capability probes are written after R6,
  once the format is final, on `benchmark/track-b-adapters` from `release/v1.0.0` [D9.25].

# 4. Run kinds — R0 — applied

- `RunKind.Partial` becomes `RunKind.Measurement` in the harness and the plan; the `Partial` state of a
  verified pair is a different concept and stays. The description of `RunKind.Baseline` becomes the
  record of a tagged revision — a release, a pre-release when the owner asks for one, a local tag
  before a rework [D9.26]. The documents are applied at once; the harness is changed after the
  `pre-rework` run, whose build it would otherwise alter.
- §5 roster re-verified on the day (RST-03).
- The buffer family (R3) makes FAIR-07 comparisons direct against MemoryPack and MessagePack-CSharp,
  which the pre-rework format could not enter.
- §6 capability tiers regenerated from the probes of §7.5 (TIER-04). V0 carries no phase and no
  references, so its tier membership is unchanged by the rework.
- §17 composed baselines: V1 with its phases against a hand-composed payload plus the same algorithms,
  as today.
