# ViShap.Viper — Benchmark Plan

**Class: plan.** The post-release measurement checklist. It gates no release and defines no behavior.

**Baseline target:** the `v1.0.0` tag, measured after the release, then re-run per v1.x
**Status:** Realigned with the reworked architecture — B0 open, nothing measured yet
**Framework:** BenchmarkDotNet 0.15.8 · `net10.0`
**Scope:** the published performance of `src/`, measured against the current market and frozen as the v1.0.0 baseline
**Gates:** no release. This plan gates what may be *claimed* about performance, never whether a version ships *(§28)*
**Normative source:** `System-Contract.md` — a benchmark measures behavior the contract defines; it never defines behavior
**Contents:** items, the rules that define each experiment, and gates. Nothing else — no method, and no result
**References:** a bare `§n` is a section of this plan; a section of the contract is written `Contract §n`

---

# 1. How to read this plan

This is a **checkpoint list**, worked through incrementally. It contains items, the rules that define the experiment, and gates. Investigation method, triage of a surprising number, profiling technique and reporting style are not in this document.

Every item has the form:

```text
- [ ] ID — the fact that must be measured, or the harness property that must hold
```

Rules that govern the boxes:

- A box is ticked only when the measurement exists in a committed raw result file produced by a recorded run, not when the benchmark class compiles.
- A box is never ticked from a debug build, a machine on battery, or a `Dry` job. Publication numbers come from the recorded environment of §4.
- An item whose experiment cannot be made fair is marked **`BLOCKED (Qn)`** and recorded in §27, never silently dropped.
- An item is never deleted to make a gate pass. It is rewritten, split, or blocked.
- A benchmark measures the library; it never changes it. **Nothing in `src/` and nothing in `internal/System-Contract.md` is modified while working this plan.** An optimization, an extension point that would make something measurable, a suspected defect — each is written up as a proposal in `internal/performance/` (§27.4) and left for the repository owner to decide.
- A benchmark asserts nothing about correctness, and a measurement is never evidence that behavior is right.
- **No item here blocks a release.** Correctness and safety are settled before a version ships; performance is measured after it, against the tag. What an open item blocks is a performance claim — a number in the README, in a package description, in a release note or in an issue reply (§28).
- A baseline belongs to a revision, not to a date. Measuring the `v1.0.0` tag two months after it shipped produces the v1.0.0 record, because the revision, the lock and the manifest say so (§4, §25).

Measurement layers used below:

| Layer | Meaning |
|---|---|
| **L1** | **Comparative.** Viper against another library, inside one capability tier (§6). |
| **L2** | **Configuration.** Viper against Viper: format version, profile, options, member layout. |
| **L3** | **Component.** One mechanism measured on its own: directly, below the public API through an `InternalsVisibleTo` grant — formatters, value primitives, contracts, budgets, header, metering — or, where no grant exists, by subtracting two public measurements that differ in that mechanism alone. |
| **L4** | **System.** Process-level: cold start, parallel throughput, sustained load, working set. |

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
    IBufferWriterSerializer     IBufferWriter<byte> in; ReadOnlySpan<byte> and ReadOnlySequence<byte> out
                                (declared in ISerializerAdapter.cs with the two above; an adapter without
                                buffer entry points does not implement it and is Unsupported in the family)
    ICapabilityProbe.cs         what an adapter can actually do, proven at run time
    <one file per library>
  Capabilities/                 the probes that produce the capability matrix
  Verification/                 round-trip and equivalence checks run before any timing
  Suites/                       the benchmark classes of §10
    Components/                 the §18.1 component suites, kept apart from every market table
  Reporting/                    raw results → published report and charts
  Baselines/                    frozen baseline packages, one per measured tag
  Measurements/                 measurements, one directory per run
  reproduction/                 the containerised check of §26, run from a base image
```

Constraints on the layout:

- The benchmark project references `src/` by project reference, is never packable, and changes nothing in it.
- Model variants are written per library, and all of them are checked against one `Schema/` description (§7.4), so "the same logical data" is a property the harness proves rather than a claim in prose.
- Comparative suites (L1) use the public surface of §3 alone, because that is what a consumer has. Component suites (L3) reach below it through the `InternalsVisibleTo` grant of §18.3, and are never mixed into a market table.
- No adapter for a competitor ever reaches into Viper internals; the two sides of a comparison always use the same kind of surface.

- [x] LAY-01 — the layout above exists and the project builds in Release
- [x] LAY-02 — `dotnet run -c Release -- --list flat` enumerates every suite of §10
- [x] LAY-03 — the benchmark project is never packable, so no part of it can reach a released package

---

# 3. Stages and gates

Work proceeds stage by stage. A stage closes when every one of its items is `[x]` or `BLOCKED (Qn)`, its raw artifacts are committed under `Baselines/`, and its gate holds.

A stage belongs to one track or to both (§29). A stage that contains Track B items cannot close while only Track A has been worked, however many of its own checkpoints are ticked — the boxes close one at a time, the stage closes when all of them are.

| Stage | Content | Layer | Closes on | Gate |
|---|---|---|---|---|
| **B0** | Harness foundation — §2 layout, §4 environment lock, §7 fairness machinery, §8 profiles, §9 corpus, verification | — | A + B | Every dataset round-trips through every adapter and the capability matrix is generated from probes, before a single timing exists |
| **B1** | Viper against Viper — §8 profiles over §9 corpus | L2 | **A** | Every profile measured on every dataset it supports; the cost of each envelope feature separated from the cost of the payload |
| **B2** | Comparative core — §10 workloads inside the §6 tiers | L1 | **B** | Every mandatory library of §5.2 measured on every dataset of its tiers, or explicitly `Unsupported` |
| **B3** | Size — §14 payload size and envelope accounting | L1, L2 | A + B | Size recorded for every (adapter, dataset) pair, with no timing in the same table |
| **B4** | Algorithms — §15 compression, §16 checksum and encryption, §17 composed baselines | L2, L1 | A + B | Every phase measured separately and in combination; the protected envelope compared against a hand-composed equivalent |
| **B5** | Component measurements — §18 | L3 | **A** | Every mechanism of §18.1 measured for time and allocation, with §18.2 agreeing within margins. Diagnostic only; never published as a market comparison |
| **B6** | Resources — §13 allocation and GC, §20 working set | L2, L1, L4 | A + B | Allocation attributed per phase from B5, and no published number from a run without `MemoryDiagnoser` |
| **B7** | Scaling — §19 curves over the §9 sweeps | L2, L1 | **A** | Each curve has at least five points and states where its slope changes |
| **B8** | System — §20 cold start, §21 concurrency, §22 soak | L4 | A + B | Cold start measured in fresh processes, concurrency on real threads, soak showing no unbounded growth |
| **B9** | Publication — §23 artifacts, §24 charts, §25 baseline, §26 reproduction | — | A + B | The report regenerates from the raw files with one command, and §28 is fully evaluated |

The order is the dependency order: a stage needs only stages before it. B5 follows the end-to-end stages because isolating a mechanism is only worth doing once the end-to-end picture says which mechanism matters, and it precedes B6 because attributing allocation to a phase is reading B5's figures. Its results are kept whatever they say — they are the per-component record a later version measures its own changes against.

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

- [x] ENV-01 — every field above is captured by code, not typed by hand — `Environment/EnvironmentManifest.cs`, `--manifest`
- [x] ENV-02 — the manifest is written to `environment.json` beside the raw results of every run
- [ ] ENV-03 — every competitor package version is pinned exactly; no floating or wildcard version resolves in the benchmark project
- [x] ENV-04 — a run refuses to start on a dirty working tree unless `--allow-dirty` is passed, and records the flag in the manifest
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
| **T2** | **Schema evolution.** A field added, removed or reordered on one side is tolerated by the other, and unknown data is skipped rather than fatal | V1 and V0 over keyed models (B-P0, B-P7) | protobuf-net, MessagePack keyed, Nerdbank.MessagePack, Orleans, System.Text.Json |
| **T3** | **Graph fidelity.** Shared references survive as identity, cycles are representable, polymorphic values restore their runtime type | V1 + `PreserveReferences` + `[BinaryUnion]` (B-P1) | Orleans, Nerdbank.MessagePack, System.Text.Json (`ReferenceHandler.Preserve`), and from §5.3 Newtonsoft.Json, Hyperion, DataContractSerializer |
| **T4** | **Protected envelope.** Integrity and confidentiality bound to the metadata rather than bolted on beside it | V1 + CRC-32 + AES-256-GCM (B-P6) | No library peer. Measured against the **composed baselines** of §17 |

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
- [ ] FAIR-07 — the output buffer strategy is equal in kind, family by family: in the `byte[]` family every adapter returns a fresh `byte[]`; in the streaming family every adapter writes into an equivalently pre-sized stream; in the buffer family every adapter writes into the same reset `IBufferWriter<byte>` and reads from a span or a sequence, which makes Viper directly comparable with the buffer entry points of MemoryPack and MessagePack-CSharp. A pooled or reused result — Viper's `PooledPayload` among them — is a separate, labelled family and never competes against an allocating one *(Contract §3.1, §3.2; rewritten in R3, when the buffer entry points arrived)*

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

- [x] FAIR-24 — before any suite runs, every (adapter, dataset) pair round-trips and the result is compared with the source by a structural comparison, not by reference equality — `Verification/`, `--verify`: 270 pairs, 0 failed
- [x] FAIR-25 — a pair that fails verification is marked `Failed` and produces no timing at all; a cycle without reference framing is `Unsupported` and a lost identity is `Partial`, never `Failed`
- [ ] FAIR-26 — verification is re-run after the suites, so a benchmark that corrupted shared state is caught
- [ ] FAIR-27 — the payload sizes recorded in §14 come from the same verified serialization, so size and timing can never describe different bytes

---

# 8. Viper configuration profiles under test

The configurations a consumer can build, each measured as itself. Built with the real builder surface *(Contract §4.1)*, and every published number names the profile it belongs to. The keyed layout is not among them: it is a property of the model rather than of the configuration, so it is measured under B-P0 and B-P7 over the keyed datasets of §9.

| Profile | Configuration | Tier | Purpose |
|---|---|---|---|
| **B-P0** | `new BinarySerializer()` — V1, no algorithms | T1 | The default a consumer gets |
| **B-P1** | `Configure().PreserveReferences()` | T3 | The cost of reference framing |
| **B-P2** | `Configure().WithLimits(tight)` | — | The cost of accounting near a ceiling; not a performance mode |
| **B-P3** | `Configure().WithCompression(DeflateCompression)` (B-P3d) and `BrotliCompression` (B-P3b) | — | §15 |
| **B-P4** | `Configure().WithChecksum(Crc32Checksum)` | — | §16 |
| **B-P4x3** | `Configure().WithChecksum(XxHash3Checksum)` | — | §16 *(added in R5)* |
| **B-P4x128** | `Configure().WithChecksum(XxHash128Checksum)` | — | §16 *(added in R5)* |
| **B-P5** | `Configure().WithEncryption(Aes256GcmEncryption, key)` | — | §16 |
| **B-P5c** | `Configure().WithEncryption(ChaCha20Poly1305Encryption, key)` | — | §16 *(added in R5)* |
| **B-P6** | `BrotliCompression` + `Crc32Checksum` + `Aes256GcmEncryption` (B-P6b), and the same with `DeflateCompression` (B-P6d) | T4 | The full protected envelope |
| **B-P7** | `Configure().WithVersion(0).AllowV0Fallback()` | T0 | The compact codec |

- [ ] PROF-01 — every profile is measured on every dataset it supports, buffered and streaming
- [ ] PROF-02 — B-P0 against B-P7 isolates the V1 envelope from the payload, on one value under one layout *(Contract §10.2, §22.8)*
- [ ] PROF-03 — keyed models against positional ones of the same shape isolate the keyed layout, under B-P0 and B-P7 alike *(Contract §14.2)*
- [ ] PROF-04 — B-P1 against B-P0 measures reference framing on a graph with no sharing at all, so the price of the option when it is not needed is visible *(Contract §16)*
- [ ] PROF-05 — B-P2 against B-P0 shows what limit accounting costs; if the difference is not measurable, that is the result and it is published
- [ ] PROF-06 — `Populate` into an existing instance is measured against `Deserialize` of the same payload, which allocates the root; there is no `ref` form left to measure — a struct is read with the ordinary overload *(Contract §3.3; rewritten in R3)*
- [ ] PROF-07 — a serializer reused across operations is measured against one constructed per operation, so what the per-type caches and a fresh serializer cost has a number *(Contract §3; rewritten in R3 — the per-call `StreamExtensions` path it once priced is gone)*
- [ ] PROF-08 — a union-typed dataset is measured against the same shape written under its concrete type, so the discriminator's cost is separated from polymorphic dispatch *(Contract §15)*
The built-ins were renamed in R5 to carry their family as a suffix; the plan IDs of the profiles did
not change, and neither did the members of the harness's `ViperProfile` enum — `Deflate`, `Brotli`,
`Crc32`, `Aes256Gcm` — which name profiles, not types, and which are what a cell's parameters record.
A cell therefore keeps its identity across the rename, and BASE-02 still matches it with
`pre-rework`. The three profiles R5 added have no `pre-rework` cell.

- [x] PROF-09 — the same profile set is measured on the `pre-rework` commit, so the matrices of every rework stage compare with it cell by cell — `Baselines/pre-rework/`, 667 cells, none without a number

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
| **DATA-06** | DeepGraph | nesting at depths 5, 10, 25, 50, 200, 511 | small | T0–T3 | Depth accounting and recursion cost up to just below `MaxDepth` *(Contract §5.1)* |
| **DATA-07** | UnicodeHeavy | multi-byte, CJK, emoji, combining sequences, with an ASCII twin | ~100 KB | T0–T2 | String encoding cost, and the honest ASCII-versus-UTF-8 difference |
| **DATA-08** | Incompressible | high-entropy strings and blobs | ~1 MB | T0, T4 | Compression's worst case, and what the phase costs when it buys nothing |
| **DATA-09** | HighlyCompressible | heavily repeated structure | ~0.35 MB | T0, T4 | Compression's best case. The size follows `MaxDecompressionRatio` rather than the 1 MB of its neighbours — see R-02 in §27.3 |
| **DATA-10** | SharedReferenceDAG | one instance reachable by many paths | ~100 KB | T3 | Identity preservation, in time and in size |
| **DATA-11** | CyclicGraph | parent/child cycles | ~50 KB | T3 | The scenario that is impossible without reference support |
| **DATA-12** | PolymorphicBatch | a base type with 6–8 derived shapes | ~100 KB | T2, T3 | Discriminator cost and polymorphic dispatch |
| **DATA-13** | KeyedEvolution | a v1/v2 schema pair: fields added, removed, reordered | ~10 KB | T2 | Evolution cost on the writing side and on the skipping side |
| **DATA-14** | ByteBlob | one `byte[]` inside a small object, and a batch of four | 0.9 MB, 3.6 MB | T0–T2, T4 | Pure carrying cost, with traversal taken out of the picture. The sizes follow `MaxArrayLength` rather than the 16 MB the blob limit suggests — see PERF-01 in §27.1 |
| **DATA-15** | NumericArrays | `int[]`, `double[]`, `long[]`, 100k elements | ~1 MB | T0–T2 | Where a memcpy-shaped serializer legitimately wins, shown rather than avoided |
| **DATA-16** | StringTable | 50k short strings | ~1 MB | T0–T2 | String-dominated payloads and per-string overhead |
| **DATA-17** | NullSparse | 40 members, most of them null | ~1 KB | T0–T2 | Null framing cost against formats that omit absent fields |
| **DATA-18** | WideObject | 200 flat members | ~5 KB | T0, T2 | Member-plan cost, positional against keyed |
| **DATA-19** | CollectionZoo | one instance of each Contract §23 container family | ~50 KB | T0–T2 | Breadth over the supported-type table in a single measurement |
| **DATA-20** | TimeAndNumerics | `DateTime`, `DateTimeOffset`, `decimal`, `Int128`, `BigInteger`, vectors, matrices | ~10 KB | T0–T2 | The families other serializers most often lack natively |

- [ ] DATA-00 — every generator is deterministic, culture-independent and time-independent, and produces byte-identical data on two machines
- [ ] DATA-21 — every dataset is generated, verified through every adapter of its tiers, and its actual size published beside its target
- [ ] DATA-22 — a dataset that misses its target by more than 2× is resized, or its target is corrected
- [ ] DATA-23 — no dataset requires a limit above `SerializationLimits.Default`; one that would is split *(Contract §5)*
- [ ] DATA-24 — a dataset that a mandatory library cannot express is recorded as `Unsupported` for that library with the reason, and is not removed from the corpus

---

# 10. Workload catalog

The operations measured. Every workload runs per (adapter, dataset, profile) triple that its tier admits.

| ID | Workload | Layer | Notes |
|---|---|---|---|
| **WL-01** | Serialize to a new `byte[]` | L1, L2 | The primary comparative family |
| **WL-02** | Serialize to a pre-sized `MemoryStream` | L1, L2 | The streaming family; the stream is reset, never reallocated, inside the timed region |
| **WL-03** | Serialize to a non-seekable stream | L2 | Viper-specific: every profile, V0 and keyed contracts included, writes to a destination that cannot seek, so it is a timed workload — `ProfileStreamBenchmarks` *(Contract §7.2; rewritten in R2)* |
| **WL-04** | Deserialize from `byte[]` | L1, L2 | Payload produced in setup by the same adapter |
| **WL-05** | Deserialize from `MemoryStream` | L1, L2 | |
| **WL-06** | Round trip | L1, L2 | Serialize and deserialize in one timed operation |
| **WL-07** | Populate an existing instance | L2 | `Populate` against `Deserialize` of the same payload (PROF-06); `Unsupported` for most libraries, and the cell says so *(Contract §3.3; rewritten in R3)* |
| **WL-08** | Steady state over one serializer instance | L1, L2 | The default for every comparative suite |
| **WL-09** | First operation in a fresh process | L4 | §20 |
| **WL-10** | First operation for a type not seen before | L4 | Type-plan and formatter-cache construction, measured in a fresh process per type family |
| **WL-11** | Parallel throughput over a shared serializer | L4 | §21 |
| **WL-12** | Large payload throughput in MB/s | L1, L2 | DATA-04, DATA-14, DATA-15 |
| **WL-13** | Sustained load over a fixed duration | L4 | §22 |
| **WL-14** | Serialize the same graph with reference framing on and off | L2 | DATA-10, DATA-11 |
| **WL-15** | Read a payload whose schema differs from the model | L1 | DATA-13; the skipping side of evolution |
| **WL-18** | Deserialize from a non-seekable stream | L2 | The V1 profiles of the representative set below: a frame declares its length, so it is read without seeking — the path that did not exist before; V0 is refused there by design — `ProfileFramedReadBenchmarks` *(Contract §20; added in R3)* |
| **WL-19** | An encrypted frame written to an `IBufferWriter<byte>`, against the same frame to `byte[]` | L2 | WL-21 under B-P5 and B-P6 against WL-01 of the same profile. Since R5 the encryption writes straight into the writer's span, sized from the exact ciphertext length the algorithm states, and the difference is the copy saved *(Contract §13; added in R3, path in place since R5)* |
| **WL-20** | A long stream of small frames read with `DeserializeAsyncEnumerable` | L2 | 1 000 frames of DATA-01 from a pipe and from a stream, under B-P0 and B-P6b; throughput and allocation per frame, each frame its own operation — `FrameStreamBenchmarks` *(Contract §3.5; added in R3)* |
| **WL-21** | Serialize to an `IBufferWriter<byte>` | L1, L2 | The buffer family: a reset `ArrayBufferWriter<byte>`, over B-P0, B-P1, B-P5, B-P6b and B-P7 — `ProfileBufferBenchmarks` *(Contract §3.1; added in R3)* |
| **WL-22** | Deserialize from a `ReadOnlySpan<byte>` and from a `ReadOnlySequence<byte>` of four segments | L1, L2 | The buffer family's read side, over B-P0, B-P1, B-P5, B-P6b and B-P7 — `ProfileBufferBenchmarks` *(Contract §3.1; added in R3)* |
| **WL-23** | Serialize to a `PooledPayload` | L2 | The pooled family, labelled and never compared with an allocating one (FAIR-07), over B-P0, B-P1, B-P5, B-P6b and B-P7 — `ProfileBufferBenchmarks` *(Contract §3.2; added in R3)* |
| **WL-24** | The asynchronous family: serialize to a `Stream` and a `PipeWriter`, deserialize from a `Stream` and a `PipeReader`, awaited | L2 | Writes over B-P0, B-P1, B-P5, B-P6b and B-P7, V0 included; reads over the four V1 profiles among them, since V0 is read synchronously — `ProfileAsyncWriteBenchmarks`, `ProfileFramedReadBenchmarks` *(Contract §3.5; added in R3)* |

WL-18 and WL-21…WL-24 measure an entry point, not a configuration. What a profile costs is measured
over all ten profiles by WL-01…WL-06 (`ProfileMatrixBenchmarks`, `ProfileStreamBenchmarks`), so the
entry-point suites run over a representative set — the default frame, reference framing, one
encryption, the full envelope and V0 — which covers every kind of path a frame can take through them
(PROF-01 is carried by the matrix). *(Owner's decision of 2026-09-27, `rework/Owner-Review.md` log 56.)*

- [ ] WL-00 — every workload above has a suite, and every suite states which of WL-01…WL-24 it implements
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
- [ ] ALLOC-02 — Viper's write allocation is attributed by phase — payload write into `PayloadBuffer`, the one linearisation a phased payload needs, checksum, compression and encryption into pooled buffers, header, and the one copy into the destination or the returned `byte[]` — from the §18.1 component measurements, and cross-checked against the profile differentials of §18.2, never inferred from a total *(re-attributed in R2: no `MemoryStream` or intermediate `ToArray` remains on the path)*
- [ ] ALLOC-03 — the read path is attributed the same way: routing, header, the read-ahead or the array decoded where it lies, decryption and decompression into pooled buffers, payload read, materialization *(re-attributed in R2)*
- [ ] ALLOC-04 — allocation is reported per family — `byte[]`, streaming, buffer (`IBufferWriter<byte>` in, span and sequence out), pooled and asynchronous — and never summed across them, because the `byte[]` entry point's copy is part of what it costs, a buffer writer's growth belongs to the caller, a pooled payload rents instead of allocating, and an awaited call adds a state machine only when it truly suspends *(Contract §3.1, §3.2, §3.5; rewritten in R3)*
- [ ] ALLOC-05 — V1 and V0 buffer alike on write — the whole frame in the serializer's pooled buffers, then one copy — so what is measured is what the header and the phases add to V0's frame *(Contract §2.6, §10.2; rewritten in R2)*
- [ ] ALLOC-06 — a large-payload suite reports Gen2 and LOH behavior, and the payload sizes at which allocations cross the LOH threshold are named; re-measured from R2, where pooled phase buffers should remove most crossings for payloads under the pool's largest bucket
- [ ] ALLOC-07 — the tight-limits profile B-P2 is measured for allocation as well as time, so the accounting structures have a number
- [ ] ALLOC-08 — no allocation number is published from a run that also produced a timing in the same iteration when the diagnoser is known to perturb it; where it does, the timing comes from a separate run and the report says so
- [ ] ALLOC-10…ALLOC-21 — the allocation targets of `Rework-Plan.md` §11, one per line of its table, each with its target and its measured value, bytes per operation after warm-up, from `AllocationTargetBenchmarks` in `Measurements/48c7bf5-20260928T082209Z` *(added in R4)*. A target not met stays open with its number:

  | | Line of §11 | Target | Measured | State |
  |---|---|---|---|---|
  | ALLOC-10 | write to `IBufferWriter`, V1, a record of primitives and strings | 0 | 272 | open — two `PayloadBuffer` objects (PERF-07); the engine adds 0 (ALC-01) |
  | ALLOC-11 | the same, V0 | 0 | 136 | open — one `PayloadBuffer` (PERF-07) |
  | ALLOC-12 | the same with Brotli | 0 managed | 352 (R4: 296) | open — the buffers and the `CompressionBuffer` object (PERF-07, PERF-08) |
  | ALLOC-13 | the same with Deflate | one `DeflateStream` | 552 (R4: 840) | open — the buffers (PERF-07); the phase is the `DeflateStream`, its adapter stream and the `CompressionBuffer` |
  | ALLOC-14 | the same with encryption | one `AesGcm` | 432 (R4: 432) | open — the ciphertext now goes straight to the destination (a copy saved, not an allocation); the buffers remain (PERF-07) |
  | ALLOC-15 | `Serialize<T>(T)` → `byte[]` | the array | 384 (array alone 112) | open — the buffers (PERF-07); exactly the array beyond them (ALC-02) |
  | ALLOC-16 | `SerializePooled` | one `PooledPayload` | 304 | open — the buffers; exactly one `PooledPayload` beyond them (ALC-03) |
  | ALLOC-17 | read from a span, a record of primitives | the record | 152 (record alone 80) | open — 72 B of algorithm objects per read (PERF-07); exactly the record beyond them (ALC-04) |
  | ALLOC-18 | read a graph | the graph | 22 659 (graph alone 22 584) | met but for the same 72 B — no copy, no reallocation (ALC-05) |
  | ALLOC-19 | with `PreserveReferences` | as without | read 22 659 = 22 659; write 512 against 368 | read met; write +144 B: the reference frames make the payload 1.3 KB longer, so the payload buffer's segment list grows once more (PERF-07); the tables themselves are pooled (ALC-06) |
  | ALLOC-20 | asynchronous methods | as the synchronous ones | `SerializeAsync` 368 = 368; `DeserializeAsync` 22 659 = 22 659 | met |
  | ALLOC-21 | first use of a type | its codec and contract, once | contract: 137 KB for eight members (`contract-cold.csv`) | met in kind — nothing per type after first use (ALC-01, ALC-04); the cost itself is PERF-06 |
- [ ] ALLOC-09 — cycle detection without references: the engine's ancestor stack against a per-operation `HashSet` by reference over the same path, at depths 4, 32 and 500; the depth-500 engine cell also against SCALE-03 of `Baselines/pre-rework/` *(Contract §16; added in R2)* — `CycleDetectionBenchmarks`

---

# 14. Payload size and envelope accounting

Size is recorded without timing, from the verified serialization of §7.6.

A size is deterministic: the same value under the same configuration produces the same bytes on any machine, under any load, on any day. Size rows therefore do not need an idle machine and are valid the moment they are taken — the idle machine is a requirement of §11, the timings.

Recorded per (adapter, dataset, profile):

```text
payload bytes
envelope bytes (total minus the payload the same configuration would write without the envelope)
bytes per logical element
compressed bytes and ratio, where compression applies
encrypted bytes and the overhead over the plaintext
```

- [ ] SIZE-01 — every (adapter, dataset) pair has a size, or a result state explaining its absence
- [ ] SIZE-02 — the V1 envelope's fixed cost is stated in bytes, measured as B-P0 minus B-P7 on the same value *(Contract §22.6, §22.8)*
- [ ] SIZE-03 — the reference-framing cost is stated in bytes, as B-P1 minus B-P0 on a graph with no sharing, and as the saving on DATA-10 where sharing exists *(Contract §16)*
- [ ] SIZE-04 — the keyed layout's cost is stated in bytes against the positional layout on the same type, and against the evolution tolerance it buys *(Contract §14.2)*
- [ ] SIZE-05 — the per-string, per-element and per-null framing costs are derived from DATA-16, DATA-15 and DATA-17 and published as a table
- [ ] SIZE-06 — a size comparison against a self-describing text format states that the comparison is between formats of different kinds
- [ ] SIZE-07 — compressed sizes are only compared with compressed sizes, and the algorithm is named in the cell
- [ ] SIZE-08 — encrypted sizes name the tag and nonce overhead separately from the ciphertext *(Contract §13)*
- [ ] SIZE-09 — the V1 header of service records, in bytes, for no service, a checksum, a checksum and compression, and all three with a key id, beside the fixed header it replaced (29 bytes plus its strings and checksum) — `format-sizes.csv` *(Contract §11; added in R6)*
- [ ] SIZE-10 — the null fold, in bytes, on a wide nullable record of sixteen members all null and all set, and on a list of 1 000 strings and of 1 000 nulls — `format-sizes.csv` *(Contract §22.2; added in R6)*

---

# 15. Compression

Layer L2, over DATA-08, DATA-09, DATA-04, DATA-14 and DATA-16.

- [ ] CMP-01 — `DeflateCompression` compress and decompress: time, ratio, allocation, at every corpus size *(Contract §12)*
- [ ] CMP-02 — `BrotliCompression` compress and decompress: the same *(Contract §12)*
- [ ] CMP-03 — the no-compression path is measured on the same datasets, so the phase's cost is a difference rather than an estimate
- [ ] CMP-04 — the incompressible dataset shows what compression costs when it saves nothing, including the case where output exceeds input *(Contract §12)*
- [ ] CMP-05 — compression throughput is published in MB/s of input, on both directions
- [ ] CMP-06 — decompression is measured against its declared uncompressed length, since the exact-length rule is part of the read path *(Contract §12)*
- [ ] CMP-07 — where a competitor offers built-in compression, it appears in this section and nowhere else *(FAIR-16)*
- [ ] CMP-08 — a custom registered algorithm is measured once, so the extension path's overhead over a built-in is known *(Contract §4.1)*
- [ ] CMP-09 — incremental decompression for `DeflateCompression` and `BrotliCompression`: time and allocation against the previous single-buffer path, at every corpus size. NX-01 replaced the path, so its cost on a legitimate payload is unknown, and a 64 KiB probe promoted once is a copy on every payload above 64 KiB. Since R5 it is the only path, for every algorithm, so the comparison is with `pre-rework`
- [ ] CMP-10 — the `MaxDecompressionRatio` check, isolated: expected to be negligible, and a number makes it a fact

---

# 16. Checksum and encryption

- [ ] SEC-01 — `Crc32Checksum` over each corpus size: time, throughput, allocation *(§11)*
- [ ] SEC-02 — the checksum's share of a full V1 write and read, as a difference against B-P0
- [ ] SEC-03 — `Aes256GcmEncryption` encrypt and decrypt: time, throughput MB/s, allocation, at every corpus size *(Contract §13)*
- [ ] SEC-04 — the associated data is the header bytes, so its cost is the header's own: measured through MICRO-08 on the narrowest and the widest header, and as the difference between a payload carrying long custom algorithm names and a key id and one carrying none *(Contract §13.1; rewritten in R6 — there is no separate image to build)*
- [ ] SEC-05 — key resolution through `IKeyProvider` measured against a static key, including the per-operation copy `SecretKey` makes *(Contract §13.2)*
- [ ] SEC-06 — the full protected envelope B-P6 against B-P0, per dataset, so the price of protection is one number a reader can act on
- [ ] SEC-09 — `XxHash3Checksum` and `XxHash128Checksum` over each corpus size, beside `Crc32Checksum`: time, throughput, allocation; and B-P4x3, B-P4x128 against B-P0 as SEC-02 does for B-P4 *(Contract §3; added in R5)*
- [ ] SEC-10 — `ChaCha20Poly1305Encryption` encrypt and decrypt beside `Aes256GcmEncryption`, and B-P5c against B-P5. Which one is faster depends on whether the processor has AES instructions: the manifest records the CPU, and a published result names it together with SEC-08's AES flag *(Contract §13; added in R5)*
- [ ] SEC-07 — the order of phases is the contract's, and no benchmark measures a reordered pipeline *(Contract §10.1)*
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
- [ ] COMP-02 — the composed header carries the same information the V1 header does, so the size comparison is between equivalents *(Contract §11)*
- [ ] COMP-03 — the composed baseline is measured on the same datasets as B-P6, with the same statistics
- [ ] COMP-04 — the report states plainly what the composed baseline does **not** provide: no authenticated metadata binding, no algorithm negotiation, no key id, no limits, no budget accounting *(Contract §13.1, §5)*
- [ ] COMP-05 — where Viper is slower than a composed baseline, the difference is published with the features that account for it named, and never hidden behind a feature argument alone

---

# 18. Component measurements

Diagnostic, never a market comparison. They exist for two readers: the engineer explaining an L1 or L2 result, and the next version, which needs a per-component record of this one to know what a change actually improved. Each item names the end-to-end number it explains.

## 18.1 Microbenchmarks, below the public API

Measured directly on the internal type that owns the mechanism, through the grant of §18.3.

MICRO-04 measures the reflected contract, the only one v1.0 has. The conformance suite of the test
plan (`QA-Plan.md` §15.1, CONF-01…CONF-07) fixes, for every object shape, the description and the
bytes any `TypeContract<T>` must produce, so a generated contract measured later against MICRO-04 is
measured doing the same work to the same wire — a like-for-like comparison, not a new workload.
*(added in R8)*

- [ ] MICRO-01 — `WireWriter`/`WireReader` primitives: varint, fixed-width, string, blob, on both directions, compared cell by cell with the `ValueWriter`/`ValueReader` cells of `Baselines/pre-rework/`; a write cell includes filling the `PayloadBuffer` from empty and returning it, as one serialization does *(Contract §22.1; rewritten in R1)*
- [ ] MICRO-02 — `ElementCount` validation and budget charging over a hot loop, on the budget inside the `OperationState` the pipeline creates, reached by reference as the codecs reach it *(Contract §6; rewritten in R4)* — `BudgetBenchmarks`
- [ ] MICRO-03 — depth scope entry and exit over the state's budget, and the unwind on the exceptional path *(Contract §5.1; rewritten in R4)* — `DepthScopeBenchmarks`
- [ ] MICRO-04 — `ReflectedContract<T>` construction for a cold type, and the lookup by runtime type the polymorphic slot makes once cached, positional and keyed; a value of its declared type pays no lookup *(Contract §14, §15; rewritten in R4)* — `ContractLookupBenchmarks`, `ContractColdRunner`
- [ ] MICRO-05 — the codec of a claimed type and of a member-encoded one, read from the static field of `FormatterCache<T>` *(Contract §18; rewritten in R4)* — `FormatterResolutionBenchmarks`
- [ ] MICRO-06 — one codec per shape family: scalar, sequence, map, composite, the containers through the engine's entry for a payload *(Contract §2.4; rewritten in R4)* — `FormatterShapeBenchmarks`
- [ ] MICRO-07 — reference identity tracking through the pooled reference tables the payload's traversal rents and returns: rent, registration, lookup, scope exit and return, at several sharing densities *(Contract §16; rewritten in R2; re-read in R4, where the tables moved into `GraphState` unchanged)* — `ReferenceIdentityBenchmarks`
- [ ] MICRO-08 — V1 header write and parse over service records, for no service and for all three with custom names, a key id and a checksum *(Contract §11, §13.1; rewritten in R6 — the image build is gone)*
- [ ] MICRO-09 — metering and windowing over buffers against a bare copy: the `PayloadBuffer` budget on write, the `WireReader` budget on read, the `WireReader.Slice` window read and skip, and the copy of a finished buffer to a stream *(Contract §7; rewritten in R2, where the three stream decorators were removed)* — `MeteringBenchmarks`
- [ ] MICRO-10 — the algorithm primitives outside the pipeline, every built-in: `DeflateCompression` and `BrotliCompression` into a reused buffer writer, `Crc32Checksum`, `XxHash3Checksum`, `XxHash128Checksum`, `Aes256GcmEncryption` and `ChaCha20Poly1305Encryption` *(Contract §12, §13; extended in R5)* — `CompressionPrimitiveBenchmarks`, `ProtectionPrimitiveBenchmarks`
- [x] MICRO-11 — allocation is recorded for every microbenchmark above, not only time, since the per-component allocation record is what a later version compares against
- [x] MICRO-12 — every microbenchmark names the end-to-end measurement it explains; one that explains nothing is deleted

## 18.2 Differentials, through the public API

The same mechanisms seen from outside, by subtracting two public measurements that differ in one thing alone. They are not a substitute for §18.1 — they are its cross-check, and they are what remains measurable if a grant is ever withdrawn.

- [ ] DIFF-01 — the V1 envelope: B-P0 minus B-P7 on one value, with and without custom algorithm names and a key id *(Contract §11, §22.8)*
- [ ] DIFF-02 — each algorithm phase: B-P3, B-P4, B-P5 against B-P0 *(Contract §12, §13)*
- [ ] DIFF-03 — the member plan: DATA-18 positional against keyed, first use against steady state *(Contract §14)*
- [ ] DIFF-04 — reference framing: B-P1 against B-P0 by sharing density *(Contract §16, and SCALE-07)*
- [ ] DIFF-05 — limit accounting: B-P2 against B-P0 *(Contract §5)*
- [ ] DIFF-06 — stream metering: the stream family against the `byte[]` family on the same value *(Contract §7)*
- [ ] DIFF-07 — each differential agrees with the §18.1 measurement of the same mechanism within their combined margins of error; a disagreement is investigated before either number is published

## 18.3 Access

The component suites see internals through `src/ViShap.Viper.Serialization/Properties/AssemblyInfo.QA.cs`, which names `ViShap.Viper.Serialization.Benchmarks`. The grant is the owner's to give; this plan does not edit `src/` (§1).

- [x] MICRO-13 — the grant exists for `ViShap.Viper.Serialization.Benchmarks`, added by the repository owner on 2026-09-20, and an internal type resolves from the benchmark project in a Release build *(Q1)*
- [x] MICRO-14 — the grant is the only thing the component suites need from `src/`; nothing else is added, made public, or made `internal` for their sake, and a measurement that would need more is a proposal in `internal/performance/` (§27.4)
- [ ] MICRO-15 — the count a sequence shape reports on write, and the write path it enables for sets, frozen and immutable sets, against the gathering path a sequence without an O(1) count takes. NX-02 removed an intermediate list per set written; it may be the largest incidental gain of the NX changes and nothing records it *(rewritten in R4: `CollectionCountCache` is deleted, each shape counts its own collection)*
- [ ] MICRO-16 — the duplicate check after `Complete`, the count read on every container read that NX-02 put on the read path of every collection
- [ ] MICRO-17 — a positional record of sixteen booleans, eight bytes and eight 16-bit integers, written and read through the engine: the shape where a call per byte dominates, so the gain of the primitives moving off `Stream` is visible rather than averaged away *(Contract §2.3, §22.1; added in R1)* — `SmallFieldBenchmarks`
- [ ] MICRO-18 — a primitive array read into an array of its final length against the pooled path a count the bytes do not back takes, at 16, 4 096 and 1 000 000 elements *(Contract §17; added in R4)* — `ArrayMaterializationBenchmarks`
- [ ] MICRO-19 — the dumper, informational: `Dump<T>` of DATA-01, DATA-04 and DATA-08, time and allocation per node; it gates nothing, but states what a dump of a large frame costs and catches an accidental quadratic rendering *(Contract §19; added in R6)*

---

# 19. Scaling curves

A single size is a point; a curve is a property. Each curve has at least five points and states where the slope changes.

- [ ] SCALE-01 — element count: 1, 10, 100, 1k, 10k, 100k records — time, allocation, bytes
- [ ] SCALE-02 — payload size: 1 KB, 64 KB, 1 MB, 16 MB, 64 MB — throughput MB/s, against `MaxPayloadBytes` *(Contract §5)*
- [ ] SCALE-03 — depth: 1, 5, 25, 100, 500 — time and allocation per level *(Contract §5.1)*
- [ ] SCALE-04 — member count: 5, 20, 50, 100, 200 members — positional against keyed *(Contract §14)*
- [ ] SCALE-05 — string length: 8 B, 256 B, 4 KB, 64 KB, 1 MB — ASCII against multi-byte
- [ ] SCALE-06 — dictionary size: 10, 100, 1k, 10k, 100k entries
- [ ] SCALE-07 — sharing density in a DAG: 0%, 10%, 50%, 90% shared nodes — B-P1 time and size against B-P0 *(Contract §16)*
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
- [ ] SOAK-04 — the same run with encryption and compression enabled, since those phases own the temporary buffers *(Contract §13.2)*
- [ ] SOAK-05 — throughput at the end of the run is within the margin of error of throughput at the start, or the difference is explained

---

# 23. Reporting artifacts

A run produces raw artifacts and a report generated from them. Nothing in the report is typed by hand.

Both directories below live inside the benchmark project, beside the layout of §2:
`benchmarks/ViShap.Viper.Serialization.Benchmarks/`. Neither is ignored by git.

```text
Baselines/<tag>/
  run.json                  what the run was: kind, tag, revision, job, filter, suites run
  environment.json          §4 manifest
  capabilities.csv          §7.5 probe results
  verification.csv          §7.6 outcomes, per (adapter, dataset)
  results.zip               the BenchmarkDotNet export, one csv, json, md and html per suite
  results.csv               every timed cell of the run, in one table
  payload-sizes.csv         §14
  memory.csv                §13
  components.csv            §18 per-mechanism time and allocation
  contract-cold.csv         §18 member-plan construction, per type
  cold-start.csv            §20 per launch, aggregated
  soak.csv                  §22 throughput and heap over time
  configuration.md          §7.3 rationales, verbatim
  report.md                 the generated report
  report.html               the same, with the charts inline
  charts/                   §24
  reproduction.md           §26
  manifest.md               what ran, when, on what, against which packages
```

A full baseline is not the only thing worth committing. A change to one mechanism is examined by running
the suites that cover it, and those numbers belong in a directory that can never be mistaken for a
baseline:

```text
Measurements/<git describe>-<UTC timestamp>/
  environment.json          §4 manifest, exactly as a baseline records it
  scope.md                  which suites ran, which did not, and why the run was taken
  …                         only the result files the suites it ran produced
```

- [ ] REP-01 — every artifact above is produced by one command
- [x] REP-02 — the report regenerates byte-identically from the raw files, so the raw files are the record and the report is a view
- [x] REP-03 — the generator's own version and the revision it ran on are recorded in the manifest
- [x] REP-04 — a cell with no number carries its result state and its reason, never a blank
- [ ] REP-05 — the report opens with the tier table, the roster, and the exclusions, before any number
- [x] REP-06 — the report never reduces the outcome to a single winner
- [ ] REP-07 — every claim in the README or the package description that cites a performance figure cites a cell in a committed raw file
- [x] REP-08 — a publication run writes every artifact of the list above directly into `Baselines/<tag>/`, never into a directory git ignores, so no result exists only outside the commit
- [ ] REP-09 — a publication run refuses to start when `Baselines/<tag>/` already exists; a re-measurement of the same tag goes to a new directory and the difference between the two is recorded in §27.3
- [x] REP-10 — `BenchmarkDotNet.Artifacts/` is the working directory of ad-hoc and exploratory runs, is ignored by git, and is never the source of a published figure
- [x] REP-11 — every baseline is readable on its own: no artifact in it refers to another baseline, to the working directory, or to a file outside the repository
- [x] REP-12 — a measurement writes to `Measurements/<git describe>-<UTC timestamp>/`, needs no tag, and never writes into `Baselines/`; the timestamp makes every run its own directory, so no measurement can overwrite another
- [x] REP-13 — a measurement carries `scope.md` naming every suite it ran and every suite of §10 and §18 it did not, so it can never be read as a baseline
- [ ] REP-14 — a measurement is never the source of a published figure about anything it did not measure, and a delta against a baseline covers only the cells both contain

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
- [x] CHT-13 — the chart layer contains no value that is absent from the raw results
- [x] CHT-14 — every chart is committed inside its baseline as SVG at a stable path, so a README or a release note can reference it directly and the chart of an older baseline keeps rendering after a newer one exists
- [ ] CHT-15 — a chart that shows two versions names both tags, and its values come from both baselines' raw files rather than from one run

---

# 25. Baselines and regression policy

A baseline belongs to one source revision, runtime, hardware, package lock and BenchmarkDotNet version. Later releases are compared only against a compatible baseline environment.

The run against the `v1.0.0` tag becomes the frozen baseline, whenever it happens. It is never regenerated afterwards: a rebuilt baseline agrees with whatever the code became, and so proves nothing.

Every later v1.x that changes `src/` is measured the same way, against its own tag, and published as a delta against the baseline it is compatible with. That is what this plan is for once v1.0.0 has shipped: not a gate in front of a release, a record behind each one.

The rework before `v1.0.0` has a baseline of its own: `Baselines/pre-rework/`, taken once on the commit the rework started from. A rework stage is compared against `pre-rework`; a release is compared against the previous release. The two never mix: a rework stage is not a release, and `pre-rework` is never the baseline of one.

- [ ] BASE-01 — the baseline package is committed under `Baselines/v1.0.0/` with every artifact of §23, produced from a checkout of the `v1.0.0` tag
- [x] BASE-02 — a comparison tool reports the delta of a new run against a baseline, cell by cell, with margins of error — `--compare <baseline> <run>` (`Reporting/BaselineComparison`): every cell of `results.csv`, `components.csv`, `contract-cold.csv` and `cold-start.csv`, faster or slower only when the intervals do not overlap, a suite the run did not measure reported once, a cell on one side only listed *(built in R4)*
- [ ] BASE-03 — a comparison against an incompatible environment is refused rather than printed
- [ ] BASE-04 — review thresholds, as triggers for investigation and not automatic failures:
  - a throughput regression beyond 10% that is stable across two runs;
  - an allocation increase beyond 10%;
  - any payload-size increase at all, since size is a wire property and a change may be a compatibility break;
  - a statistically significant cold-start regression;
  - a Gen2 or LOH increase that was not there before
- [ ] BASE-05 — a crossed threshold is recorded in §27 with its cause; it informs the next version, and never holds a release hostage
- [ ] BASE-06 — each subsequent v1.x run is committed under its own `Baselines/<tag>/`, with the delta against the previous one and an entry in §27.3 for every threshold it crosses
- [ ] BASE-07 — every cell of a baseline carries the tag it belongs to, so two baselines can be read in one table without either being modified
- [x] BASE-08 — the directory name of a full baseline is the tag `git describe --tags --exact-match HEAD` reports and nothing else; on a commit that carries no tag no baseline directory is created at all and the run is recorded under `Measurements/` instead (REP-12), because a baseline signed with the wrong version is worse than a missing one
- [ ] BASE-09 — a full baseline is taken deliberately, not once per tag: a release that did not change `src/` records that the previous baseline still applies, and anything examined between baselines is a measurement of §23

---

# 26. Reproduction

Every baseline carries its own `reproduction.md`: the commands that produced it, in the order they ran, so a second engineer can take the same measurements from a clean clone of the same revision. It names only what the repository contains — the benchmark project's own command-line modes — because a reader of a baseline has the repository and nothing else.

- [x] REPRO-01 — `Baselines/<tag>/reproduction.md` lists the exact command sequence that produced that baseline, in order, and nothing in it depends on a file outside the repository
- [x] REPRO-02 — it states the hardware, the machine state the run requires, and the expected wall-clock duration of each command
- [x] REPRO-03 — it shows how to re-run one stage or one suite alone, with the same commands narrowed by a filter
- [x] REPRO-04 — it names which values are expected to differ on other hardware and which are not: payload sizes, compression ratios and result states are properties of the format and hold everywhere, while every timing and every allocation figure belongs to the recorded machine
- [x] REPRO-05 — it is committed inside the baseline package, so the baseline is repeatable without this plan

---

# 27. Findings

Everything this plan discovers is **recorded**. Nothing it discovers is acted on in `src/`.

## 27.1 Findings — index

Behavior seen while measuring that the owner may want to know about: an unexpected cost, a surprising allocation, a result that does not match what the contract led one to expect, an optimization the numbers suggest.

**Every finding lives in `internal/performance/` as its own file.** This section is the index and nothing
else: one line per finding, so the plan stays a checkpoint list and the finding stays where a decision
can be recorded against it.

| | Finding | Raised at | Status |
|---|---|---|---|
| [PERF-01](performance/PERF-01-byte-array-limits.md) | A `byte[]` is bounded by `MaxArrayLength`, not by the blob limit its name suggests, and spends the element budget per byte | B0 verification, DATA-08 and DATA-14 | Resolved by the owner, as a clarification of Contract §5; no behavior and no bytes changed |
| [PERF-02](performance/PERF-02-bulk-binary-accounting.md) | Should bulk binary data spend the structural element budget, a byte budget, or both? | A5, SCALE-02 and SCALE-09 | Open |
| [PERF-03](performance/PERF-03-write-buffer-lifecycle.md) | The write buffer's rent, clear and return cost a small blob write more than the pre-sized stream the old MICRO-01 cell used | MICRO-01, rework R1 | Open |
| [PERF-04](performance/PERF-04-pooled-phase-buffers.md) | The pooled write path is slower on a large unphased blob (×1.15) and on a few compressed cells (×1.05–1.07), while the rest of the matrix is ×0.43–0.99 | ALLOC-02, ALLOC-03, ALLOC-06, rework R2 | Open |
| [PERF-05](performance/PERF-05-ancestor-stack-depth.md) | The ancestor-stack cycle search is quadratic in depth; SCALE-03 goes from ×0.56 at depth 1 to ×0.94 at depth 500 | ALLOC-09, SCALE-03, rework R2 | Open |
| [PERF-06](performance/PERF-06-typed-engine-first-use.md) | The first operation of a process is ×1.34–1.69 slower after the typed engine (+20–35 ms), and one member plan ×1.3–1.5; the steady state is ×0.41–0.51 | COLD against pre-rework, MICRO-04, rework R4 | Open |
| [PERF-07](performance/PERF-07-frame-fixed-allocations.md) | The frame allocates 136 B per payload buffer on write and 72 B of algorithm objects per read; the engine adds nothing | ALLOC-10…ALLOC-17, rework R4 | Open |

## 27.2 Open questions

An experiment that cannot be made fair, a scenario the plan does not say how to measure, a tier a library sits between, or a decision that belongs to the owner because it touches something this plan may not change. The item is marked `BLOCKED (Qn)` until the owner decides.

*(none open)*

### Decided

| | Question | Decision | Unblocked |
|---|---|---|---|
| **Q1** | The component suites of §18.1 measure internal mechanisms — value primitives, budgets, contracts, header, metered streams — and the benchmark assembly could not see them. Adding the grant is a change in `src/`, which this plan does not make (§1) | Granted by the repository owner on 2026-09-20: `AssemblyInfo.QA.cs` now names `ViShap.Viper.Serialization.Benchmarks`, verified by resolving an internal type from the benchmark project in a Release build. Nothing a consumer sees changes, and v1.0.0 gets the per-component record a later version measures its optimizations against | MICRO-01…MICRO-14, ALLOC-02, ALLOC-03, SEC-04 |
| **Q2** | PERF-01 showed Contract §5.6 describing `MaxByteBlobBytes` in words that also describe `byte[]`, which no benchmark can correct: the contract and `src/` are outside this plan (§1, §28 *Boundary*) | Decided by the repository owner on 2026-09-21: §5.2, §5.6, §5.7, §6 and a new §21.4 state the wire-form model, the XML docs of three limits repeat it for a consumer on hover, and every `BinaryLimitException` now names the property that governs it. No behavior, no defaults and no bytes changed, and the change is the owner's commit, not this plan's. The boundary box of §28 is therefore evaluated against the benchmark work alone, and this row is what records the exception | PERF-01 closed; PERF-02 opened |

## 27.3 Results register

The place where an unflattering result is recorded rather than argued with. Each entry names the cell, the gap, the cause if it is known, and whether the owner accepted it for v1.0.0 or opened a proposal against it.

| | Cell | What was seen | Cause | Resolution |
|---|---|---|---|---|
| **R-01** | §14 sizes, DATA-19 under B-P3d, B-P3b, B-P6b, B-P6d | The compressed size of one dataset moved between runs of `--sizes` — 12088, 12087, 12085, 12084 bytes — while its uncompressed size stayed at 18733. Nothing in the harness had changed between the runs | `CollectionZoo` held an `ImmutableDictionary<string, int>`. An immutable dictionary enumerates in hash order and .NET randomizes string hash codes per process, so the payload carried the same lengths in a different order in every run, and the compressor answered differently | Harness defect, fixed before any publication run: the member is keyed by an integer, which keeps the container family in the corpus and makes its order deterministic. Three consecutive `--sizes` runs now produce a byte-identical 270-row table. DATA-00 still asks for byte-identical data on **two machines**, which one machine cannot show, so it stays open |
| **R-02** | B0 verification, DATA-09 under B-P3b and B-P6b | The Track A run for the `pre-rework` baseline stopped at verification: 2 of 270 pairs failed with `BinaryLimitException` — Brotli wrote 20 000 identical strings, 1 440 005 bytes, as 64, and the reader refused an expansion of 22 500 against the default `MaxDecompressionRatio` of 10 000. Deflate wrote the same payload as 8 496 bytes and passed | The ratio check arrived with the NX fixes after the corpus was sized. Brotli compresses this payload to a few dozen bytes at any count — 76 to 77 bytes anywhere from 2 000 to 10 000 strings — so the ratio grows with the count alone, and at 20 000 the dataset required a limit above `SerializationLimits.Default`, which DATA-23 forbids | Harness defect, fixed by the repository owner's decision of 2026-09-26 before the baseline: DATA-09 holds 5 000 strings, 360 005 bytes, an expansion of about 4 700 under Brotli. It stays compression's best case and needs no relaxed limit, as FAIR-18 requires. Raising the limit in the Brotli profiles, varying the strings and recording the refusal as a result were rejected. `--verify` passes 270 of 270 pairs |
| **R-03** | Rework R4 against `pre-rework`, `Measurements/48c7bf5-20260928T082209Z`, `comparison-pre-rework.md` | Of 220 matched timed cells of `results.csv`, 211 are faster and 8 slower. The slower are six nanosecond-scale component cells — MICRO-02 `validate array count` 1.3 → 1.7 ns, `charge graph node` 0.9 → 1.0 ns, `charge keyed field` ×1.05; MICRO-03 `descend and return` at depth 64 and `descend and unwind` ×1.02–1.03 — one profile cell, WL-01 B-P3b DATA-01 13 381 ± 47 → 14 412 ± 74 ns (×1.08, with allocation 2 336 → 448 B), and one cell within error. All 18 cold-start cells and both generic contract rows are slower | The budget is reached through a `ref` to the state instead of a class field, which the JIT keeps in a register less often; the Brotli cell is the phase, which R5 rewrites; the cold cells are PERF-06 | Micro cells accepted as the cost of one operation state per call (INV-1), a few tenths of a nanosecond per charge. The cold cells are PERF-06, open. The Brotli cell is re-measured in R5 |
| **R-04** | Rework R5 against `pre-rework`, `Measurements/c215131-20260928T131001Z`, `comparison-pre-rework.md` | All 70 matched `AlgorithmBenchmarks` cells are faster (×0.05–×0.88). Of the MICRO-10 primitives 28 cells are slower: `Crc32Checksum` ×1.05–1.10 and AES-GCM ×1.01–1.15 though their code did not change, Brotli ×1.04–1.22 with 32 B per call, Deflate decompress of 1 MB compressible ×1.57. ALLOC-12 296 → 352 B, ALLOC-13 840 → 552 B, ALLOC-14 432 B unchanged | The unchanged primitives set the run's own drift at up to ~15 %; the soak took 40 minutes of wall time for 10. Brotli moved from the one-shot calls to a stepped encoder and decoder over the buffer writer; Deflate copies its input for the stream it reads through; compression writes into a `CompressionBuffer` object | PERF-08, open: re-measure MICRO-10 on an idle machine first, then the one-shot Brotli path, a Deflate profile and a reused `CompressionBuffer` |

## 27.4 Proposals — `internal/performance/`

The one output this plan produces besides measurements. A proposal is a document, not a change: it describes what was measured, what it suggests, what it would cost and what it would risk, and it ends where the owner's decision begins.

One file per proposal, `PERF-nn-<slug>.md`, indexed by `internal/performance/README.md`, in the shape that file defines.

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

- [ ] The benchmark project builds and runs in Release, and `--smoke` passes over every suite.
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

- [ ] `src/` and `internal/System-Contract.md` are untouched by this plan's work: the diff of the benchmark effort contains the benchmark project, this plan and `internal/performance/`, and nothing else. The `InternalsVisibleTo` grant of §18.3, if it exists, was made by the owner.
- [ ] Every optimization idea the measurements produced exists as a proposal in `internal/performance/`, and none of them was applied.
- [ ] Comparative suites use the public surface alone; internal access appears only in the component suites, and no competitor adapter uses it.
- [ ] The component record of §18 is committed with the baseline, so a later version can measure a change against this one.

## Publication

- [ ] The report and charts regenerate from the raw files with one command.
- [ ] The environment manifest, the package lock and the source revision are committed with the results.
- [ ] The v1.0.0 baseline is frozen under `Baselines/v1.0.0/`.
- [ ] Reproduction instructions are committed, and the sequence has been executed once from a clean clone on a machine that did not produce the baseline — a second machine, or a base container image — proving the instructions complete. The check compares payload sizes, verification outcomes and result states, which are machine-independent; it does not compare timings or allocation, which belong to the recorded machine.

---

# 29. Tracks

The plan is worked in two tracks, because half of it needs nothing but Viper and the other half needs
six competitor libraries. A session is told which track it is on and works only that track's items.

## 29.1 Track A — Viper alone

Everything measurable without a second library. It is worked first, and it stands on its own: its
result is the record of what this version costs, feature by feature.

**In scope, in this order:**

| | Sections | What it produces |
|---|---|---|
| **A0** | §2 layout, §4 manifest, §8 profiles, §9 corpus, §7.6 verification, §12 validity | A harness whose every dataset verifies under every profile before a timing exists |
| **A1** | §10 WL-01…WL-08 and WL-14 over §8 × §9 | The profile matrix: what each configuration costs |
| **A2** | §14 (Viper rows), §11 | Sizes and the envelope accounting, with no timing in the same table |
| **A3** | §15, §16 | Compression, checksum and encryption, separately and combined |
| **A4** | §18 | The component record, time and allocation per mechanism |
| **A5** | §13, §19 | Allocation attributed per phase from A4, GC, and the scaling curves |
| **A6** | §20, §21, §22 (Viper rows) | Cold start, concurrency, soak |
| **A7** | §23, §24, §25, §26 for what A0–A6 measured | Artifacts, charts, baseline, reproduction |

Each A-stage closes when its suites exist, build, and their cells come from one publication run. That
is a milestone of this track, not of §3: **B1, B5 and B7 close entirely here, and B0, B3, B4, B6, B8
and B9 stay open until Track B fills in their comparative items.** A stage is never marked closed
because the Viper half of it is done.

**The tag.** A baseline belongs to a revision, and it takes its name from the tag on that revision, so
the tag exists before the run does: the release is tagged, the tagged commit is checked out, and the
publication run is taken there (BASE-08). A run on an untagged commit is a measurement of §23, never a
baseline, however complete it happens to be.

**Out of scope, and left untouched:** §5 the roster, §6 the tier tables beyond Viper's own placement,
§7.3 configuration attestation, §7.4 model equivalence, §7.5 capability probes, §10 WL-09…WL-15 rows
belonging to another library, §17 composed baselines, and every comparative cell.

**The freeze rule.** No publication run happens until A0–A6 exist and build. Once the first
publication run starts, the harness is frozen: a change to a dataset, a profile, a suite or the job
configuration invalidates every cell that change could touch, and those suites are re-run in full.
Numbers taken while the harness was still moving are exploratory and never enter an artifact.

**One command.** A publication run is started once and left alone: `--track A` runs every A-suite in
order, writes the manifest, the verification, the raw results and the report, and stops. No
interactive step, no decision in the middle.

## 29.2 Track B — the market

Everything that needs the roster of §5: model variants, adapters, capability probes, tier tables,
composed baselines, and the comparative cells of §10.

It is entered only once Track A's baseline is committed and frozen, and it re-measures nothing Track A
already recorded: the corpus, the profiles, the job configuration and the verification stay as they
are, so an A cell and a B cell in the same table describe the same experiment.

## 29.3 Current position

Kept accurate at the end of every session, so a session that starts cold knows where to resume
without reading the history.

```text
Track:        A — Viper alone
Stage:        A0–A7 written. Every A-stage suite exists, builds and runs; what remains for each is the
              number, which only the publication run produces
Harness:      frozen? not yet, and nothing further is planned in it. The freeze takes effect when the
              publication run starts
Last stage:   rework R4 — Measurement 48c7bf5-20260928T082209Z (11 suites, cold start, contract cold,
              soak), compared with pre-rework through --compare: comparison-pre-rework.md beside it
Last run:     the pre-rework Baseline — every suite of Track A under the publication job on 916f805, the
              commit the rework started from, tagged locally pre-rework: 667 cells, none without a
              number, 3h54m. Committed under Baselines/pre-rework/. It is the "before" of every rework
              stage (§25), not a release baseline
Machine:      the one recorded in Baselines/pre-rework/environment.json
Next action:  during the rework, the stage measurements of Development-Workflow §6.5 against pre-rework.
              After the release, tag v1.0.0, check the tag out, and take the publication run there on an
              idle machine — about 4 hours, of which 40 minutes is the soak. Then tick the A-stage
              measurement boxes against the committed raw files under Baselines/v1.0.0/. Nothing is open before that: the
              last two A5 items are written — the 16 MB and 64 MB points of SCALE-02 are built from
              records rather than byte arrays, because array data spends the element budget per byte
              (PERF-01, PERF-02), and SCALE-09 re-runs that large end under Server GC
```

---

# 30. Definition of completion

Benchmarking is complete when the raw results, the environment manifest, the capability matrix, the baselines and the generated charts exist together, trace to one source revision and one locked environment, and every cell is either a number or a stated reason.

The report presents measurements. It does not reduce the outcome to a winner, and it does not omit a result because of what the result is.

Complete for a version, not for the project: the plan is worked again against each v1.x tag, and what changes between runs is the corpus only where a new capability needs one — never the fairness rules, and never mid-flight.
