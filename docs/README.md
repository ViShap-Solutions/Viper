# Viper documentation

| Document | What it is | Status |
|---|---|---|
| [System-Contract.md](System-Contract.md) | **The normative contract.** Public API surface, configuration, limits and budgets, stream mechanisms, exception taxonomy, format versions, member layouts, polymorphism, references, the byte-level wire format (§22), the supported types and their encodings (§23), and the release checklist (§24). | Current — matches `src/` |
| [Architecture-Audit.md](Architecture-Audit.md) | Why the architecture looks the way it does: the audit that produced it, the alternatives considered and rejected, the invariants, and the implementation status. Written in Russian. | Current |
| [QA-Plan.md](QA-Plan.md) | Test plan. | **Stale** — predates the architecture rework |
| [Benchmark-Plan.md](Benchmark-Plan.md) | Benchmark plan. | **Stale** — predates the architecture rework |
| [audit/](audit) | Historical record of the hostile-input audit that drove the rework: the original probes and the first remediation design with its review. | Historical, superseded |

## Rules

- The contract is the source of truth. Where it and the code disagree, one of them is defective; §22
  and §23 are pinned by tests in `tests/ViShap.Viper.Serialization.Tests/Correctness/` so that the
  disagreement surfaces as a failing build rather than as a surprise for a consumer.
- Behaviour changes update the contract in the same commit.
- `audit/Problems.cs` does not compile and is not part of any project. It is kept because it records
  what was actually observed before the rework, and it maps each finding to the test that now pins it.
