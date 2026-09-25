# Benchmark-Plan.md — changes required

**Applies to:** `internal/Benchmark-Plan.md` as of the NX-12 change.
**Scope rule unchanged:** benchmark work is read-only over `src/` and the contract; it writes only to
`benchmarks/`, the plan, and `internal/performance/`. The method stays in the `viper_bencher` skill.

Three parts: §1 is owed now; §2 is owed to the rework, stage by stage; §3 is the Track B order once the
format is final.

---

# 1. Owed now

The NX fixes changed three read and write paths the plan does not measure.

| New ID | Section | Checkpoint | Why |
|---|---|---|---|
| CMP-09 | §15 | incremental decompression for `Deflate` and `Brotli`: time and allocation against the previous single-buffer path, at every corpus size | NX-01 replaced the path; its cost on a legitimate payload is unknown, and a 64 KiB probe promoted once is a copy on every payload above 64 KiB |
| CMP-10 | §15 | the `MaxDecompressionRatio` check, isolated | expected to be negligible; a number makes it a fact |
| MICRO-13 | §18 | `CollectionCountCache` lookup, and the write-side fast path it enables for sets, frozen and immutable sets against the old materialising path | NX-02 removed an intermediate list per set written; it may be the largest incidental gain of the NX changes and nothing records it |
| MICRO-14 | §18 | the duplicate check after `Complete` — the count read on every container read | NX-02 put it on the read path of every collection |

Also owed now:

- **FAIR-07** — today it says Viper's lack of an `IBufferWriter` entry point "is reported as a fact
  rather than hidden". Keep the wording until R3, then rewrite it: the buffered family becomes directly
  comparable.
- **MICRO-09** — names `MeteredReadStream`, `MeteredWriteStream`, `WindowReadStream`. Valid until R2.

---

# 2. Owed to the rework, by stage

## R0 — the one run taken before the rework

- **Track A baseline on the `pre-rework` tag**, committed under `Baselines/pre-rework/`. This is the
  "before" of every stage below. It is taken even though Track A has not published a baseline on a
  release tag yet: the rework cannot prove any improvement without it, and every claim it would make
  otherwise is exactly what §28 forbids.
- §25 baselines and regression policy: add the rule that a rework stage compares against
  `pre-rework`, and a release compares against the previous release.
- New `PROF-09`: the same profile set on `pre-rework`, so the matrices are comparable cell by cell.

## R1 — Wire primitives on buffers

- MICRO-01 rewritten for `WireReader`/`WireWriter`, compared cell by cell with the `pre-rework`
  `ValueReader`/`ValueWriter` numbers.
- New `MICRO-15`: a positional record of many booleans and small integers — the path where a virtual
  call per byte dominates, so the gain from R1 is visible rather than averaged away.

## R2 — Pipeline on pooled buffers

- ALLOC-02 and ALLOC-03 re-attributed: `MemoryStream` and `ToArray` disappear from the phase list.
- ALLOC-05 rewritten: V1 no longer buffers differently from V0 in kind, only in the header; measure
  what remains.
- ALLOC-06: re-measure the LOH threshold crossings — pooled buffers should remove most of them for
  payloads under the pool's largest bucket.
- MICRO-09 replaced by the metering and windowing of the reader and writer.

## R3 — New entry points

- §10 workload catalog: add the buffer family (`IBufferWriter<byte>` in, `ReadOnlySpan<byte>` and
  `ReadOnlySequence<byte>` out), the pooled family (`SerializePooled`) and the pipe family.
- ALLOC-04: allocation per family, now including the buffer family.
- FAIR-07 rewritten (see §1).
- New `WL-15`: a non-seekable stream — the path that did not exist before.

## R4 — Typed engine

- New `ALLOC-09` … `ALLOC-16`: one checkpoint per row of `Rework-Plan.md` §11, each stating its target
  and the measured value; a target not met stays open with its number, never rounded to a claim.
- COLD-*: cold start against `pre-rework`. The typed engine builds more per type; this is where that
  cost appears. A regression is written up in `internal/performance/` before the stage closes.
- MICRO-04, MICRO-05, MICRO-06 rewritten for `FormatterCache<T>` and typed shapes.

## R5 — Algorithm contracts

- §15: add `ZLib`; §16: add `Crc64`, `XxHash64`, `XxHash3`, `XxHash128`, `ChaCha20Poly1305`; §8
  profiles gain one profile per new phase choice.
- MICRO-10 extended to every built-in.
- Note in §16: ChaCha20Poly1305 against AES-GCM depends on AES hardware support; the manifest already
  records the CPU, and the result must name it.

## R6 — Final wire format

- §14 size **re-measured entirely.** SIZE-02 (envelope cost), SIZE-03 (reference framing) and SIZE-05
  (per-string, per-element, per-null costs) are the direct evidence for W1–W6; each is published as
  `pre-rework` → R6.
- New `SIZE-09`: schema fingerprint cost, if D-2 is accepted.
- New `SIZE-10`: the positional null-flag cost on a wide nullable record — the data decision D-3 waits
  for.
- MICRO-08 rewritten for the flag-driven header and the extension area.

## R7 — V0 at parity

- §8 profiles: V0 with compression, V0 with checksum, V0 with AEAD, V0 with references.
- §6 capability tiers: V0's tier membership changes — it now carries the phases. The probes of §7.5
  regenerate it; nothing is edited by hand (TIER-04).
- DIFF-01 extended: V1 against V0 with the *same* phases, so the envelope's cost is isolated from the
  phases' cost.

## R8 — Generator ground

- No new measurement. §18 notes that the conformance suite fixes the plans a later generated path must
  match, so a later generated-versus-reflection comparison is like for like.

## R9 — Release

- Track A publication run on the release tag, per §29.1.
- §28 publication gate evaluated. Only after it: any adjective in a package description or README.

---

# 3. Track B, once the format is final

- Starts after R9, on the release tag. Nothing from Track B is measured against an intermediate stage.
- §5 roster re-verified on the day (RST-03).
- The buffer family (R3) makes FAIR-07 comparisons direct against MemoryPack and MessagePack-CSharp,
  which is the comparison the pre-rework format could not enter.
- §6 tiers regenerated from the probes after R7, because V0's capabilities changed.
- §17 composed baselines: V0 with phases becomes a candidate for the "hand-composed equivalent"
  comparison, since it is exactly payload plus phases with no envelope.
