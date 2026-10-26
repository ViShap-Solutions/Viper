# ViShap.Viper — Conformance audit of `v1.0.0-rc.2`: brief

**Class: plan.** The brief the skill `viper_conformance_auditor` works from in stage P8 of `RC2-Plan.md`:
what the audit of `rc.2` rests on, what it asks, what it delivers and how it closes. It defines no
behavior; the contract does.

**Status:** written 2026-10-16 in P7; **approved by the owner on 2026-10-26** as written, recorded in
`RC2-Plan.md` §3.9.
**Written against:** `release/v1.0.0` at `3c27571` (P6 merged) with P7 applied, 2 265 + 46 tests green in
Debug and Release.

---

# 1. What is audited

`v1.0.0-rc.1` was audited whole in R9 (`Audit-Conformance.md`, verdict "the rc may be tagged", tagged on
`c728c99`). `rc.2` is that system plus everything `RC2-Plan.md` built: the naming of the contract's read
side (P1), the public type-contract seam and the context (P2), the source generator and its tests (P3,
P4), the contract found once per configuration (P4b), native AOT through the context (P4c, P4d), the
executable documentation (P5f), the Track B adapters and the graceful stop of the harness (P6), and the
documentation (P7).

The audit therefore has two parts, and both are full:

- **the delta** — every change between `c728c99` and the audited commit (`git diff c728c99..HEAD`) is
  read and held to the decision that asked for it, and every document it made untrue was changed with it;
- **the whole system** — the invariants, the three barriers, the wire and the consistency of contract,
  plans, documentation and code are proven again over the current tree, not inherited from the R9
  report: a green R9 verdict proves nothing about `rc.2`.

What was deferred past `v1.0.0` is audited only for absence: nothing of it is half-built (§4.1).

# 2. Sessions and branches

| Step | Branch | Who | What |
|---|---|---|---|
| Audit | `audit/rc2-conformance` | `viper_conformance_auditor`, a session of its own | the questions of §4; writes the report (§6) |
| Fixes | `bugfix/rc2-audit-<topic>`, one per group of findings | `viper_builder`; `viper_generator` for `src/ViShap.Viper.Generator` | closes the findings at the layer each names |
| Closure | `audit/rc2-closure` | `viper_conformance_auditor`, a new session | §7: every finding verified closed, the CD dry run, the final verdict |
| Tag | — | the owner | `v1.0.0-rc.2` on `release/v1.0.0`, only after the closure's verdict "rc.2 may be tagged" |

Each branch is cut from `release/v1.0.0` after the previous one is merged (`Development-Workflow.md`
§2.5). A session that built any part of `rc.2` does not audit it; the auditor fixes nothing it finds.

# 3. Evidence, in order of authority

```text
internal/rc2/RC2-Plan.md §3.9          the owner's decisions for rc.2 — the highest authority for what rc.2 was to build
internal/rc2/Native-AOT-Design.md       D1…D12 as approved (§9) and as built (§10)
internal/rework/Decisions.md            the owner's decisions for the rework (Russian), with the amendments of
internal/rework/Owner-Review.md         its decision log; binding until v1.0.0, except where §3.9 decided later
internal/System-Contract.md             the contract; §25 holds INV-1…INV-20
internal/QA-Plan.md                     the test plan; §15.2, §15.3, §33 and §34 are new since rc.1
internal/Benchmark-Plan.md              the measurement plan; §7 and §29.2 hold Track B
internal/rc2/RC2-Plan.md §4, §7, §8     the stages and gates, the ledger of renamed names, the Progress table
internal/rework/Retired.md              what the rework removed or renamed, with the strings to search for
CLAUDE.md, .claude/skills/, internal/README.md
docs/, the package READMEs, the XML documentation
src/, tests/, benchmarks/, .github/workflows/
```

Where two disagree, the higher one is right and the lower one is the finding. A decision of §3.9 that
contradicts an earlier decision of `Decisions.md` amends it; cite the row. A disagreement about a decision
itself — the code does what nobody decided, or a decision cannot be what the code does — is a finding of
weight "blocks the rc" with side "owner's decision needed", the alternatives visible in the evidence, and
no recommendation.

# 4. Questions

Each is answered completely; a question with an unchecked item is not answered.

## 4.1 Decisions

- Every row of `RC2-Plan.md` §3.9 — N1a/N1b, S1–S5 with their refinements, the invalid description, F1,
  A1, T1, B1, the engine fast path, S4 revisited, B2, P4b item 1, P4c and P4d — and every decision D1…D12
  of `Native-AOT-Design.md`: implemented as decided, with its evidence (file and line, test name) or as a
  finding.
- The deferred items are absent, not half-built: no fingerprint record, no live trace sink on the write
  side, no `Zstandard` or `Lz4` member of `CompressionAlgorithm` and no class for either, no AES-GCM-SIV,
  no lazy offers of PERF-10/PERF-12, no member-call change of PERF-11. Each has its `ideas/` or
  `performance/` file, and nothing in `src/`, the contract or `docs/` promises it.

## 4.2 Invariants

For every INV-1…INV-20 of contract §25: it **holds by construction** (the type, visibility, signature or
factory that makes the violation inexpressible, not a call site that respects it); it is **stated in the
section it governs**; and it is **pinned by a structural test that inspects the current types** and would
fail if it broke. Read with particular care where `rc.2` touched them:

| Invariant | What `rc.2` added that it must survive |
|---|---|
| INV-2, INV-5 | the public seam: `TypeContract<T>`, `MemberWriter`, `MemberReader` — no byte, count, position or limit reachable; a copy or a default instance of a writer or reader refused |
| INV-7, INV-20 | `SupportedTypes` and `Add(SupportedType)`: an offer is the codec the rules build, the first offer kept, no registry through which behaviour changes |
| INV-12 | the generator's member plan against `ReflectedContract<T>`, rule for rule (§14.4) |
| INV-17 | the generated and hand-written contracts box nothing outside a polymorphic slot |
| INV-19 | with `ReflectionPath.IsEnabled` off, no codec, contract or union map is built by reflection |

## 4.3 The generator

- It emits contracts, supported-type registrations and type information, and nothing of the traversal: no
  loop, length, count, limit, frame, tag or byte (GEN-09) — read the emitter, not only the test.
- Its tables equal `BuiltInCodecs` and `TypeRules` (`PipelineTests`), and every rule of
  `ReflectedContract<T>` has its counterpart in `Planner`; `ByteIdentityTests` and the second conformance
  run agree.
- Every diagnostic VPR001…VPR021 is in contract §14.4, in `docs/generator.md` with its fix, in the
  generator README's table, and has a test that earns it.
- The package: `netstandard2.0`, packed to `analyzers/dotnet/cs`, a development dependency; no shipped
  assembly references it; the meta-package carries it as an analyzer.

## 4.4 Consistency, in both directions

- **contract ↔ `src/`**, section by section, and every behaviour of the new surface a consumer can observe —
  an exception, a refused description, a refused offer, the switch's default — is in the contract.
- **rule ↔ checkpoint ↔ test** for every checkpoint added or changed since `rc.1`: it names a rule that
  exists and a test that asserts what the checkpoint claims. Retired checkpoints are marked, not deleted.
- **benchmark checkpoint ↔ entry point and suite** for Track B and the graceful stop; no `src/` file was
  edited by P6 (`git log c728c99..HEAD -- src` against the P6 commits).
- **public surface ↔ contract §3 ↔ XML documentation ↔ `docs/`** — `PublicSurfaceTests` and
  `MemberSurfaceTests` against §3 and §3.6, and each new public type explained on a page of `docs/`.
  XML documentation cites no `internal/` file and records no history.
- **`docs/` and the READMEs** — every `csharp` block compiles and runs (DOC-01…04); what a block prints
  in a comment is what it prints; no page cites `internal/`; no adjective about performance appears in
  `docs/`, a README, a package description or the XML documentation (`Benchmark-Plan.md` §28).

## 4.5 Nothing left behind

- Every row of `RC2-Plan.md` §7 and of `rework/Retired.md` — every `Searched as` value — searched in
  `src/`, `tests/`, `benchmarks/`, the XML documentation, `docs/`, the READMEs, `CLAUDE.md`,
  `.claude/skills/` and every living document of `internal/`. Allowed hits: historical documents, explicit
  retirement markers, the frozen baselines under `benchmarks/`. A row recorded clean that is not is a finding.
- Beyond the ledgers: names and rules of `rc.1` that `rc.2` replaced and no row names — the internal
  contract base type, the read-side names before P1, the registry's tables before P4d, the façade's name
  before the owner named it. Each one found is a finding and a missing ledger row.

## 4.6 Wire

- No frame byte changed since `rc.1`: `git log c728c99..HEAD -- tests/ViShap.Viper.Serialization.Tests/Fixtures/Wire`
  is empty and `Format/CompatibilityTests` reads every fixture.
- Generated and hand-written contracts write the reflected bytes (`ByteIdentityTests`, CONF-01…07 through
  both seams, CTX-01…08); the AOT consumer reads and writes the frozen payloads back with the switch off.
- `Fixtures/Wire.cs` still encodes and decodes with its own code.

## 4.7 Trimming and native AOT

- Nothing in `src/` suppresses a trimming or AOT warning (`UnconditionalSuppressMessage`,
  `SuppressMessage` with an `IL` id, `#pragma warning disable IL`); exactly the 26 generic entry points
  without type information carry `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`, and below them only
  the reflective builders.
- `Api/AotAnalysisTests` passes; the CI job `native-aot` is green on the audited commit (cite the run);
  where the Visual Studio C++ tools are present, `dotnet publish tests/ViShap.Viper.AotConsumer -c Release
  -r win-x64` reports no warning and its output equals the JIT run's.

## 4.8 Document classes

Every file under `internal/` — the new ones of `rc2/`, `ideas/` and `performance/` included — carries its
class at its head, and the same class in `internal/README.md` and `CLAUDE.md`; a living document that
cites a historical one as the source of a rule is a finding.

## 4.9 Release readiness

Run, and put each command with its relevant output in the report:

```powershell
dotnet build Viper.sln --configuration Release
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj --configuration Debug
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj --configuration Release
dotnet test tests/ViShap.Viper.Generator.Tests/ViShap.Viper.Generator.Tests.csproj --configuration Debug
dotnet test tests/ViShap.Viper.Generator.Tests/ViShap.Viper.Generator.Tests.csproj --configuration Release
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks --configuration Release -- --smoke
```

The build reports no warning in a shipped project; both suites are green with their counts; the harness
builds, `--verify` passes every pair and `--smoke` passes.

# 5. Principles checked in every audit

Each against the code, not against a document that says so: dependencies point strictly downwards and no
type below `Pipeline/` references `SerializationLimits`; limits and budgets have one owner, the operation,
and no value read from the wire widens one; the three barriers hold — byte monopoly, validated counts,
engine-owned traversal — with the public seam and the generated code inside them.

# 6. Deliverable

One file, in Russian: `internal/rc2/Audit-Conformance.md`, class *historical* once written, the only file
the audit branch adds or changes. Its sections follow the skill's report; its findings are numbered
`RC2-01`, `RC2-02`, … — never reused, and distinct from the `CONF-nn` of the R9 report and of the QA
conformance checkpoints. Each finding carries its weight (**blocks the rc**, **must be fixed before the
release**, **recorded**), its side, its layer, its evidence, what is observed and what the higher authority
expects.

The verdict is exactly one of: **rc.2 may be tagged**; **rc.2 may be tagged once the listed findings are
closed**; **rc.2 may not be tagged**. A green suite alone never justifies the first; benchmark
incompleteness never justifies the third.

# 7. Closure

On `audit/rc2-closure`, with every `bugfix/rc2-audit-*` branch merged:

1. Every finding verified closed from the code and the documents, at the layer it named, with the
   documents it made untrue changed in the same change and a ledger row (`RC2-Plan.md` §7) for anything
   removed or renamed. A finding the owner decided not to fix is closed only with that decision recorded
   beside it. A fix that opened a new divergence is a new finding, and the sequence returns to the fixes.
2. §4.5 re-run over the files the fixes touched, and §4.9 in full.
3. The CD dry run, as `.github/workflows/cd.yml` runs it, without pushing:

   ```powershell
   dotnet restore Viper.sln
   dotnet build Viper.sln --configuration Release --no-restore
   dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj --configuration Release --no-build --no-restore
   dotnet test tests/ViShap.Viper.Generator.Tests/ViShap.Viper.Generator.Tests.csproj --configuration Release --no-build --no-restore
   dotnet pack src/ViShap.Viper.Core/ViShap.Viper.Core.csproj --configuration Release --no-build --no-restore -p:Version=1.0.0-rc.2 --output <scratchpad>/artifacts
   dotnet pack src/ViShap.Viper.Serialization/ViShap.Viper.Serialization.csproj --configuration Release --no-build --no-restore -p:Version=1.0.0-rc.2 --output <scratchpad>/artifacts
   dotnet pack src/ViShap.Viper.Generator/ViShap.Viper.Generator.csproj --configuration Release --no-build --no-restore -p:Version=1.0.0-rc.2 --output <scratchpad>/artifacts
   dotnet pack src/ViShap.Viper/ViShap.Viper.csproj --configuration Release --no-build --no-restore -p:Version=1.0.0-rc.2 --output <scratchpad>/artifacts
   ```

   Then what the workflow checks — exactly four `.nupkg` (`ViShap.Viper.Core`, `ViShap.Viper.Serialization`,
   `ViShap.Viper.Generator`, `ViShap.Viper`), the two `.snupkg` of Core and Serialization,
   `<version>1.0.0-rc.2</version>` in each `.nuspec` — and in addition: each package carries its own
   non-empty README; the generator package holds its assembly under `analyzers/dotnet/cs` and nothing under
   `lib/`; the Serialization package carries `buildTransitive/ViShap.Viper.Serialization.targets`. The
   artifacts go to the scratchpad, never into the repository.
4. The report's closure section written and its verdict replaced; the findings keep their text.

The owner then tags `v1.0.0-rc.2` on `release/v1.0.0`; `v1.0.0` follows a few days later, not the same day
(§3.9).
