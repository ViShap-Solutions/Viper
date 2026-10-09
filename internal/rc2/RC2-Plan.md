# ViShap.Viper — Plan for `v1.0.0-rc.2`

**Class: plan.** A checklist of work to do before `v1.0.0`, in the order it is to be done. It defines no
behavior: the contract (`internal/System-Contract.md`) does, and each stage here changes it in the stage
that makes the change real.

**Status:** drafted 2026-10-09 from the documents; **not approved, not started.** Section 3 holds the
decisions only the owner can take; no stage whose decision is open begins before it is answered.
**Written against:** `release/v1.0.0` at `c728c99` (the commit `v1.0.0-rc.1` points at), the tree clean,
2 014 tests green in Release.

---

# 0. What this plan changes about earlier decisions

Until now everything not needed for `v1.0.0` was recorded as "after the release, additive"
(`rework/Decisions.md` D1.1, D9.2, D9.20; `rework/Rework-Plan.md` §13, §13.1; contract §21.3 "deferred
by design"). On 2026-10-09 the owner decided the opposite timing: **all of it is done before `v1.0.0`,
collected in `v1.0.0-rc.2`.** Consequences, each checked against the documents:

| Consequence | Where it comes from |
|---|---|
| The rules of `Development-Workflow.md` §5 start at the `v1.0.0` tag (§5.6). Until then nothing here costs a major version, and what is published in `v1.0.0` is what §5 then freezes — which is the reason to settle the public shape of the generator seam and the names in it now, not after. | Workflow §5.6 |
| `rc.2` is the only pre-release label that fits: `beta.N` sorts below `rc.1`, a stage never goes back (Workflow §3.2), and `cd.yml` refuses a pre-release after its stable version. | Workflow §3.2, `cd.yml` |
| Workflow §3.3 says "`v1.0.0-rc.N` — only fixes". `rc.2` carries features. **Needs the owner's word** that §3.3 is amended; the agent does not edit `Development-Workflow.md` (it is the owner's guide). Proposed wording is in §6. | Workflow §3.3, §10 |
| `rc.1` is published on NuGet and cannot be removed, only hidden. Whatever `rc.2` changes in public API is a change against a published pre-release, which §5.6 allows, but it goes into the release note. | Workflow §1.5 |
| The R9 sequence "rc → defects only → release" is replaced for this release by: `rc.2` stages → conformance audit of `rc.2` (§4, P8) → fixes → `rc.3` if needed → release. | Rework-Plan R9 |

`rework/` stays as it is (historical). This plan does not edit it; the decisions of §3 are recorded
here and in `rework/Owner-Review.md` by the owner, if they want the log continued.

---

# 1. R9e — the closure check, verified

The report is `internal/Audit-Conformance.md` §12 (written in the R9e session, commit `e8af35b`,
2026-09-29); `v1.0.0-rc.1` was tagged on `c728c99`, which is `release/v1.0.0` HEAD and is on origin.

| Check | Result |
|---|---|
| Seventeen R9c findings | CONF-01…15 and CONF-17 closed with evidence (code, contract section, QA checkpoint, test); CONF-16 stays open by design |
| Two new findings | CONF-18 (Progress row), CONF-19 (a compiler warning in a test file) — both weight "recorded" |
| Verdict "the rc may be tagged" | Present (§1, §12.5); no finding of weight "blocks the rc" or "fix before release" is open |
| CD dry run | `dotnet pack` × 3 with `-p:Version=1.0.0-rc.1`: three `.nupkg`, two `.snupkg`, a non-empty README in each (§12.1) |
| Independent re-run in this session | `dotnet test … --configuration Release`: **2 014 passed, 0 failed**; working tree clean; the tag points at HEAD |

What is still wrong after R9e — all small, all in P0:

- `Rework-Plan.md` Progress: row **R9e** says `not started` and the **Release** row is empty (CONF-18's
  successor); R9d's row names only one of its two branches (`bugfix/v1-audit-all`; the second was
  `bugfix/v1-strict-utf8-keys`, merge `e8af35b`).
- `CLAUDE.md` "Current state" still says "the closure check (R9e) comes before `v1.0.0-rc.1`" — the rc is
  tagged.
- Seven compiler warnings remain in the test project (`CS0414`, `CS8631`, `CS8604`, `CS8602`, `xUnit2013`
  ×2, `xUnit2028` — CONF-19 and the six of R9c). The packages have none.
- The `rc.1` baseline of `Rework-Plan.md` R9 step 7 was not taken: `Baselines/` holds only `pre-rework`.
  This is the owner's step (a worktree on the tag, a `benchmark/` branch); P0 only reminds.
- CONF-16 (Track B adapters) is open; it becomes stage P6.

---

# 2. Inventory — everything that was deferred, with where it was written

"Detail" is what the documents already fix; "open" is what they leave to a decision in §3.

| # | Item | Written in | Detail the documents give | Open | Stage |
|---|---|---|---|---|---|
| I1 | State sync after rc.1 | `Audit-Conformance.md` §12.3 | Progress rows, `CLAUDE.md` state, CONF-19 | — | P0 |
| I2 | Name of the contract's read methods, member calls | owner, 2026-10-09 | `TypeContract<T>.Read` (positional) vs `ReadField` (keyed); `MemberWriter.Member`/`Field`; `MemberReader.Member`/`Value` | **N1** | P1 |
| I3 | Public generator seam: `TypeContract<T>`, `MemberWriter`, `MemberReader` | `Decisions.md` §8.8; `Rework-Plan.md` §13.1 | Shape of a method, abstract class so defaults can be added; engine checks every call (§8.4); seam is `internal` today; `PublicSurfaceTests.EngineTypes_AreNotPublic` pins it | **S1** | P2 |
| I4 | Registration of generated contracts | `Decisions.md` §8.8; §13.1 | One lookup place: `TypeContractCache.Get`; "generated registered for T → use it, else `ReflectedContract<T>`"; bytes identical | **S2** | P2 |
| I5 | Non-public `[BinaryInclude]` members under a generated contract | `Decisions.md` §8.8 | "`partial`, or `[UnsafeAccessor]`" | **S3** | P2/P3 |
| I6 | `ViShap.Viper.Generator` | `Rework-Plan.md` §13.1; `Audit-Future.md` §5, §8-C, §9; contract §14.1 | Roslyn incremental generator, `netstandard2.0`, fourth package, analyzer in `analyzers/dotnet/cs`, build-time only, `Serialization` never references it; emits only `TypeContract<T>` (Create, Write, Read/ReadField, description, registration), never a length, count, loop, limit, null frame, union tag or byte; every runtime rejection of `ReflectedContract<T>` becomes a compiler diagnostic; repository gains the project, a snapshot-test project, `CONF-*` run a second time, CD packs four packages | **S4** (scope), **S5** (bundling) | P3, P4 |
| I7 | AOT honesty of the generated path | `Rework-Plan.md` §13.1 "the generated path carries none" | — | **S4** — see the warning there | P3 |
| I8 | Schema fingerprint | `Decisions.md` D9.2; `Rework-Plan.md` §13; `Owner-Review.md` P10 | "A new critical service with a new number" (4); an additional check, not a format change | **F1** (what is hashed, which layouts, which error) | P5a |
| I9 | Zstandard, LZ4, AES-GCM-SIV | `Decisions.md` D9.2, D9.12; `Rework-Plan.md` §13; `Audit-Future.md` §2 | Separate packages implementing the Core `I*Algorithm` interfaces (exact `GetCiphertextLength`, `HashSizeInBytes`, no default members except additive ones); `Custom = 255` carries a name | **A1** (packaging, ids), dependency choice | P5c–e |
| I10 | Live tracing: `WithTrace(sink)` | `Rework-Plan.md` §13 (owner's note 2026-09-29) | Options switch; sink receives the `BinaryDump` of each operation; needs the trace seam on the write side too; the observer itself stays internal | **T1** | P5b |
| I11 | Executable documentation examples | `Rework-Plan.md` §13 | Test in `Serialization.Tests` that extracts every `csharp` block of `docs/` and READMEs, compiles with Roslyn, runs the ones with top-level statements; one new test dependency; CONF-15 fixed the wording | — | P5f |
| I12 | Track B adapters | `Decisions.md` D9.25; CONF-16; `Benchmark-Plan.md` §5.2, §29.2 | `benchmark/track-b-adapters`; seven mandatory libraries (Viper, MemoryPack, MessagePack-CSharp, protobuf-net, Nerdbank.MessagePack, Orleans.Serialization, System.Text.Json), model variants (§7.4), probes (§7.5), verification (§7.6), tier tables (§6) | **B1** (when it is measured) | P6 |
| I13 | Benchmark graceful stop | `Benchmark-Graceful-Stop.md` | Variant C decided 2026-09-27; stop between suites, recovery from exports; branch `benchmark/graceful-stop` | — | P6 |
| I14 | Open performance proposals | `performance/PERF-02…PERF-09` | PERF-02 (byte budget), PERF-03…08 (measured costs), PERF-09 (trace seam ≈ 1.5 %) — each "Open", the owner decides | **B2** | P6 |
| I15 | Performance in README / `docs/` | `Rework-Plan.md` §13; `Benchmark-Plan.md` §28 | Charts from `Baselines/v1.0.0/` after the §28 gate is green; no adjective before | **B1** | P7 (gated) |
| I16 | `rc.1` and `v1.0.0` baselines | `Rework-Plan.md` R9 steps 7, 10 | Taken from a worktree on the tag, branch `benchmark/<tag>-baseline` | **B1** | owner |
| I17 | Second conformance run | `Rework-Plan.md` §13.1, R8 | `Contracts/ConformanceTests` reaches a contract through the single seam `ContractOf<T>`; run unchanged against generated contracts | — | P4 |
| I18 | CD / CI for new packages | `cd.yml` (`EXPECTED_COUNT=3`); `ci.yml` | Pack and validate every new package | — | P3, P5 |

**Recorded, not scheduled** — the documents name them but do not give enough to plan, or say "not
claimed". Each needs a design brief from the owner before it can become a stage:

| Item | Written in | Why it is not a stage |
|---|---|---|
| Preservation of unknown keyed fields | `Audit-Future.md` §7 | Contradicts §16.2 (reference scopes assume a skipped field takes part in nothing); the interaction with `PreserveReferences` must be designed first |
| CLI, and parsing a frame without the CLR type "by schema" | `Decisions.md` §9.32 "after the release" | The wire carries no type description; "by schema" needs a schema source nobody has specified |
| Public formatter contract | contract §21.3, `Rework-Plan.md` §15 | Publishing it freezes the traversal protocol |
| Per-field compression, V2 envelope | `Audit-Future.md` §7 | New wire rules and a new decompression-bomb surface |
| Constant-time checksum comparison, async engine | contract §21.3 | Deferred by design, not claimed |

---

# 3. Decisions for the owner

Each: every option, its cost, why the recommended one is better, a short example. A decision made is
recorded in the table of §3.9 by the owner.

## N1 — Naming of the contract's read side and member calls

Today `TypeContract<T>` has `Write`, `Read` (positional, all members in plan order) and `ReadField`
(keyed, called by the engine once per field on the wire). Both read members; the names do not say so.
Two questions, independent of each other.

**N1a — the two read methods**

| | Names | Cost | Verdict |
|---|---|---|---|
| A | `ReadMembers` / `ReadMember(key)` | Reads naturally | Differ by one letter; easily mistyped and misread in a `switch` |
| **B** | `ReadPositional` / `ReadKeyed` | Mirrors `MemberLayout.Positional` / `Keyed`, the vocabulary of the whole contract; the pair is visibly one concept | `ReadKeyed` does not say it reads one field — the XML doc and the `key` parameter do |
| C | `ReadMembers` / `ReadMemberByKey` | Self-explanatory, no collision | Longest; the layout words used everywhere else are lost |
| D | keep `Read`, rename only `ReadField` → `ReadMemberByKey` | Smallest change | Keeps the asymmetry the owner objected to |

**N1b — the calls on `MemberWriter` / `MemberReader`**

| | Writer / reader | Cost | Verdict |
|---|---|---|---|
| **M1** | writer `Member(value)` and `Member(key, value)`; reader `Member<T>()` for both layouts (`Value<T>()` goes) | One verb for "a member's value"; "field" stays the wire word (`MaxKeyedFields`, the field window, `int32 length`), owned by the engine, which the contract never sees | The layout is no longer visible in the call; it is still checked at run time (`WrongLayout`), as it is now |
| M2 | keep `Member` / `Field` / `Value` | No change | The same member is a "Member", a "Field" and a "Value" depending on layout |

Recommended: **B + M1**. Example, the sketch of `Rework-Plan.md` §10.2 after the change:

```csharp
public override void Write(ref MemberWriter w, in Person v)
{
    w.Member(1, v.Name);          // positional: w.Member(v.Name)
    w.Member(2, v.Age);
}

public override void ReadPositional(ref MemberReader r, ref Person v) => v.Age = r.Member<int>();

public override bool ReadKeyed(ref MemberReader r, int key, ref Person v)
{
    switch (key)
    {
        case 1: v.Name = r.Member<string>(); return true;
        case 2: v.Age = r.Member<int>(); return true;
        default: return false;
    }
}
```

The chain (P1) is the rename's whole blast radius: `TypeContract<T>`, `ReflectedContract<T>` and
`MemberAccessor<T>.ReadFieldFrom`, `MemberReader`/`MemberWriter` and their mismatch messages,
`ObjectCodec`, the tests that name them (`ContractCallTests`, `StructuralBarrierTests`,
`ConformanceTests`, `PublicSurfaceTests`), contract §2.4/§14, QA-Plan, `CLAUDE.md`, the skills that
quote them. `docs/` and the READMEs do not mention them. `rework/` and `Audit-*.md` are historical and
are left as written; the renames go into the ledger of §7.

## S1 — How much of the seam becomes public

| | Option | Cost | Verdict |
|---|---|---|---|
| **a** | Publish exactly `TypeContract<T>`, `MemberWriter`, `MemberReader`, `MemberDescription`, `MemberLayout` (constructors protected or private as today) | The seam was designed as the façade: no bytes, no counts, no position, every call checked (INV-2, INV-5). Frozen at `v1.0.0` — adding members later is possible only with defaults, which the abstract class allows | Smallest surface that works; nothing of the codecs or shapes is published, so the traversal protocol stays free |
| b | A narrower public façade (new public types the generator targets); the engine adapts to the internal contract | Two layers to keep identical; every future change is made twice | Only worth it if (a) is judged too wide; it adds a type to explain, not a guarantee |
| c | Generator emits only the member *description* (data), the engine runs it | No public method seam | Already rejected in `Decisions.md` §8.1: data cannot express straight-line code, so the generator gains nothing over reflection |

## S2 — How the engine finds a generated contract

| | Option | Example | Cost | Verdict |
|---|---|---|---|---|
| a | `[ModuleInitializer]` calls `ContractRegistry.Register(...)` | zero configuration | A process-wide mutable registry — the thing INV-7 ("no process-wide mutable registry can change what an algorithm is") exists to avoid, and the thing `AlgorithmCatalog` was built not to be; timing depends on when the consumer's module initializes; unload and test isolation are awkward | Easiest to use, hardest to defend |
| b | `static abstract` member on the type (`IBinaryContract<TSelf>`) | `where T : IBinaryContract<T>` or a runtime interface lookup | A generic constraint excludes every type that is not generated; a runtime lookup is reflection again | Does not fit a library that serializes types it does not own |
| **c** | A context in the options, like `JsonSerializerContext`: `[BinaryContext]` on a partial class lists the types; `options.WithContracts(AppContracts.Default)` | `Serialize(person, options)`; types not in a context fall back to `ReflectedContract<T>` | One line of configuration; options stay immutable snapshots; no global state; the shape that extends to S4-B (a context that also carries codecs) | Recommended. A forgotten `WithContracts` silently uses reflection — answered by an analyzer hint and an opt-in `RequireGeneratedContracts()` that makes the fallback a `BinaryConfigurationException` |

## S3 — Non-public `[BinaryInclude]` members under a generated contract

| | Option | Cost | Verdict |
|---|---|---|---|
| a | Require `partial` and emit inside the type | Access to everything; the type must be partial and the owner of the source | Cannot cover a type the consumer does not own |
| **b** | Public members by plain access; non-public by `[UnsafeAccessor]` (net10 supports it for fields, properties and methods, generic owners included) | Generated code stays outside the type; works under S2-c; no `partial` | Recommended; verify in P3 that every eligible member shape (init-only, struct owner by `ref`) is reachable, and fall back to a diagnostic where one is not |
| c | Refuse non-public members in generated mode (diagnostic, the type uses reflection) | Simple | Loses the speed for exactly the types that use `[BinaryInclude]` |

## S4 — What the generator is for (scope) — read before deciding

`Rework-Plan.md` §13.1 says "the reflection path's `[RequiresDynamicCode]` annotations stay, and the
generated path carries none." **Checked against the code, that is true only for the *member* half.**
`FormatterRegistry` builds every codec of the declared type graph with `MakeGenericType` and
`Activator.CreateInstance` (`Engine/FormatterRegistry.cs:186-287`), and `ReflectedContract` builds
accessors with `Expression.Compile` (`Engine/Contracts/ReflectedContract.cs:33-166`). A generator that
emits only `TypeContract<T>` removes the second and not the first. `BinarySerializer.Serialize<T>` would
still carry `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`, and `tests/ViShap.Viper.AotConsumer`
would still report them. Hence:

| | Scope | What a consumer gets | Cost |
|---|---|---|---|
| **A** | Contracts only (as §13.1) | No `Expression.Compile`, no member reflection; faster first use and steady state; compile-time diagnostics for every contract error; trim-friendly members. **Not** native-AOT-clean: the entries stay annotated and say so | The seam of S1; no codec is published |
| B | Contracts and the codec graph: the context also emits the closed codec for every reachable type, and the entries that take a context carry no annotation | Native AOT without warnings | Publishes the codec/shape layer or a façade over it — the traversal protocol §13.1 and `Audit-Future.md` §7 keep internal; a type graph walker in the generator; a second set of entry points |
| C | A now, B as a later minor, with the context type designed in S2-c so B is additive | A's value now, a path to B without breaking anything | B is not done before `v1.0.0` |

Recommended: **A**, with the claim in §13.1 and the docs corrected to say what is true (the generator is
about speed, start-up and compile-time diagnostics; AOT annotations stay until B), and S2-c chosen so B
fits later. If the owner wants native AOT in `v1.0.0`, B becomes a stage of its own, larger than the
generator, and `viper_generator` gets a design stage before any code.

## S5 — Does the meta-package bundle the generator

| | Option | Cost | Verdict |
|---|---|---|---|
| **a** | `ViShap.Viper` references it with `PrivateAssets="all"` (a development dependency) | One install gives everything; the generator is inert until a `[BinaryContext]` exists | Matches §13.1 "if the owner chooses to bundle it" |
| b | Separate opt-in package only | The consumer who wants speed must know to add it | Safer for consumers who reject analyzers in the build |

## F1 — The schema fingerprint

The documents give only "a new critical service with a new number" and the questions of
`Owner-Review.md` P10 (does a renamed class break the fingerprint? nested and generic types? a keyed
contract exists precisely to evolve).

| | What is hashed | Cost | Verdict |
|---|---|---|---|
| a | Ordered member names and CLR type names | Detects everything | A rename breaks it though the wire is compatible; platform-dependent type names |
| **b** | The *wire shape* of the root type: for a positional contract the ordered sequence of member wire kinds; for a keyed contract nothing (evolution is the point); no CLR names | Stable across renames; computed from the contract description, so the reflected and the generated contract give the same value (INV-12) | Recommended. Catches the case it exists for — reading a positional payload as the wrong type — and says so |
| c | A user-declared number, `[BinarySchema(3)]` | Human-controlled, trivially stable | Catches nothing automatically |

Also to decide with it: the error (recommend `BinaryFormatException`, "schema mismatch", carrying both
values — not an integrity error, since nothing was tampered with), and whether the check is opt-in
(`WithSchemaFingerprint()`, recommended; default writes nothing) or on by default (a wire change for
every frame, major under §5.2).

## A1 — Zstandard, LZ4, AES-GCM-SIV

| | Packaging | Cost | Verdict |
|---|---|---|---|
| **a** | One package each (`ViShap.Viper.Compression.Zstd`, `….Lz4`, `ViShap.Viper.Encryption.AesGcmSiv`), registered through the existing custom-algorithm builder methods; the header carries the algorithm's name (`Custom`, id `FF 01`) | Each has its own dependency and cycle; no change to Core | Matches `Decisions.md` D9.2 "separate packages" and `Audit-Future.md` §9 "a package exists when it has its own public contract and cycle" |
| b | One `ViShap.Viper.Extras` | One package to ship | Pulls every dependency into every consumer |
| c | Reserve built-in ids in Core's enums for them | Short header record | An enum value with no implementation in Core is a trap; reverses D9.12's "built-ins are in Serialization" |

Dependencies to verify in the stage, not assumed here: a managed Zstandard and LZ4 implementation with a
maintained release; AES-GCM-SIV is not in the BCL, so a library that provides it must be found and its
test vectors (RFC 8452) used. If none is acceptable, AES-GCM-SIV is dropped from this release and the
reason recorded. Each package implements the interface exactly as Core states it (exact ciphertext
length, hash size 1…255, `expectedLength` produced exactly) and the services check it.

## T1 — Live tracing

`WithTrace(sink)`: the sink receives the `BinaryDump` of each operation. Needs the trace seam in the
write codecs (today only read codecs report). Cost is known from `PERF-09`: the switched-off seam costs
≈ 1.5 % on reads. Questions: is the sink called on failure with the partial dump (recommend yes, as
`Dump<T>` does); is it called under a limit of its own (recommend the options' limits, as the dumper);
does it ever see a key (never, as the dumper). Alternative: do nothing and keep the documented
`catch` + `Dump<T>` pattern.

## B1 — When Track B is measured, and the performance claim

A baseline belongs to a tagged revision (`Benchmark-Plan.md` §29.1, BASE-08), and §28's box "the v1.0.0
baseline is frozen" can only close after `v1.0.0`.

| | Option | Cost | Verdict |
|---|---|---|---|
| **a** | Adapters before `v1.0.0` (P6); Track A and B measured on `v1.0.0` as planned; README/`docs/` performance after | The plan as written | No rule bent |
| b | Measure A and B on `v1.0.0-rc.2` and record `Baselines/v1.0.0-rc.2`; README performance still waits for the §28 box on `v1.0.0` | Numbers earlier, from a tag that is not the release | Fine for information; cannot publish |
| c | Amend §28 so an rc baseline can open the gate | Performance claims in the `v1.0.0` README | The owner's rule "no adjective before the numbers" is kept only if the numbers are the release's |

## B2 — The open performance proposals

`PERF-02…PERF-09` are all "Open", each ending where the owner's decision begins. They are not
scheduled until decided; P6 lists them for a single sitting. `PERF-02` (byte budget) touches the public
limits and is the only one that could change behavior.

## 3.9 Decisions taken

Filled in by the owner.

| | Decision | Date |
|---|---|---|
| N1a / N1b | B + M1 (recommended) | 2026-10-09 |
| S1 / S2 / S3 | a / c / b (recommended) | 2026-10-09 |
| S4 / S5 | A / a (recommended) | 2026-10-09 |
| S1 base type | `TypeContract<T>` implements an internal interface; exactly the five types of S1-a are public | 2026-10-09 |
| S2 context | abstract `BinarySerializerContext`, contracts added in its constructor through `protected Add<T>`, snapshotted by `Build()`; `[BinaryContext(typeof(…))]` in Core lists the root types | 2026-10-09 |
| S2 type coverage | the listed types and every member-encoded type reachable from them through members, element and key/value types, `Nullable<T>` and `[BinaryUnion]` arms | 2026-10-09 |
| Invalid description | `BinaryConfigurationException` from the public constructors and from `Add<T>` | 2026-10-09 |
| P1–P4 on one branch | `feature/generator`; the order inside it is kept, the gates are checked together at the end | 2026-10-09 |
| F1 | | |
| A1 | | |
| T1 | | |
| B1 / B2 | | |
| Workflow §3.3 amended | | |

---

# 4. Stages

Each stage is one branch from `release/v1.0.0`, cut **after the previous stage is merged into it**
(`Development-Workflow.md` §2.5); the owner commits and merges. "Gate" means the stage may be handed over
only when it holds. Every stage ends with the suite green in Debug and Release, the benchmark harness
building (`--verify`, `--smoke`) where the stage touches code it measures, and the documents the stage
made untrue corrected **in the same change** (`CLAUDE.md`, contract, QA plan, `docs/`, READMEs, the skills'
quotations).

| Stage | Branch | Skill | Needs decision |
|---|---|---|---|
| P0 State sync | `docs/rc1-state-sync` | `viper_builder` | — |
| P1 Naming | `feature/generator` (one branch for P1–P4, owner's decision) | `viper_generator` | N1 — taken |
| P2 Public contract seam | `feature/generator` | `viper_generator` | S1–S4 — taken |
| P3 Generator | `feature/generator` | `viper_generator` | S2–S5 — taken |
| P4 Generator tests | `feature/generator` | `viper_generator`, `viper_tester` | — |
| P5a Schema fingerprint | `feature/schema-fingerprint` | `viper_builder` | F1 |
| P5b Live tracing | `feature/trace-sink` | `viper_builder` | T1 |
| P5c Zstandard | `feature/zstd-package` | `viper_builder` | A1 |
| P5d LZ4 | `feature/lz4-package` | `viper_builder` | A1 |
| P5e AES-GCM-SIV | `feature/aes-gcm-siv-package` | `viper_builder` | A1 |
| P5f Executable examples | `test/docs-examples` | `viper_tester` | — |
| P6 Benchmarks | `benchmark/track-b-adapters`, `benchmark/graceful-stop`, `benchmark/generator-profiles` | `viper_bencher` | B1, B2 |
| P7 Documentation | `docs/rc2-docs` | `viper_builder` | — |
| P8 Audit of rc.2 | `audit/rc2-conformance`, then `audit/rc2-closure` | `viper_conformance_auditor` (new brief) | — |

P5a–P5f are independent of each other and of P3; the order above is a default, the owner may reorder. P5a
follows P4 because the fingerprint is computed from the contract description, which the reflected and
the generated contract must produce identically.

## P0 — State sync — `docs/rc1-state-sync`

- `Rework-Plan.md` Progress: R9d row names both branches; R9e `closed` with the merge of
  `audit/v1-conformance-closure` (`c728c99`); Release row stays `not started`. (The rework's own file;
  this is the Progress update its rules require.)
- `CLAUDE.md` "Current state": `v1.0.0-rc.1` tagged; the closure check is done; the next work is this plan.
- The seven test-project warnings fixed in the tests (`Hostile/CanonicalScalarTests.cs:55` and the six of
  R9c), so the test project builds clean too.
- A line in the hand-over reminding the owner of the `rc.1` baseline (R9 step 7) if they still want it.
- **Gate:** build without a warning in any project; suite green; no behavior change.

## P1 — Naming — `rework/p1-contract-naming`

The chain of N1, in one change so no intermediate state names two things differently: `src/` (the
contract, the reflected contract and its accessors, the reader and writer and the text of their
mismatch exceptions, the object codec), every test that names them, the contract (§2.4, §14), the QA plan
(checkpoints that quote the names), `CLAUDE.md` ("Member layouts", "Adding a formatter"), the skills.
Search before and after for every old name; the ledger of §7 records each. No wire byte changes: the
frozen fixtures and `CONF-*` are the arbiter.

- **Gate:** the search for the old names is clean outside historical documents; `PublicSurfaceTests`,
  `CONF-*`, `ContractCallTests` green; fixtures untouched.

## P2 — Public contract seam — `feature/public-contract-seam`

- S1: the chosen types become public with the full XML documentation a consumer reads on hover: what
  each member does, what it takes, which exception it raises; no reference to `internal/`, no history.
  The exposed types keep every guarantee INV-2/INV-5 states: no byte, no count, no position.
- S2: the single lookup `TypeContractCache.Get` consults what the options carry (S2-c) before
  `ReflectedContract`; the options gain `WithContracts(...)` (and `RequireGeneratedContracts()` if chosen);
  `Build()` validates them. No static registry unless S2-a is chosen, in which case INV-7 is restated and a
  structural test added.
- S3: whatever the chosen access rule needs in the description (for example a flag per member).
- Contract: §3 (surface) and §3.6 (member surface, enforced by `Api/MemberSurfaceTests`), §14.1, §2.4;
  `PublicSurfaceTests` updated (`EngineTypes_AreNotPublic` no longer lists the seam types); QA group for
  the seam; `docs/contracts.md` gains the section a hand-written contract needs (the seam is usable
  without the generator).
- **Gate:** a hand-written `TypeContract<T>` in a test, registered through the new path, passes the whole
  `CONF-*` suite; the AOT analysis test still reports exactly what it reported (no new warning, no new
  suppression); `CLAUDE.md` barrier text and "Adding a formatter" updated.

## P3 — Generator — `feature/generator`

Governed by `viper_generator`. `src/ViShap.Viper.Generator` (`netstandard2.0`, `IIncrementalGenerator`,
`EnforceExtendedAnalyzerRules`, packed to `analyzers/dotnet/cs`, `DevelopmentDependency`, no runtime
reference from `Serialization`), producing per type exactly one `TypeContract<T>`: the member description,
`Create`, `Write`, `ReadPositional` or `ReadKeyed` (names per N1), and the registration of S2. Member
order, keys, inheritance levels (base first), shadowing and override handling reproduce
`ReflectedContract` rule for rule — the rules are contract §14, `Audit-Closure.md` NX-03…NX-05, and the
total order of INV-12.

- **Diagnostics.** One compiler diagnostic per rejection `ReflectedContract` makes at first use today
  (list in §13.1: unmarked member of a contract, `[BinaryKey]` without `[BinaryContract]`, duplicate keys
  or orders, `[BinaryKey]` with `[BinaryIgnore]`, `[BinaryInclude]` with `[BinaryIgnore]`, `[BinaryOrder]`
  or `[BinaryInclude]` on a contract, a delegate member, a union tag above 255, an abstract type with no
  union, a duplicate union tag, a known type not assignable to the base). Ids `VPR001…`, documented in
  `docs/` with a fix for each; the runtime keeps rejecting the same things for a type without a generated
  contract.
- **What the generator never emits:** a length, a count, a loop over wire data, a limit, a null or
  reference frame, a union tag, a byte. A structural test (P4) reads the emitted source for them.
- **Packaging and delivery.** Solution entry; `ci.yml` builds it; `cd.yml` packs and validates four
  packages (`EXPECTED_COUNT`, the pack steps, the artifact-name check) — dry-run with `-p:Version=1.0.0-rc.2`;
  `GENERATOR-README.md`; the meta-package per S5.
- **Contract:** a generator section (what is generated, the guarantee of byte identity, the diagnostics).
  `Rework-Plan.md` §13.1's AOT sentence is corrected in the contract and the docs per S4.
- **Gate:** every `CONF-*` case passes on a generated contract (P4 runs it); the frozen fixtures read and
  reproduce through generated contracts; the package packs; the consumer sample builds with the generator
  and without it and writes identical bytes.

## P4 — Generator tests — `test/generator-conformance`

Governed by `viper_tester` with `viper_generator`. A test project for the generator (snapshot tests of the
emitted code, one per object shape and one per diagnostic id); `Contracts/ConformanceTests` run a second
time against generated contracts through the same `ContractOf<T>` seam; frozen-fixture cross-check;
a property-style test that reflected and generated contracts produce identical bytes for the whole corpus
types; structural tests for "emits no loop, length, limit"; QA-Plan gains a GEN group with checkpoints
citing contract sections.

- **Gate:** QA-Plan GEN group fully ticked with passing tests; snapshots committed; both CONF runs green.

## P5a–P5f — Additive features

Each is one branch and follows the same pattern: contract section first (what it promises, what
exception for what), checkpoint list in the QA plan, tests, XML docs, `docs/` page or section, README
line, `CLAUDE.md` if an architectural sentence changes.

- **P5a Fingerprint (F1).** A new critical service, number 4, in the header's ascending order after
  encryption; at most 4 096 bytes still; written only when asked; read: if present and the reader's
  expectation differs, the chosen error. V1 only (V0 has no header). The reflected and generated contracts
  must produce the same value. Wire §22 gains the record; new frozen fixtures are **added**, none
  regenerated (`CLAUDE.md`: fixtures are frozen).
- **P5b Tracing (T1).** The write-side trace seam in the codecs; `WithTrace(sink)`; the cost measured
  against `PERF-09` and recorded as a new `PERF-nn` if it is more than the read side's.
- **P5c–e Packages (A1).** Per package: `src/…`, a test project with the published vectors, its README,
  CD steps, an entry in `docs/algorithms-and-keys.md`. Each implements the Core interface exactly and is
  held to it by the services. Anything that needs a native dependency is refused (`Rework-Plan.md` §15).
- **P5f Examples.** A test in `Serialization.Tests` extracting every `csharp` block of `docs/` and the
  READMEs, compiling it with Roslyn against the built assemblies, running those with top-level statements
  and compiling the others as a library — the CONF-15 wording. One new test-only dependency.
- **Gate (each):** suite green; the new frozen/added fixtures read; `PublicSurfaceTests` and
  `MemberSurfaceTests` list the new surface; documents consistent.

## P6 — Benchmarks — `viper_bencher`

- `benchmark/graceful-stop`: variant C of `Benchmark-Graceful-Stop.md`, done-when list included.
- `benchmark/track-b-adapters` (CONF-16): an adapter for each library of `Benchmark-Plan.md` §5.2 in its
  documented best mode, the model variants (§7.4), the capability probes (§7.5), verification before
  timing (§7.6), the tier tables (§6) generated from probes. Only the public surface is used for
  comparison; versions re-verified on the day of the run (RST-03). The work is read-only over `src/`.
- `benchmark/generator-profiles`: a profile for the generated contract beside the reflected one (cold start,
  first use, steady state) in the profile matrix (§8), so the generator's claim is a measured one.
- A single sitting on `PERF-02…PERF-09` (B2): each accepted, rejected or superseded in the file itself.
- **Gate:** harness builds; `--verify` passes every pair; `--smoke` passes; no `src/` file edited; nothing
  measured or published as a result before B1 says where.

## P7 — Documentation — `docs/rc2-docs`

`docs/` and the package READMEs (one more per new package): a generator page, the new algorithm packages, schema
fingerprint, tracing, the contract-authoring page; every `csharp` block compiles and runs (P5f enforces).
No adjective about performance (`Benchmark-Plan.md` §28) — the performance page waits for the gate (B1).

## P8 — Audit of `rc.2`

A new auditor brief (`internal/rc2/Audit-Brief.md`, written in P7 from the decisions taken and this plan,
for the owner to approve) and a separate session of `viper_conformance_auditor` per `audit/rc2-conformance`;
fixes on `bugfix/rc2-audit-<topic>`; a closure session on `audit/rc2-closure`; a CD dry run of **all
packages** with `-p:Version=1.0.0-rc.2`; then the owner tags `v1.0.0-rc.2` on `release/v1.0.0`. Further
defects: `bugfix/` and `rc.3`. The release follows the sequence of `Rework-Plan.md` R9, steps 8–11.

---

# 5. Rules for whoever executes this

1. One stage at a time; a stage starts only when the previous one is merged and the suite is green at entry.
2. **A stage whose decision (§3) is blank does not start.** Ask — with every option, its cost, why the
   recommendation is better, a short example — and wait.
3. The wire does not change except where a stage says so (P5a). The frozen `Fixtures/Wire/*.bin` are never
   regenerated; a failure there is a compatibility break to be fixed in `src/`. New features add fixtures.
4. The structural barriers and INV-1…INV-18 hold by construction; a stage that would weaken one stops and
   asks (`CLAUDE.md`, "Three structural barriers"). The generator in particular may not become a second
   place where a length, a count or a loop over wire data exists.
5. Public surface is XML-documented for the NuGet consumer; no comment addresses the reader, records
   history, or cites `internal/`.
6. Documents change with the code, in the same change; a retired or renamed name goes into the ledger (§7)
   and is searched for.
7. The owner commits. Never commit, push, open a PR or tag. Every stage ends with the changed paths, the
   evidence of the gate, the owner's next commands for the stage's cycle, and a ready commit message
   (one subject line, optionally a short clause after a dash; no body, no trailer).
8. Measure before claiming; benchmarks never change the library (`Benchmark-Plan.md` §1).
9. A discovery that contradicts a decision is reported to the owner with the evidence; it is not
   resolved by choosing.

---

# 6. Changes the owner's guide needs (not made by the agent)

`Development-Workflow.md` binds every agent and is edited only on the owner's request. For this plan to
run as written it needs:

1. §3.3 "after R9e — `v1.0.0-rc.N` — fixes only": a sentence that for `v1.0.0` the rc carries the
   features of `internal/rc2/RC2-Plan.md`, and that `rc.2` is audited as the R9 rc was.
2. §2.1: the skills `viper_builder` and `viper_generator` in the *Skill* column for `feature/`, `bugfix/`
   and `rework/` branches, and `viper_conformance_auditor` for the rc.2 `audit/` branches.
3. §10: nothing to change.

---

# 7. Ledger of names retired or renamed by this plan

Filled by the stage that renames; each row is searched for in `src/`, `tests/`, `benchmarks/`, the XML
documentation, `docs/`, the READMEs, `CLAUDE.md`, the skills and the living documents of `internal/`.

| Stage | Was | Now | Searched as | Result |
|---|---|---|---|---|
| P1 | `TypeContract<T>.Read` | `TypeContract<T>.ReadPositional` | `override void Read(`, `.Read(ref members` | clean |
| P1 | `TypeContract<T>.ReadField` | `TypeContract<T>.ReadKeyed` | `ReadField\b`, `ReadField_` | clean outside historical documents |
| P1 | `MemberWriter.Field<TMember>(key, value)` | `MemberWriter.Member<TMember>(key, value)` | `.Field(`, `Field<TMember>` | clean; `MemberReader.Field` (the engine's internal factory that opens one field) and `WireTrace.Field` keep the wire word |
| P1 | `MemberReader.Value<TMember>()` | `MemberReader.Member<TMember>()`, one call for both layouts | `.Value<`, `Value<TMember>` | clean |
| P1 | `MemberAccessor<T>.ReadFrom` / `ReadFieldFrom` | `ReadPositionalFrom` / `ReadKeyedFrom` | `ReadFieldFrom`, `.ReadFrom(ref reader` | clean (`BinaryFormatHeaderV1.ReadFrom` is unrelated) |
| P1 | `Contracts.WrongLayout(…, write)` on the read side | write side only: a reader's layout is the one the engine opened it for | `WrongLayout(` | one caller, `MemberWriter` |
| P2 | `ViShap.Viper.Engine.TypeContract` (internal abstract base) | `ViShap.Viper.Engine.ITypeContract` (internal interface `TypeContract<T>` implements) | `TypeContract contract`, `typeof(TypeContract)`, `(TypeContract)` | clean |
| P2 | `ViShap.Viper.Engine.TypeContract<T>`, `MemberWriter`, `MemberReader`, `MemberDescription`, `MemberLayout` (internal) | the same names, public, in `ViShap.Viper.Contracts` | `ViShap.Viper.Engine.TypeContract`, `ViShap.Viper.Engine.Member` | clean outside historical documents |
| P2 | `ViShap.Viper.Engine.Contracts` (static diagnostics class) | `ContractCalls` — the old name would shadow the new namespace | `Contracts.Mismatch`, `Contracts.WrongLayout`, `Contracts.TooManyCalls` | clean |
| P2 | `StructuralCodec<T>.FoldsNull` (property) | `FoldsNull(ref OperationState)` — an object's null follows the contract in force for the operation | `FoldsNull =>` | clean |
| P3 | `SourceTree.ProductionFiles` over every file of `src/` | over the shipped runtime assemblies only; the generator is held to its own tests | `ProductionFiles` | the source invariants still cover Core and Serialization |
| P1 | test `ReadField_APositionalMemberUnderAKeyedLayout_ThrowsType` | `ReadKeyed_AMemberCallPerKnownKey_ReadsEachFieldsMember` — the mistake is no longer expressible | `APositionalMemberUnderAKeyedLayout` | clean |

---

# 8. Progress

Updated by the executor when a stage's gate holds; a stage is `closed` only after the owner has merged
its branch into `release/v1.0.0`.

| Stage | Branch | Status | Closed by (merge commit) |
|---|---|---|---|
| Decisions of §3 | — | N1, S1–S5 taken; F1, A1, T1, B1, B2 open | |
| P0 State sync | `docs/rc1-state-sync` | not started | |
| P1 Naming | `feature/generator` | gate holds — awaiting commit | |
| P2 Public contract seam | `feature/generator` | gate holds — awaiting commit | |
| P3 Generator | `feature/generator` | gate holds — awaiting commit | |
| P4 Generator tests | `feature/generator` | gate holds — awaiting commit | |
| P5a Schema fingerprint | `feature/schema-fingerprint` | not started | |
| P5b Live tracing | `feature/trace-sink` | not started | |
| P5c Zstandard | `feature/zstd-package` | not started | |
| P5d LZ4 | `feature/lz4-package` | not started | |
| P5e AES-GCM-SIV | `feature/aes-gcm-siv-package` | not started | |
| P5f Executable examples | `test/docs-examples` | not started | |
| P6 Benchmarks | `benchmark/graceful-stop`, `benchmark/track-b-adapters`, `benchmark/generator-profiles` | not started | |
| P7 Documentation | `docs/rc2-docs` | not started | |
| P8 Audit of rc.2 | `audit/rc2-conformance`, `audit/rc2-closure` | not started | |
| Tag | the owner tags `v1.0.0-rc.2` | not started | |
| Release | `release/v1.0.0` → `main` | not started | |

Status values: `not started` · `in progress` · `gate holds — awaiting commit` · `closed` · `blocked — <question>`.
