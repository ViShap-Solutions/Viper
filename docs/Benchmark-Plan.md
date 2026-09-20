# ViShap.Viper — Benchmark Plan

**Baseline target:** the `v1.0.0` tag, measured after the release, then re-run per v1.x
**Status:** Realigned with the reworked architecture — B0 open, nothing measured yet
**Framework:** BenchmarkDotNet 0.15.8 · `net10.0`
**Scope:** the published performance of `src/`, measured against the current market and frozen as the v1.0.0 baseline
**Gates:** no release. This plan gates what may be *claimed* about performance, never whether a version ships *(§28)*
**Normative source:** `System-Contract.md` — a benchmark measures behavior the contract defines; it never defines behavior
**Method:** `.claude/skills/viper_bencher.md` — this document holds items and gates only

---

# 1. How to read this plan

This is a **checkpoint list**, worked through incrementally. It contains items, the rules that define the experiment, and gates. Investigation method, triage of a surprising number, profiling technique and reporting style live in the bencher skill.

Every item has the form:

```text
- [ ] ID — the fact that must be measured, or the harness property that must hold
```

Rules that govern the boxes:

- A box is ticked only when the measurement exists in a committed raw result file produced by a recorded run, not when the benchmark class compiles.
- A box is never ticked from a debug build, a machine on battery, or a `Dry` job. Publication numbers come from the recorded environment of §4.
- An item whose experiment cannot be made fair is marked **`BLOCKED (Qn)`** and recorded in §27, never silently dropped.
- An item is never deleted to make a gate pass. It is rewritten, split, or blocked.
- A benchmark measures the library; it never changes it. **Nothing in `src/` and nothing in `docs/System-Contract.md` is modified while working this plan.** An optimization, an extension point that would make something measurable, a suspected defect — each is written up as a proposal in `docs/performance/` (§27.4) and left for the repository owner to decide.
- A benchmark asserts nothing about correctness, and a measurement is never evidence that behavior is right.
- **No item here blocks a release.** Correctness and safety are settled before a version ships; performance is measured after it, against the tag. What an open item blocks is a performance claim — a number in the README, in a package description, in a release note or in an issue reply (§28).
- A baseline belongs to a revision, not to a date. Measuring the `v1.0.0` tag two months after it shipped produces the v1.0.0 record, because the revision, the lock and the manifest say so (§4, §25).

Measurement layers used below:

| Layer | Meaning |
|---|---|
| **B1** | **Comparative.** Viper against another library, inside one capability tier (§6). |
| **B2** | **Configuration.** Viper against Viper: format version, profile, options, member layout. |
| **B3** | **Component.** One mechanism measured on its own: directly, below the public API through an `InternalsVisibleTo` grant — formatters, value primitives, contracts, budgets, header, metered streams — or, where no grant exists, by subtracting two public measurements that differ in that mechanism alone. |
| **B4** | **System.** Process-level: cold start, parallel throughput, sustained load, working set. |

Result states, used in every published cell:

```text
Supported      the measurement ran and the value is real
Unsupported    the library cannot express this scenario without changing the logical data
Partial        the library expresses it with a documented semantic difference, named in the cell
Failed         the adapter ran and did not produce a correct round trip — a defect, not a number
Excluded       deliberately outside the matrix, with the reason in §5.4
```

`Unsupported` is not a zero, not an omitted row, and not an approximation.

---

# 2. Target benchmark project layout

```text
benchmarks/ViShap.Viper.Serialization.Benchmarks/
  Program.cs                    BDN switcher, plus --verify and --report modes
  Config/                       jobs, columns, exporters, diagnosers, validators
  Environment/                  environment manifest capture
  Schema/                       one logical schema description per dataset
  Models/
    Viper/                      the model as Viper consumes it
    MessagePack/                the same model, MessagePack attributes
    NerdbankMessagePack/        the same model, PolyType shapes
    MemoryPack/                 the same model, partial + [MemoryPackable]
    ProtobufNet/                the same model, protobuf-net contracts
    Orleans/                    the same model, [GenerateSerializer]
    SystemTextJson/             the same model plus a source-generated context
  DataSets/                     deterministic generators, one per DATA-xx
  Adapters/
    IBufferedSerializer.cs      byte[] in, byte[] out
    IStreamingSerializer.cs     Stream in, Stream out
    ICapabilityProbe.cs         what an adapter can actually do, proven at run time
    <one file per library>
  Capabilities/                 the probes that produce the capability matrix
  Verification/                 round-trip and equivalence checks run before any timing
  Suites/                       the benchmark classes of §10
  Reporting/                    raw results → published report and charts
  Baselines/                    frozen baseline packages, one per release
```

Constraints on the layout:

- The benchmark project references `src/` by project reference, is never packable, and changes nothing in it.
- Model variants are written per library, and all of them are checked against one `Schema/` description (§7.4), so "the same logical data" is a property the harness proves rather than a claim in prose.
- Comparative suites (B1) use the public surface of §3 alone, because that is what a consumer has. Component suites (B3) reach below it through the `InternalsVisibleTo` grant of §18.3, and are never mixed into a market table.
- No adapter for a competitor ever reaches into Viper internals; the two sides of a comparison always use the same kind of surface.

- [ ] LAY-01 — the layout above exists and the project builds in Release
- [ ] LAY-02 — `dotnet run -c Release -- --list flat` enumerates every suite of §10
- [ ] LAY-03 — the benchmark project is excluded from packing and from the CI verification job
- [ ] LAY-04 — a `--job Dry` smoke run of the whole switcher completes in CI on every push, proving the harness still runs; its numbers are never published

---

# 3. Stages and gates

Work proceeds stage by stage. A stage closes when every one of its items is `[x]` or `BLOCKED (Qn)`, its raw artifacts are committed under `Baselines/`, and its gate holds.

| Stage | Content | Layer | Gate |
|---|---|---|---|
| **B0** | Harness foundation — §2 layout, §4 environment lock, §7 fairness machinery, §8 profiles, §9 corpus, verification | — | Every dataset round-trips through every adapter and the capability matrix is generated from probes, before a single timing exists |
| **B1** | Viper against Viper — §8 profiles over §9 corpus | B2 | Every profile measured on every dataset it supports; the cost of each envelope feature separated from the cost of the payload |
| **B2** | Comparative core — §10 workloads inside the §6 tiers | B1 | Every mandatory library of §5.2 measured on every dataset of its tiers, or explicitly `Unsupported` |
| **B3** | Size — §14 payload size and envelope accounting | B1, B2 | Size recorded for every (adapter, dataset) pair, with no timing in the same table |
| **B4** | Algorithms — §15 compression, §16 checksum and encryption, §17 composed baselines | B2, B1 | Every phase measured separately and in combination; the protected envelope compared against a hand-composed equivalent |
| **B5** | Resources — §13 allocation and GC, §20 working set | B2, B1, B4 | Allocation attributed per phase; no published number from a run without `MemoryDiagnoser` |
| **B6** | Scaling — §19 curves over the §9 sweeps | B2, B1 | Each curve has at least five points and states where its slope changes |
| **B7** | System — §20 cold start, §21 concurrency, §22 soak | B4 | Cold start measured in fresh processes, concurrency on real threads, soak showing no unbounded growth |
| **B8** | Publication — §23 artifacts, §24 charts, §25 baseline, §26 reproduction | — | The report regenerates from the raw files with one command, and §28 is fully evaluated |
| **B9** | Component measurements — §18 | B3 | Every mechanism of §18.1 measured for time and allocation, with §18.2 agreeing within margins. Diagnostic only; never published as a market comparison |

B9 runs last on purpose: isolating a mechanism is only worth doing once the end-to-end picture says which mechanism matters. Its results are kept whatever they say — they are the per-component record a later version measures its own changes against.

---

# 4. Environment and version lock

A published number belongs to one machine, one runtime, one lock file and one source revision. Anything else is an anecdote.

The manifest is captured by the harness and committed with the results:

```text
OS name, build, architecture
CPU model, base/boost clock, physical and logical cores
RAM size and configured speed
power plan / CPU governor
hypervisor or bare metal
.NET SDK version, runtime version, tiered compilation and OSR state
GC mode (workstation/server, concurrent/non-concurrent), heap count
BenchmarkDotNet version
Viper source revision — commit sha, branch, dirty flag
every competitor package id and exact version
build configuration and optimization flags
UTC timestamp of the run
```

- [ ] ENV-01 — every field above is captured by code, not typed by hand
- [ ] ENV-02 — the manifest is written to `environment.json` beside the raw results of every run
- [ ] ENV-03 — every competitor package version is pinned exactly; no floating or wildcard version resolves in the benchmark project
- [ ] ENV-04 — a run refuses to start on a dirty working tree unless `--allow-dirty` is passed, and records the flag in the manifest
- [ ] ENV-05 — the publication run is executed twice on separate occasions on the same machine; any metric whose two means differ by more than their combined margin of error is published as unstable rather than as a single value
- [ ] ENV-06 — the GC mode behind the published comparative matrix is declared in the manifest and is the default a library consumer gets, with Server GC measured separately where §19 and §21 call for it
- [ ] ENV-07 — no other interactive workload runs during a publication run, and the harness records idle CPU before starting
- [ ] ENV-08 — the same manifest fields are recorded for the second-circle run of §5.3 when it happens on different hardware, and such results are never merged into the main matrix

---

# 5. Competitor roster

## 5.1 Selection criteria

A library belongs in the mandatory set when all of the following hold:

1. **It is a real alternative.** A .NET team choosing a serializer today would plausibly choose it for the same job.
2. **It is maintained.** A release within roughly the last two years, or an explicit in-support statement from its owner.
3. **It is comparable.** Its `Deserialize` produces a fully materialized object graph, so the measured operation is the same operation. A library whose read is a lazy view over the buffer measures something else and belongs in §5.4.
4. **It can run in its own documented best production mode** in this harness — source generator, compiled model, or whatever its owner recommends.
5. **Its capability claims can be proven by a probe** (§7.5), so the tier it appears in is a measured fact.

Market presence alone is not a criterion, and neither is the result. A library is never dropped because Viper loses to it.

## 5.2 Mandatory set

Versions verified against nuget.org on 2026-09-20. The latest publication date is recorded because criterion 2 is otherwise a matter of opinion.

| | Library | Version | Latest published | Why it is in the set |
|---|---|---|---|---|
| 1 | **ViShap.Viper** | this revision | — | The subject, in the profiles of §8 |
| 2 | **MemoryPack** | 1.21.4 | 2025-02-12 | The performance ceiling for .NET binary serialization: source-generated, layout-fixed, close to a memcpy for blittable shapes. It sets the "how fast could this possibly be" line |
| 3 | **MessagePack-CSharp** | 3.1.9 | 2026-09-17 | The de-facto industry standard for .NET binary serialization, in both its attributed and keyed modes |
| 4 | **protobuf-net** | 3.4.30 | 2026-09-16 | The schema-evolution reference in the POCO-first workflow, and the .NET face of Protocol Buffers |
| 5 | **Nerdbank.MessagePack** | 1.3.86 | 2026-09-17 | The closest capability peer: source-generated, version-tolerant, reference-preserving, depth-limited. Written by one of MessagePack-CSharp's two principal contributors as its successor, so it is the current state of the art for the feature set Viper targets |
| 6 | **Microsoft.Orleans.Serialization** | 10.3.1 | 2026-08-28 | Version tolerance, polymorphism and reference preservation as shipped by Microsoft in a production framework; the package is usable standalone |
| 7 | **System.Text.Json** | in-box `net10.0` | — | The baseline every .NET team already has, with a source-generated context and, in T3, `ReferenceHandler.Preserve`. Not a binary format: it is here to make the cost of the default choice visible, not to be beaten |

This set of seven was approved on 2026-09-20 and is the one the publication gate measures. A library joins or leaves it only by the owner's decision, recorded here.

- [ ] RST-01 — every mandatory library has an adapter that passes verification (§7.6) on every dataset of its tiers
- [ ] RST-02 — every mandatory library runs in its own documented best production mode, recorded per §7.3
- [ ] RST-03 — the version and publication date of every mandatory library is re-verified on the day of the publication run

## 5.3 Second circle — measured where it adds information, never gating

Run and published when the scenario makes them informative. They do not gate a stage. Google.Protobuf and Hyperion were placed here deliberately on 2026-09-20 rather than in §5.2: the first has a different workflow, the second is no longer maintained, and neither should decide whether a release stage closes.

| Library | Version | Latest published | Role |
|---|---|---|---|
| **Newtonsoft.Json** | 13.0.4 | 2025-09-16 | The honest "it handles everything" reference in T3: `$id`/`$ref` and type handling. Slow by construction; included so graph fidelity is not measured only among fast libraries |
| **Google.Protobuf** | 3.33.0 | 2025-10-15 | The canonical Protocol Buffers implementation. A different workflow — `.proto` and generated code rather than POCOs — so it is a wire-size and throughput reference on the flat datasets only |
| **Hyperion** | 0.12.2 | 2022-03-31 | Cycles, polymorphism and unattributed POCOs, the classic Akka.NET answer to T3. Fails criterion 2 today, kept as a datapoint because T3 has few members |
| **DataContractSerializer** | in-box | — | The in-box reference-preserving serializer (`preserveObjectReferences`). Second circle because a second text format adds format cost rather than information |

- [ ] RST-04 — each second-circle library is either measured and published with its role stated, or listed as not run for this release with the reason

## 5.4 Excluded, with the reason

An exclusion names the criterion it fails. No exclusion is a claim that a library is bad, and none is justified by a result.

| Library | Reason |
|---|---|
| **ZeroFormatter** | Fails criteria 2 and 3. Last published 2018-11-16 and superseded by its own author's MessagePack-CSharp and MemoryPack. Its deserialization is a lazy view: `Deserialize` returns almost immediately and the cost arrives when a property is touched, so a timed `Deserialize` would flatter it against every eager serializer here. The previous plan removed it for adapter trouble; these two are the real reasons |
| **Ceras** | Fails criterion 2. Last published 2019-08-23. Feature-wise the closest peer of its era — references, cycles, version tolerance — which is why its absence is recorded rather than passed over |
| **BinaryFormatter** | Cannot run. Removed from the runtime in .NET 9 and unsafe by design; there is no configuration in which it is a legitimate choice on `net10.0` |
| **FlatSharp / FlatBuffers, Cap'n Proto** | Fails criterion 3. Random-access zero-copy readers: "deserialize" is a pointer cast, and comparing it with an eager graph build measures a difference in programming model, not in speed |
| **XmlSerializer** | Fails criterion 3 for this corpus rather than in general: it cannot express dictionaries, interfaces, cycles or shared identity, so most of §9 would be `Unsupported`. `DataContractSerializer` covers the in-box XML answer in §5.3 with more of the corpus expressible |
| **Utf8Json, SpanJson, Jil** | Fail criterion 2, or serve a niche `System.Text.Json` now occupies |
| **Apache Avro (Chr.Avro), Apache Thrift, Bond** | Fail criterion 1 for this comparison: schema-registry and IDL-first ecosystems, chosen for interoperability with a platform rather than as a .NET serializer |

- [ ] RST-05 — every exclusion above names the criterion it fails
- [ ] RST-06 — the report reproduces this table, so a reader sees what was not measured and why

## 5.5 Reintroduction gate

A library leaves §5.4 only with all of:

- [ ] a working adapter in its own best mode;
- [ ] a model variant proven equivalent to the schema (§7.4);
- [ ] every capability probe of §7.5 executed and recorded;
- [ ] verification (§7.6) green on every dataset of its tiers;
- [ ] a fairness review recorded per §7.3 before any number is published.

---

# 6. Capability tiers

The spine of the comparison. Libraries are compared **inside a tier**, because a serializer that carries no metadata is not doing the same job as one that does, and putting the two in one table is the most common way a benchmark lies.

Viper appears in every tier, in the configuration that belongs to that tier.

| Tier | What the tier means | Viper configuration | Peers |
|---|---|---|---|
| **T0** | **Compact codec, closed world.** Both ends deployed together, schema fixed, no metadata on the wire, context supplied by the transport | V0 (B-P7) | MemoryPack, MessagePack, protobuf-net, Nerdbank.MessagePack, Google.Protobuf |
| **T1** | **Self-describing envelope.** The payload states how it was produced — algorithms, lengths, flags — and a reader the writer never configured can act on it | V1 default (B-P0) | Orleans, System.Text.Json. A format that describes its values but not its production — MessagePack signals its own LZ4 compression and nothing else, MemoryPack signals nothing — is placed by its probe, and the cell names exactly what the payload does and does not carry |
| **T2** | **Schema evolution.** A field added, removed or reordered on one side is tolerated by the other, and unknown data is skipped rather than fatal | V1 and V0 keyed contracts (B-P8) | protobuf-net, MessagePack keyed, Nerdbank.MessagePack, Orleans, System.Text.Json |
| **T3** | **Graph fidelity.** Shared references survive as identity, cycles are representable, polymorphic values restore their runtime type | V1 + `PreserveReferences` + `[BinaryUnion]` (B-P1) | Orleans, Nerdbank.MessagePack, System.Text.Json (`ReferenceHandler.Preserve`), and from §5.3 Newtonsoft.Json, Hyperion, DataContractSerializer |
| **T4** | **Protected envelope.** Integrity and confidentiality bound to the metadata rather than bolted on beside it | V1 + Crc32 + AES-256-GCM (B-P6) | No library peer. Measured against the **composed baselines** of §17 |

- [ ] TIER-01 — every published comparative table names its tier and contains only serializers whose probes place them in it
- [ ] TIER-02 — a T0 number is never shown against a T1–T4 number without the tier visible in the same view
- [ ] TIER-03 — Viper's tier configurations are the ones a consumer would use for that job, not a stripped configuration chosen to win
- [ ] TIER-04 — tier membership is produced by the §7.5 probes and regenerated on every run, never edited by hand

---

# 7. Fairness rules

These define the experiment. A number produced outside them is not publishable.

## 7.1 The same operation

- [ ] FAIR-01 — the buffered and streaming API families of §10 are compared only against their own kind: `byte[]`→`byte[]` against `byte[]`→`byte[]`, stream against stream
- [ ] FAIR-02 — `Deserialize` produces a fully materialized graph for every adapter; where a library offers a lazy read, the eager path is used and the choice is recorded
- [ ] FAIR-03 — a serializer instance, resolver, compiled model or generated context is created in `GlobalSetup`, outside the timed region, for every adapter alike
- [ ] FAIR-04 — the payload a deserialize benchmark consumes is produced in `GlobalSetup` by that same adapter, never inside the timed method
- [ ] FAIR-05 — every timed method returns its result or feeds a `Consumer`, so no adapter benefits from dead-code elimination another does not get
- [ ] FAIR-06 — no logging, console output, assertion or verification runs inside a timed region
- [ ] FAIR-07 — the output buffer strategy is equal in kind: every adapter returns a fresh `byte[]`, or every adapter writes into an equivalently pre-sized stream. A pooled or reused buffer is a separate, labelled family, and Viper's lack of an `IBufferWriter` entry point is reported as a fact rather than hidden by comparing against one

## 7.2 The same data

- [ ] FAIR-08 — one logical schema per dataset, in `Schema/`; every library's model variant is derived from it
- [ ] FAIR-09 — no field is removed, retyped or narrowed to make a library succeed; a library that cannot express the shape is `Unsupported` for that dataset
- [ ] FAIR-10 — collection semantics are preserved: a set stays a set, a dictionary stays a dictionary, and an ordering guarantee is not quietly dropped
- [ ] FAIR-11 — in T3 shared instances stay shared; a library that duplicates them is `Partial` with the duplication named, and its payload size is published with the duplication visible
- [ ] FAIR-12 — datasets are generated from a fixed seed and contain no wall-clock value, no machine culture and no environment dependency, so the same bytes are generated on every machine

## 7.3 The best configuration, attested

Each adapter carries a configuration rationale, published verbatim in the report:

```text
Library, version
Mode used (source generator / compiled model / reflection)
Options set, and the documentation or sample they come from
What was deliberately not enabled, and why
A known faster mode not used, and why it was not
```

- [ ] FAIR-13 — every adapter carries a rationale and the report prints it
- [ ] FAIR-14 — where a library ships a source generator or a compiled path as its recommended production mode, that mode is used: MessagePack and Nerdbank.MessagePack generated resolvers and shapes, MemoryPack generated formatters, protobuf-net a compiled `RuntimeTypeModel`, Orleans its generated codecs, System.Text.Json a `JsonSerializerContext`
- [ ] FAIR-15 — no competitor runs with a diagnostic, debug or validation feature its own documentation does not recommend for production
- [ ] FAIR-16 — where a library offers built-in compression (MessagePack LZ4, Nerdbank.MessagePack), it is measured both off and on, and the compressed variant is compared only against Viper's compressed profiles
- [ ] FAIR-17 — a result that contradicts the library's own published benchmarks by an order of magnitude is treated as a harness defect until it is explained in §27, never published as a finding
- [ ] FAIR-18 — Viper is held to the same rule: no profile a consumer would not use, and no limit relaxed below the shipping default to gain speed

## 7.4 Model equivalence

- [ ] FAIR-19 — a harness check proves, per dataset, that every library's model variant carries the same member set, member types and nesting as the schema; a mismatch fails the run before any benchmark executes
- [ ] FAIR-20 — where a model must differ structurally — a `.proto` message, a surrogate, a required `partial` — the difference is recorded per member and published beside the dataset

## 7.5 Capability probes

The tier table of §6 is generated, not asserted.

- [ ] FAIR-21 — each adapter implements probes for: envelope self-description, unknown-field tolerance, field removal, field reordering, reference identity, cycles, polymorphic dispatch, depth limiting, stream support, in-place population
- [ ] FAIR-22 — each probe is a real round trip whose outcome is `Supported`, `Partial` (with the difference named) or `Unsupported`; a documentation claim never sets a probe result
- [ ] FAIR-23 — `capabilities.csv` is regenerated on every run and is the only source of the §6 membership the report uses

## 7.6 Verification before timing

- [ ] FAIR-24 — before any suite runs, every (adapter, dataset) pair round-trips and the result is compared with the source by a structural comparison, not by reference equality
- [ ] FAIR-25 — a pair that fails verification is marked `Failed` and produces no timing at all
- [ ] FAIR-26 — verification is re-run after the suites, so a benchmark that corrupted shared state is caught
- [ ] FAIR-27 — the payload sizes recorded in §14 come from the same verified serialization, so size and timing can never describe different bytes

---

# 8. Viper configuration profiles under test

The configurations a consumer can build, each measured as itself. Built with the real builder surface *(§4.1)*, and every published number names the profile it belongs to.

| Profile | Configuration | Tier | Purpose |
|---|---|---|---|
| **B-P0** | `new BinarySerializer()` — V1, no algorithms | T1 | The default a consumer gets |
| **B-P1** | `Configure().PreserveReferences()` | T3 | The cost of reference framing |
| **B-P2** | `Configure().WithLimits(tight)` | — | The cost of accounting near a ceiling; not a performance mode |
| **B-P3** | `Configure().WithCompression(Deflate)` and `Brotli` | — | §15 |
| **B-P4** | `Configure().WithChecksum(Crc32)` | — | §16 |
| **B-P5** | `Configure().WithEncryption(Aes256Gcm, key)` | — | §16 |
| **B-P6** | Brotli + Crc32 + Aes256Gcm, and Deflate + Crc32 + Aes256Gcm | T4 | The full protected envelope |
| **B-P7** | `Configure().WithVersion(0).AllowV0Fallback()` | T0 | The compact codec |
| **B-P8** | `[BinaryContract]` keyed models under V1 and V0 | T2 | The keyed layout against the positional one |

- [ ] PROF-01 — every profile is measured on every dataset it supports, buffered and streaming
- [ ] PROF-02 — B-P0 against B-P7 isolates the V1 envelope from the payload, on one value under one layout *(§10.2, §22.8)*
- [ ] PROF-03 — B-P8 against B-P0 isolates the keyed layout from the positional one, under both format versions *(§14.2)*
- [ ] PROF-04 — B-P1 against B-P0 measures reference framing on a graph with no sharing at all, so the price of the option when it is not needed is visible *(§16)*
- [ ] PROF-05 — B-P2 against B-P0 shows what limit accounting costs; if the difference is not measurable, that is the result and it is published
- [ ] PROF-06 — the existing-instance and `ref` entry points are measured against their allocating counterparts *(§3.1)*
- [ ] PROF-07 — a serializer reused across operations is measured against one constructed per operation, so the per-call `StreamExtensions` path has a number *(§3.2)*
- [ ] PROF-08 — a union-typed dataset is measured against the same shape written under its concrete type, so the discriminator's cost is separated from polymorphic dispatch *(§15)*

---

# 9. Data corpus

Every dataset is deterministic, generated from a fixed seed, and belongs to the tiers listed. Target sizes are Viper V0 payload sizes and are targets, not promises.

| ID | Name | Shape | Target size | Tiers | What it exposes |
|---|---|---|---|---|---|
| **DATA-01** | TinyFlat | ~10 scalars, a short string, `Guid`, `DateTime` | 100–300 B | T0–T3 | Per-call fixed cost: framing, dispatch, header |
| **DATA-02** | MediumObject | 30–50 members, nested objects, enums, nullables | 1–10 KB | T0–T3 | The ordinary business object |
| **DATA-03** | RecordBatchSmall | 100–500 records of DATA-01 shape | ~10 KB | T0–T3 | Per-element cost at a realistic batch size |
| **DATA-04** | RecordBatchLarge | 10k–30k records | ~1 MB | T0–T2 | Throughput, and where allocation strategy starts to dominate |
| **DATA-05** | DictionaryHeavy | 10k+ entries, string keys, scalar values | ~1 MB | T0–T2 | Dictionary write and rebuild cost |
| **DATA-06** | DeepGraph | nesting at depths 5, 10, 25, 50, 200, 511 | small | T0–T3 | Depth accounting and recursion cost up to just below `MaxDepth` *(§5.1)* |
| **DATA-07** | UnicodeHeavy | multi-byte, CJK, emoji, combining sequences, with an ASCII twin | ~100 KB | T0–T2 | String encoding cost, and the honest ASCII-versus-UTF-8 difference |
| **DATA-08** | Incompressible | high-entropy strings and blobs | ~1 MB | T0, T4 | Compression's worst case, and what the phase costs when it buys nothing |
| **DATA-09** | HighlyCompressible | heavily repeated structure | ~1 MB | T0, T4 | Compression's best case |
| **DATA-10** | SharedReferenceDAG | one instance reachable by many paths | ~100 KB | T3 | Identity preservation, in time and in size |
| **DATA-11** | CyclicGraph | parent/child cycles | ~50 KB | T3 | The scenario that is impossible without reference support |
| **DATA-12** | PolymorphicBatch | a base type with 6–8 derived shapes | ~100 KB | T2, T3 | Discriminator cost and polymorphic dispatch |
| **DATA-13** | KeyedEvolution | a v1/v2 schema pair: fields added, removed, reordered | ~10 KB | T2 | Evolution cost on the writing side and on the skipping side |
| **DATA-14** | ByteBlob | one large `byte[]` inside a small object | 1 MB, 16 MB | T0–T2, T4 | Pure carrying cost, with traversal taken out of the picture |
| **DATA-15** | NumericArrays | `int[]`, `double[]`, `long[]`, 100k elements | ~1 MB | T0–T2 | Where a memcpy-shaped serializer legitimately wins, shown rather than avoided |
| **DATA-16** | StringTable | 50k short strings | ~1 MB | T0–T2 | String-dominated payloads and per-string overhead |
| **DATA-17** | NullSparse | 40 members, most of them null | ~1 KB | T0–T2 | Null framing cost against formats that omit absent fields |
| **DATA-18** | WideObject | 200 flat members | ~5 KB | T0, T2 | Member-plan cost, positional against keyed |
| **DATA-19** | CollectionZoo | one instance of each §23 container family | ~50 KB | T0–T2 | Breadth over the supported-type table in a single measurement |
| **DATA-20** | TimeAndNumerics | `DateTime`, `DateTimeOffset`, `decimal`, `Int128`, `BigInteger`, vectors, matrices | ~10 KB | T0–T2 | The families other serializers most often lack natively |

- [ ] DATA-00 — every generator is deterministic, culture-independent and time-independent, and produces byte-identical data on two machines
- [ ] DATA-21 — every dataset is generated, verified through every adapter of its tiers, and its actual size published beside its target
- [ ] DATA-22 — a dataset that misses its target by more than 2× is resized, or its target is corrected
- [ ] DATA-23 — no dataset requires a limit above `SerializationLimits.Default`; one that would is split *(§5)*
- [ ] DATA-24 — a dataset that a mandatory library cannot express is recorded as `Unsupported` for that library with the reason, and is not removed from the corpus

---

# 10. Workload catalog

The operations measured. Every workload runs per (adapter, dataset, profile) triple that its tier admits.

| ID | Workload | Layer | Notes |
|---|---|---|---|
| **WL-01** | Serialize to a new `byte[]` | B1, B2 | The primary comparative family |
| **WL-02** | Serialize to a pre-sized `MemoryStream` | B1, B2 | The streaming family; the stream is reset, never reallocated, inside the timed region |
| **WL-03** | Serialize to a non-seekable stream | B2 | Viper-specific: V0 with a keyed contract requires a seekable destination, which is measured as a supported refusal rather than a timing *(§10.2)* |
| **WL-04** | Deserialize from `byte[]` | B1, B2 | Payload produced in setup by the same adapter |
| **WL-05** | Deserialize from `MemoryStream` | B1, B2 | |
| **WL-06** | Round trip | B1, B2 | Serialize and deserialize in one timed operation |
| **WL-07** | Deserialize into an existing instance | B2 | `Unsupported` for most libraries; the cell says so *(§3.1)* |
| **WL-08** | Steady state over one serializer instance | B1, B2 | The default for every comparative suite |
| **WL-09** | First operation in a fresh process | B4 | §20 |
| **WL-10** | First operation for a type not seen before | B4 | Type-plan and formatter-cache construction, measured in a fresh process per type family |
| **WL-11** | Parallel throughput over a shared serializer | B4 | §21 |
| **WL-12** | Large payload throughput in MB/s | B1, B2 | DATA-04, DATA-14, DATA-15 |
| **WL-13** | Sustained load over a fixed duration | B4 | §22 |
| **WL-14** | Serialize the same graph with reference framing on and off | B2 | DATA-10, DATA-11 |
| **WL-15** | Read a payload whose schema differs from the model | B1 | DATA-13; the skipping side of evolution |

- [ ] WL-00 — every workload above has a suite, and every suite states which of WL-01…WL-15 it implements
- [ ] WL-16 — no suite mixes two workloads in one timed method
- [ ] WL-17 — every suite's parameterization is visible in the exported results as columns, not encoded in the method name

---

# 11. Metrics and statistics

## 11.1 Recorded per cell

```text
mean, median, standard deviation, margin of error, min, max
P95 where the suite is latency-shaped
operations per second
allocated bytes per operation
Gen0 / Gen1 / Gen2 collections per 1000 operations
payload bytes
throughput in MB/s for payload-dominated workloads
iteration count and warmup count actually used
```

Derived values published beside the absolutes, never instead of them:

```text
bytes per element
envelope overhead in bytes and as a percentage
compression ratio
encryption overhead in bytes
allocated bytes per payload byte
ratio to the tier's fastest entry, and ratio to Viper
```

## 11.2 Statistical discipline

- [ ] STAT-01 — every published suite declares its BDN job explicitly: warmup count, iteration count, invocation count, strategy, and no reliance on defaults that a BDN upgrade could change
- [ ] STAT-02 — `RunStrategy.Throughput` for sub-millisecond operations, `RunStrategy.Monitoring` for the large-payload and system suites, stated per suite
- [ ] STAT-03 — outlier handling is BenchmarkDotNet's, declared in the report; no measurement is removed by hand
- [ ] STAT-04 — the relative margin of error is at most 2% for micro-scale cells and 5% for heavy cells; a cell that cannot reach it is published with its margin and marked noisy
- [ ] STAT-05 — a comparison between two cells whose intervals overlap is reported as "no measurable difference", never as a winner
- [ ] STAT-06 — ratios are always shown next to the absolute values they come from
- [ ] STAT-07 — no published chart or table contains a number that is not present in a raw result file

---

# 12. Measurement validity

The failure modes that silently produce wrong benchmarks. Each is closed by construction and checked.

- [ ] VAL-01 — dead-code elimination: every timed method's result is consumed *(FAIR-05)*
- [ ] VAL-02 — setup leakage: nothing that can be hoisted out of the timed region is left inside it, and the reverse — no per-operation work is hoisted out for one adapter only
- [ ] VAL-03 — buffer reuse: an adapter that can write into a reused buffer does not compete against adapters that allocate, except in the labelled pooled family *(FAIR-07)*
- [ ] VAL-04 — state carry-over: no benchmark mutates a dataset, a payload array or an adapter's cache in a way a later iteration observes
- [ ] VAL-05 — first-iteration effects: tiered JIT promotion is completed by warmup for every adapter, and where it cannot be, the suite is moved to §20 and labelled cold
- [ ] VAL-06 — GC interference: allocation-heavy suites report Gen2 counts, and a suite whose result depends on a collection landing inside the measured window is re-shaped, not re-run until it looks stable
- [ ] VAL-07 — the harness itself is measured: an empty adapter that does nothing is benchmarked on the same datasets, and its number is the floor below which nothing is credible
- [ ] VAL-08 — the same benchmark, run twice in one process in a different order, produces the same result within its margin of error

---

# 13. Allocation and GC

Allocation is a first-class result here, not a footnote: the engine's structural barriers exist to bound allocation, so what they cost and what they prevent both belong in the report.

- [ ] ALLOC-01 — every comparative suite runs with `MemoryDiagnoser`, and allocation is published per operation
- [ ] ALLOC-02 — Viper's write allocation is attributed by phase — payload write, checksum, compression, encryption, header, final `byte[]` assembly — from the §18.1 component measurements, and cross-checked against the profile differentials of §18.2, never inferred from a total
- [ ] ALLOC-03 — the read path is attributed the same way: routing, header, decryption, decompression, payload read, materialization
- [ ] ALLOC-04 — allocation is reported for the streaming family separately from the `byte[]` family, because the `byte[]` entry point's copy is part of what it costs *(§3.1)*
- [ ] ALLOC-05 — the buffering V1 performs on write is measured against V0's straight-through write, so what the envelope costs in memory is visible *(§10.2)*
- [ ] ALLOC-06 — a large-payload suite reports Gen2 and LOH behavior, and the payload sizes at which allocations cross the LOH threshold are named
- [ ] ALLOC-07 — the tight-limits profile B-P2 is measured for allocation as well as time, so the accounting structures have a number
- [ ] ALLOC-08 — no allocation number is published from a run that also produced a timing in the same iteration when the diagnoser is known to perturb it; where it does, the timing comes from a separate run and the report says so

---

# 14. Payload size and envelope accounting

Size is recorded without timing, from the verified serialization of §7.6.

Recorded per (adapter, dataset, profile):

```text
payload bytes
envelope bytes (total minus the payload the same configuration would write without the envelope)
bytes per logical element
compressed bytes and ratio, where compression applies
encrypted bytes and the overhead over the plaintext
```

- [ ] SIZE-01 — every (adapter, dataset) pair has a size, or a result state explaining its absence
- [ ] SIZE-02 — the V1 envelope's fixed cost is stated in bytes, measured as B-P0 minus B-P7 on the same value *(§22.6, §22.8)*
- [ ] SIZE-03 — the reference-framing cost is stated in bytes, as B-P1 minus B-P0 on a graph with no sharing, and as the saving on DATA-10 where sharing exists *(§16)*
- [ ] SIZE-04 — the keyed layout's cost is stated in bytes against the positional layout on the same type, and against the evolution tolerance it buys *(§14.2)*
- [ ] SIZE-05 — the per-string, per-element and per-null framing costs are derived from DATA-16, DATA-15 and DATA-17 and published as a table
- [ ] SIZE-06 — a size comparison against a self-describing text format states that the comparison is between formats of different kinds
- [ ] SIZE-07 — compressed sizes are only compared with compressed sizes, and the algorithm is named in the cell
- [ ] SIZE-08 — encrypted sizes name the tag and nonce overhead separately from the ciphertext *(§13)*

---

# 15. Compression

Layer B2, over DATA-08, DATA-09, DATA-04, DATA-14 and DATA-16.

- [ ] CMP-01 — `Deflate` compress and decompress: time, ratio, allocation, at every corpus size *(§12)*
- [ ] CMP-02 — `Brotli` compress and decompress: the same *(§12)*
- [ ] CMP-03 — the no-compression path is measured on the same datasets, so the phase's cost is a difference rather than an estimate
- [ ] CMP-04 — the incompressible dataset shows what compression costs when it saves nothing, including the case where output exceeds input *(§12)*
- [ ] CMP-05 — compression throughput is published in MB/s of input, on both directions
- [ ] CMP-06 — decompression is measured against its declared uncompressed length, since the exact-length rule is part of the read path *(§12)*
- [ ] CMP-07 — where a competitor offers built-in compression, it appears in this section and nowhere else *(FAIR-16)*
- [ ] CMP-08 — a custom registered algorithm is measured once, so the extension path's overhead over a built-in is known *(§4.1)*

---

# 16. Checksum and encryption

- [ ] SEC-01 — `Crc32` over each corpus size: time, throughput, allocation *(§11)*
- [ ] SEC-02 — the checksum's share of a full V1 write and read, as a difference against B-P0
- [ ] SEC-03 — `Aes256Gcm` encrypt and decrypt: time, throughput MB/s, allocation, at every corpus size *(§13)*
- [ ] SEC-04 — the AAD image build, measured on its own (MICRO-08) and as the difference between a payload carrying long custom algorithm names and a key id and one carrying none *(§13.1)*
- [ ] SEC-05 — key resolution through `IKeyProvider` measured against a static key, including the per-operation copy `SecretKey` makes *(§13.2)*
- [ ] SEC-06 — the full protected envelope B-P6 against B-P0, per dataset, so the price of protection is one number a reader can act on
- [ ] SEC-07 — the order of phases is the contract's, and no benchmark measures a reordered pipeline *(§10.1)*
- [ ] SEC-08 — hardware-accelerated AES is reported as present or absent in the environment manifest, since it moves this section by an order of magnitude

---

# 17. Composed baselines — the honest T4 comparison

No competitor ships an authenticated envelope, so T4 is not comparable library-to-library. It is compared against what an application would otherwise have to build.

For each of MessagePack, MemoryPack and protobuf-net, the harness composes:

```text
serialize with the competitor
→ CRC-32 over the payload      (System.IO.Hashing)
→ AES-256-GCM over the payload (System.Security.Cryptography)
→ a minimal hand-written header carrying lengths, nonce, tag and checksum
```

- [ ] COMP-01 — the composed baseline exists for each of the three libraries and passes verification
- [ ] COMP-02 — the composed header carries the same information the V1 header does, so the size comparison is between equivalents *(§11)*
- [ ] COMP-03 — the composed baseline is measured on the same datasets as B-P6, with the same statistics
- [ ] COMP-04 — the report states plainly what the composed baseline does **not** provide: no authenticated metadata binding, no algorithm negotiation, no key id, no limits, no budget accounting *(§13.1, §5)*
- [ ] COMP-05 — where Viper is slower than a composed baseline, the difference is published with the features that account for it named, and never hidden behind a feature argument alone

---

# 18. Component measurements — B3

Diagnostic, never a market comparison. They exist for two readers: the engineer explaining a B1/B2 result, and the next version, which needs a per-component record of this one to know what a change actually improved. Each item names the end-to-end number it explains.

## 18.1 Microbenchmarks, below the public API

Measured directly on the internal type that owns the mechanism, through the grant of §18.3.

- [ ] MICRO-01 — `ValueWriter`/`ValueReader` primitives: varint, fixed-width, string, blob, on both directions *(§22.1)*
- [ ] MICRO-02 — `ElementCount` validation and budget charging over a hot loop *(§6)*
- [ ] MICRO-03 — depth scope entry and exit, and the unwind on the exceptional path *(§5.1)*
- [ ] MICRO-04 — `TypeContract` construction for a cold type, and lookup once cached, positional and keyed *(§14)*
- [ ] MICRO-05 — `FormatterRegistry.Resolve` for a claimed type and for a member-encoded one
- [ ] MICRO-06 — one formatter per shape family: scalar, sequence, map, composite
- [ ] MICRO-07 — reference identity tracking: registration, lookup, scope exit, at several sharing densities *(§16)*
- [ ] MICRO-08 — V1 header write and parse, including the AAD image build *(§11, §13.1)*
- [ ] MICRO-09 — `MeteredReadStream`, `MeteredWriteStream` and `WindowReadStream` against the bare stream *(§7)*
- [ ] MICRO-10 — the algorithm primitives over spans, outside the pipeline: `Deflate`, `Brotli`, `Crc32`, `Aes256Gcm` *(§12, §13)*
- [ ] MICRO-11 — allocation is recorded for every microbenchmark above, not only time, since the per-component allocation record is what a later version compares against
- [ ] MICRO-12 — every microbenchmark names the end-to-end measurement it explains; one that explains nothing is deleted

## 18.2 Differentials, through the public API

The same mechanisms seen from outside, by subtracting two public measurements that differ in one thing alone. They are not a substitute for §18.1 — they are its cross-check, and they are what remains measurable if a grant is ever withdrawn.

- [ ] DIFF-01 — the V1 envelope: B-P0 minus B-P7 on one value, with and without custom algorithm names and a key id *(§11, §22.8)*
- [ ] DIFF-02 — each algorithm phase: B-P3, B-P4, B-P5 against B-P0 *(§12, §13)*
- [ ] DIFF-03 — the member plan: DATA-18 positional against keyed, first use against steady state *(§14)*
- [ ] DIFF-04 — reference framing: B-P1 against B-P0 by sharing density *(§16, SCALE-07)*
- [ ] DIFF-05 — limit accounting: B-P2 against B-P0 *(§5)*
- [ ] DIFF-06 — stream metering: the stream family against the `byte[]` family on the same value *(§7)*
- [ ] DIFF-07 — each differential agrees with the §18.1 measurement of the same mechanism within their combined margins of error; a disagreement is investigated before either number is published

## 18.3 Access

The component suites see internals through `src/ViShap.Viper.Serialization/Properties/AssemblyInfo.QA.cs`, which names `ViShap.Viper.Serialization.Benchmarks`. The grant is the owner's to give; this plan does not edit `src/` (§1).

- [x] MICRO-00 — the grant exists for `ViShap.Viper.Serialization.Benchmarks`, added by the repository owner on 2026-09-20, and an internal type resolves from the benchmark project in a Release build *(Q1)*
- [ ] MICRO-13 — the grant is the only thing the component suites need from `src/`; nothing else is added, made public, or made `internal` for their sake, and a measurement that would need more is a proposal in `docs/performance/` (§27.4)

---

# 19. Scaling curves

A single size is a point; a curve is a property. Each curve has at least five points and states where the slope changes.

- [ ] SCALE-01 — element count: 1, 10, 100, 1k, 10k, 100k records — time, allocation, bytes
- [ ] SCALE-02 — payload size: 1 KB, 64 KB, 1 MB, 16 MB, 64 MB — throughput MB/s, against `MaxPayloadBytes` *(§5)*
- [ ] SCALE-03 — depth: 1, 5, 25, 100, 500 — time and allocation per level *(§5.1)*
- [ ] SCALE-04 — member count: 5, 20, 50, 100, 200 members — positional against keyed *(§14)*
- [ ] SCALE-05 — string length: 8 B, 256 B, 4 KB, 64 KB, 1 MB — ASCII against multi-byte
- [ ] SCALE-06 — dictionary size: 10, 100, 1k, 10k, 100k entries
- [ ] SCALE-07 — sharing density in a DAG: 0%, 10%, 50%, 90% shared nodes — B-P1 time and size against B-P0 *(§16)*
- [ ] SCALE-08 — every curve is published with both axes' units and states whether the growth it shows is linear
- [ ] SCALE-09 — the large end of SCALE-02 is run under Server GC as well, and both are published *(ENV-06)*

---

# 20. Cold start and first use

Cold start is a genuinely fresh process. A first in-process call after a warm-up is not cold and is never labelled so.

```text
fresh process → load the serializer → perform the first operation → record → exit
```

- [ ] COLD-01 — fresh-process first serialize, per adapter, per dataset family — the measurement is the process's own timing, aggregated over many process launches
- [ ] COLD-02 — fresh-process first deserialize, the same way
- [ ] COLD-03 — process startup itself is measured with an empty adapter and subtracted, with both numbers published
- [ ] COLD-04 — the first operation for a type never seen before, in a process already warm for another type, so type-plan construction is separated from process startup *(WL-10)*
- [ ] COLD-05 — the number of operations needed to reach steady state is published per adapter, as the point where the running mean stops moving beyond its margin of error
- [ ] COLD-06 — managed memory and working set after the first operation are recorded per adapter
- [ ] COLD-07 — no cold measurement shares static state with a previous case, and the harness proves it by launching one process per measurement
- [ ] COLD-08 — where a library's cold cost is dominated by its source generator having removed the work entirely, the report says so rather than presenting the number alone

---

# 21. Concurrency

Real parallelism, on a shared serializer, since the contract makes the serializer usable from several threads and the caches are shared.

- [ ] PAR-01 — throughput at 1, 2, 4, 8 and `Environment.ProcessorCount` threads over one shared serializer, per dataset family
- [ ] PAR-02 — scaling efficiency against the single-thread number, published as a percentage
- [ ] PAR-03 — per-operation latency distribution under parallel load, including P95 and P99
- [ ] PAR-04 — allocation per operation under parallel load against the single-thread figure
- [ ] PAR-05 — first use of a type from several threads at once, so contention over the shared type-plan cache is visible
- [ ] PAR-06 — the same measurements under Server GC, published beside the workstation figures
- [ ] PAR-07 — competitors are measured under the same thread counts, or marked `Unsupported` where a library requires an instance per thread — and that requirement is itself the result

---

# 22. Stability and sustained load

- [ ] SOAK-01 — a fixed-duration run per profile, at least 10 minutes, recording throughput over time
- [ ] SOAK-02 — managed heap size and working set sampled throughout, published as a curve
- [ ] SOAK-03 — no unbounded growth in either curve; growth that appears is investigated before publication and recorded in §27
- [ ] SOAK-04 — the same run with encryption and compression enabled, since those phases own the temporary buffers *(§13.2)*
- [ ] SOAK-05 — throughput at the end of the run is within the margin of error of throughput at the start, or the difference is explained

---

# 23. Reporting artifacts

A run produces raw artifacts and a report generated from them. Nothing in the report is typed by hand.

```text
Baselines/<revision>/
  environment.json          §4 manifest
  capabilities.csv          §7.5 probe results
  verification.csv          §7.6 outcomes, per (adapter, dataset)
  results.csv               every cell, raw
  results.json              the BDN export
  payload-sizes.csv         §14
  memory.csv                §13
  components.csv            §18 per-mechanism time and allocation
  configuration.md          §7.3 rationales, verbatim
  report.md                 the generated report
  report.html               the same, with charts
  charts/                   §24
  manifest.md               what ran, when, on what, against which packages
```

- [ ] REP-01 — every artifact above is produced by one command
- [ ] REP-02 — the report regenerates byte-identically from the raw files, so the raw files are the record and the report is a view
- [ ] REP-03 — the generator's own version and the revision it ran on are recorded in the manifest
- [ ] REP-04 — a cell with no number carries its result state and its reason, never a blank
- [ ] REP-05 — the report opens with the tier table, the roster, and the exclusions, before any number
- [ ] REP-06 — the report never reduces the outcome to a single winner
- [ ] REP-07 — every claim in the README or the package description that cites a performance figure cites a cell in a committed raw file

---

# 24. Charts

Generated from the raw files, never drawn by hand.

- [ ] CHT-01 — time per operation, per tier, per dataset
- [ ] CHT-02 — operations per second
- [ ] CHT-03 — allocated bytes per operation
- [ ] CHT-04 — Gen0/Gen1/Gen2 per 1000 operations
- [ ] CHT-05 — payload size, per tier
- [ ] CHT-06 — compression ratio and compression throughput
- [ ] CHT-07 — encryption throughput, and the protected envelope against the composed baseline
- [ ] CHT-08 — cold start against warm steady state
- [ ] CHT-09 — the scaling curves of §19
- [ ] CHT-10 — parallel scaling efficiency
- [ ] CHT-11 — every chart labels its units, states whether lower or higher is better, and names the tier
- [ ] CHT-12 — a normalized chart, where used, is secondary to the absolute one and never replaces it
- [ ] CHT-13 — the chart layer contains no value that is absent from the raw results

---

# 25. Baselines and regression policy

A baseline belongs to one source revision, runtime, hardware, package lock and BenchmarkDotNet version. Later releases are compared only against a compatible baseline environment.

The run against the `v1.0.0` tag becomes the frozen baseline, whenever it happens. It is never regenerated afterwards: a rebuilt baseline agrees with whatever the code became, and so proves nothing.

Every later v1.x is measured the same way, against its own tag, and published as a delta against the baseline it is compatible with. That is what this plan is for once v1.0.0 has shipped: not a gate in front of a release, a record behind each one.

- [ ] BASE-01 — the baseline package is committed under `Baselines/v1.0.0/` with every artifact of §23, produced from a checkout of the `v1.0.0` tag
- [ ] BASE-06 — each subsequent v1.x run is committed under its own `Baselines/<tag>/`, with the delta against the previous one and an entry in §27.3 for every threshold it crosses
- [ ] BASE-02 — a comparison tool reports the delta of a new run against a baseline, cell by cell, with margins of error
- [ ] BASE-03 — a comparison against an incompatible environment is refused rather than printed
- [ ] BASE-04 — review thresholds, as triggers for investigation and not automatic failures:
  - a throughput regression beyond 10% that is stable across two runs;
  - an allocation increase beyond 10%;
  - any payload-size increase at all, since size is a wire property and a change may be a compatibility break;
  - a statistically significant cold-start regression;
  - a Gen2 or LOH increase that was not there before
- [ ] BASE-05 — a crossed threshold is recorded in §27 with its cause; it informs the next version, and never holds a release hostage

---

# 26. Reproduction

- [ ] REPRO-01 — a documented command sequence reproduces the full publication run from a clean clone
- [ ] REPRO-02 — the document states the hardware, the machine state and the expected duration
- [ ] REPRO-03 — a partial run is possible per stage and per suite, with the same commands
- [ ] REPRO-04 — the instructions state what will differ on other hardware and what should not
- [ ] REPRO-05 — reproduction instructions are committed with the baseline, not only in this plan

---

# 27. Findings

Everything this plan discovers is **recorded**. Nothing it discovers is acted on in `src/`.

## 27.1 Observations about the library

Behavior seen while measuring that the owner may want to know about — an unexpected cost, a surprising allocation, a result that does not match what the contract led one to expect. Each entry names the measurement that showed it and points at the proposal in `docs/performance/` where it is written up.

*(none yet)*

## 27.2 Open questions

An experiment that cannot be made fair, a scenario the plan does not say how to measure, a tier a library sits between, or a decision that belongs to the owner because it touches something this plan may not change. The item is marked `BLOCKED (Qn)` until the owner decides.

*(none open)*

### Decided

| | Question | Decision | Unblocked |
|---|---|---|---|
| **Q1** | The component suites of §18.1 measure internal mechanisms — value primitives, budgets, contracts, header, metered streams — and the benchmark assembly could not see them. Adding the grant is a change in `src/`, which this plan does not make (§1) | Granted by the repository owner on 2026-09-20: `AssemblyInfo.QA.cs` now names `ViShap.Viper.Serialization.Benchmarks`, verified by resolving an internal type from the benchmark project in a Release build. Nothing a consumer sees changes, and v1.0.0 gets the per-component record a later version measures its optimizations against | MICRO-00…MICRO-13, ALLOC-02, ALLOC-03, SEC-04 |

## 27.3 Results register

The place where an unflattering result is recorded rather than argued with. Each entry names the cell, the gap, the cause if it is known, and whether the owner accepted it for v1.0.0 or opened a proposal against it.

*(none yet)*

## 27.4 Proposals — `docs/performance/`

The one output this plan produces besides measurements. A proposal is a document, not a change: it describes what was measured, what it suggests, what it would cost and what it would risk, and it ends where the owner's decision begins.

One file per proposal, `PERF-nn-<slug>.md`, indexed by `docs/performance/README.md`, in the shape that file defines.

- [ ] PROP-01 — every optimization idea arising from a measurement is a proposal, and no `src/` file is edited by this plan
- [ ] PROP-02 — every proposal cites the cells that motivate it, with their margins of error, so it can be re-evaluated against the raw results
- [ ] PROP-03 — a proposal that would change the wire, the public surface or a security boundary says so in its first paragraph
- [ ] PROP-04 — a proposal states what it does **not** know: what was not measured, and what would have to be measured before acting
- [ ] PROP-05 — proposals are never merged into the report as if they were results, and the report links to them as open questions

---

# 28. Publication gate

Nothing here decides whether a version ships. It decides whether the project may say anything about its own speed, allocation or payload size — in the README, in a package description, in a release note, in an issue reply or in a chart.

Until it is green, the answer to "how fast is it?" is "not measured yet", and that is an acceptable answer.

Checked only when a committed raw result proves it.

## Harness

- [ ] The benchmark project builds and runs in Release, and its `Dry` smoke run passes in CI.
- [ ] Every mandatory library of §5.2 has an adapter in its documented best mode, with a published rationale.
- [ ] Every exclusion in §5.4 names the criterion it fails.
- [ ] The capability matrix is generated from probes, and every tier table matches it.
- [ ] Every (adapter, dataset) pair is verified before and after the suites.

## Coverage

- [ ] Every Viper profile of §8 is measured on every dataset it supports.
- [ ] Every workload of §10 has results or a stated result state, per tier.
- [ ] Payload sizes exist for every pair, independently of timing.
- [ ] Compression, checksum and encryption are measured separately and in combination.
- [ ] The composed baselines of §17 exist for all three libraries.
- [ ] Cold start is measured in fresh processes, concurrency on real threads, and the soak run shows no unbounded growth.
- [ ] Every mechanism of §18 has a time and an allocation figure, and the component record is committed with the baseline.

## Honesty

- [ ] No competitor runs in a degraded configuration, and no Viper profile is a configuration a consumer would not use.
- [ ] No result is published that contradicts a library's own benchmarks by an order of magnitude without an explanation in §27.
- [ ] Every cell without a number states why.
- [ ] Every unfavourable result is in §27.3.
- [ ] No published figure lacks a raw result file behind it.

## Boundary

- [ ] `src/` and `docs/System-Contract.md` are untouched by this plan's work: the diff of the benchmark effort contains the benchmark project, this plan and `docs/performance/`, and nothing else. The `InternalsVisibleTo` grant of §18.3, if it exists, was made by the owner.
- [ ] Every optimization idea the measurements produced exists as a proposal in `docs/performance/`, and none of them was applied.
- [ ] Comparative suites use the public surface alone; internal access appears only in the component suites, and no competitor adapter uses it.
- [ ] The component record of §18 is committed with the baseline, so a later version can measure a change against this one.

## Publication

- [ ] The report and charts regenerate from the raw files with one command.
- [ ] The environment manifest, the package lock and the source revision are committed with the results.
- [ ] The v1.0.0 baseline is frozen under `Baselines/v1.0.0/`.
- [ ] Reproduction instructions are committed and have been followed once, from a clean clone, by someone other than the author of the run.

---

# 29. Definition of completion

Benchmarking is complete when the raw results, the environment manifest, the capability matrix, the baselines and the generated charts exist together, trace to one source revision and one locked environment, and every cell is either a number or a stated reason.

The report presents measurements. It does not reduce the outcome to a winner, and it does not omit a result because of what the result is.

Complete for a version, not for the project: the plan is worked again against each v1.x tag, and what changes between runs is the corpus only where a new capability needs one — never the fairness rules, and never mid-flight.
