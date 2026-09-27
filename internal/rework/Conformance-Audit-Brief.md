# The conformance audit of R9 — brief for the skill `viper_conformance_auditor`

**What this is.** R9 begins by writing the skill `viper_conformance_auditor`, which the owner approves
before it runs [D9.29]. This brief is what the skill is written from, so that R9 does not have to guess
what the audit rests on, what it asks and what it delivers.

**What the audit is not.** It does not design an architecture — that is `viper_auditor` — and it does
not look for the next direction — that is `viper_auditor_next`, after the release. It proves that what
was built is exactly what was decided, and that nothing of the previous system is left.

---

# 1. Evidence, in order of authority

```text
internal/rework/Decisions.md          the owner's decisions — the highest authority for what was to be built
internal/rework/Rework-Plan.md        invariants INV-1…INV-18 (§3), target design (§5–§11), stages and gates (§12)
internal/rework/*-Changes.md          what each governing document was to receive: contract, QA plan,
                                      benchmark plan, CLAUDE.md
internal/rework/Retired.md            everything the rework removed or renamed, with the strings to search for
internal/System-Contract.md           the reconciled contract
internal/QA-Plan.md                   the reconciled test plan
internal/Benchmark-Plan.md            the reconciled measurement plan
CLAUDE.md, internal/Development-Workflow.md, .claude/skills/
docs/, the package READMEs, the XML documentation
src/, tests/, benchmarks/             the system itself
internal/Architecture-Audit.md        historical, but its principles are checked (§3)
```

Where two of them disagree, the higher one is right and the lower one is a finding. A disagreement
about a decision is reported to the owner, never resolved by the auditor.

# 2. Questions

1. **Decisions.** Every decision D-n of `Decisions.md` is implemented as decided. Each is listed with
   its evidence (file and line, test name) or as a finding.
2. **Invariants.** Every invariant INV-1…INV-18 holds by construction, is stated in the contract, and is
   pinned by a structural test that inspects the current types.
3. **Consistency in both directions.**
   - contract ↔ `src/`: every section describes the code, and every behaviour of the code a consumer can
     observe is in the contract;
   - contract rule ↔ QA checkpoint ↔ test: every rule has a checkpoint, every checkpoint names a rule
     that exists and a test that passes;
   - benchmark checkpoint ↔ entry point and suite: every checkpoint names something that exists;
   - public surface ↔ contract §3 ↔ XML documentation ↔ `docs/`.
4. **Nothing left behind.** Every row of `Retired.md` is searched in `src/`, `tests/`, `benchmarks/`, the
   XML documentation, `docs/`, the READMEs, `CLAUDE.md`, the skills and every living document. The only
   hits allowed are in historical documents and explicit `retired in Rn` markers. Beyond the ledger, the
   auditor looks for rules and terms of the previous system that no row names, and each one found is a
   finding and a missing row.
5. **Wire.** Every byte rule of `Rework-Plan.md` §6 is pinned by a byte-level test with the bytes of §6;
   the fixtures were re-frozen exactly once, in R6, and the "never regenerated" rule is restored in
   `CLAUDE.md`; the oracle and its test are gone.
6. **Document classes.** Every file under `internal/` carries its class — normative, plan, operational,
   historical — at its head and in `CLAUDE.md`, and a historical file says its rules no longer apply.
7. **Release readiness.** `dotnet pack` succeeds for all three packages, each with its own non-empty
   README (`CORE-README.md` for Core); the CD dry run passes; the suite is green in Debug and Release;
   the benchmark harness builds, `--verify` passes every pair and `--smoke` passes.

# 3. Principles checked in every audit

From `Architecture-Audit.md`, which stays true after it becomes historical:

- dependencies point strictly downwards; no type below `Pipeline/` references `SerializationLimits`;
- limits and budgets have one owner — the operation — and a payload can never raise them;
- the three barriers hold: byte monopoly, validated counts, engine-owned traversal.

Each is checked against the code, not against a document that says so.

# 4. Deliverable

- A report in Russian under `internal/`, written on an `audit/<topic>` branch cut from `release/v1.0.0`
  (`Development-Workflow.md`). The auditor never modifies `src/`, `tests/`, `benchmarks/` or a document
  it audits.
- Every finding carries: the evidence (file and line, or a command and its output); which side is
  wrong — the code or a document — judged by the order of authority of §1; the layer where the fix
  belongs; and its weight: **blocks the rc**, **must be fixed before the release**, or **recorded**.
- A verdict: **the rc may be tagged**, **the rc may be tagged once the listed findings are closed**, or
  **the rc may not be tagged**, with the reasons.
- After the findings are fixed, the auditor verifies each one closed and records it in the report.

# 5. Independence

The audit is run by a separate agent in a separate session. The executor of the rework never audits
its own work, and the auditor never fixes what it finds.
