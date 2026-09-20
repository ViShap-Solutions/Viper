# Viper documentation

| Document | What it is | Status |
|---|---|---|
| [System-Contract.md](System-Contract.md) | **The normative contract.** Public API surface, configuration, limits and budgets, stream mechanisms, exception taxonomy, format versions, member layouts, polymorphism, references, the byte-level wire format (§22), the supported types and their encodings (§23), and the release checklist (§24). | Current — matches `src/` |
| [Architecture-Audit.md](Architecture-Audit.md) | Why the architecture looks the way it does: the audit that produced it, the alternatives considered and rejected, the invariants, and the implementation status. Written in Russian. | Current |
| [QA-Plan.md](QA-Plan.md) | **The release-gate test plan.** Checkpoint list only, staged M0–M8, every item citing the contract section it proves; §30 records the confirmed defects and the resolved contract questions. The method for working it lives in the `viper_tester` skill. | Current — M0 through M8 closed |
| [Benchmark-Plan.md](Benchmark-Plan.md) | **The post-release performance plan**, measured against the `v1.0.0` tag and re-run per v1.x. It gates no release — only what may be claimed about performance. Checkpoint list only, staged B0–B9: the competitor roster and the reason behind every inclusion and exclusion, the capability tiers that keep a comparison like-for-like, the data corpus, the workloads, the fairness rules, the reporting artifacts and the baseline policy. The method lives in the `viper_bencher` skill. | Current — realigned; B0 open, nothing measured yet |
| [performance/](performance) | Proposals arising from benchmarking: one file per proposed optimization or extension point, each cited to the measurements behind it and left for a decision. Nothing in it has been applied. | Current — empty |
| [audit/](audit) | Historical record of the hostile-input audit that drove the rework: the original probes and the first remediation design with its review. | Historical, superseded |

## Rules

- The contract is the source of truth. Where it and the code disagree, one of them is defective; §22
  and §23 are pinned by tests in `tests/ViShap.Viper.Serialization.Tests/Format/` so that the
  disagreement surfaces as a failing build rather than as a surprise for a consumer.
- A plan holds items and gates; the method for working it lives in its skill.
- Benchmarking measures the library and never changes it. What a measurement suggests goes to
  `performance/` as a proposal, and `src/` and the contract stay as they were.
- Behaviour changes update the contract in the same commit.
- `audit/Problems.cs` does not compile and is not part of any project. It is kept because it records
  what was actually observed before the rework, and it maps each finding to the test that now pins it.
