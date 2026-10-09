# Viper documentation

**Class: operational.** The index of `internal/` and the class of each document.

Every file under `internal/` belongs to one class, and says which at its head.

| Class | What it means | Where it is |
|---|---|---|
| **Normative** | The source of truth. Code and every other document answer to it. | `System-Contract.md` |
| **Plan** | A checklist of items and gates for work to be done or repeated. It never defines behavior. | `QA-Plan.md`, `Benchmark-Plan.md`, `Benchmark-Graceful-Stop.md`, `rc2/`, `performance/` |
| **Operational** | Working instructions that describe the system and the way it is developed as they are now. | `Development-Workflow.md`, this file, `../CLAUDE.md`, `../.claude/skills/`, the package READMEs, `../docs/` |
| **Historical** | A record of work already done, kept for provenance. Its rules no longer apply, and it is never a reason to change the system. | `Architecture-Audit.md`, `Audit-Closure.md`, `Audit-Future.md`, `Audit-Refactor.md`, `audit/`, `rework/` |

| Document | What it is | Class |
|---|---|---|
| [System-Contract.md](System-Contract.md) | **The normative contract.** Public API surface, configuration, limits and budgets, metering and windowing, exception taxonomy, format versions, member layouts, polymorphism, references, the byte-level wire format (§22), the supported types and their encodings (§23), the release checklist (§24) and the invariants (§25). Matches `src/`. | Normative |
| [QA-Plan.md](QA-Plan.md) | **The release-gate test plan.** Checkpoint list only, staged M0–M8, every item citing the contract section it proves; §30 records the confirmed defects and the resolved contract questions. The method for working it lives in the `viper_tester` skill. | Plan |
| [Benchmark-Plan.md](Benchmark-Plan.md) | **The post-release performance plan**, measured against the `v1.0.0` tag and re-run per v1.x. It gates no release — only what may be claimed about performance. Checkpoint list only, staged B0–B9. The method lives in the `viper_bencher` skill. | Plan |
| [Benchmark-Graceful-Stop.md](Benchmark-Graceful-Stop.md) | A decided backlog item for the benchmark harness; not started. | Plan |
| [rc2/RC2-Plan.md](rc2/RC2-Plan.md) | **The plan for `v1.0.0-rc.2`**: everything earlier documents deferred to after the release — the generator and the public contract seam, the naming of the contract's read side, schema fingerprint, further algorithm packages, live tracing, executable examples, Track B — with the open decisions for the owner and the stages that follow them. | Plan |
| [performance/](performance) | Proposals arising from benchmarking: one file per proposed optimization or extension point, each cited to the measurements behind it and left for the owner to decide. | Plan |
| [Development-Workflow.md](Development-Workflow.md) | How work moves through the repository: branches, tags, the alpha/beta/rc/stable cycle, SemVer rules, fixture freezing, benchmark baselines, hotfixes. Written in Russian. | Operational |
| [Architecture-Audit.md](Architecture-Audit.md) | The audit that produced the architecture: the alternatives rejected and the reasons. Written in Russian. | Historical |
| [Audit-Refactor.md](Audit-Refactor.md), [Audit-Closure.md](Audit-Closure.md), [Audit-Future.md](Audit-Future.md) | The independent audit after the first refactor, the closure of its findings, and the directions it named. Written in Russian. | Historical |
| [audit/](audit) | The hostile-input audit that drove the rework: the original probes and the first remediation design with its review. | Historical |
| [rework/](rework) | The pre-release rework: the plan, the owner's decision record, the change files for each governing document, the ledger of what was retired and the brief of the conformance audit. Its rules stop applying when `v1.0.0` is released; until then it is the evidence that the last reconciliation checks the system against. | Historical |

## Rules

- The contract is the source of truth. Where it and the code disagree, one of them is defective; §22
  and §23 are pinned by tests in `tests/ViShap.Viper.Serialization.Tests/Format/` so that the
  disagreement surfaces as a failing build rather than as a surprise for a consumer.
- A plan holds items and gates; the method for working it lives in its skill.
- Benchmarking measures the library and never changes it. What a measurement suggests goes to
  `performance/` as a proposal, and `src/` and the contract stay as they were.
- Behaviour changes update the contract in the same commit.
- A historical document is read for why, never for what to do. Where it and the contract disagree, the
  contract is right and the historical document is left as written.
- `audit/Problems.cs` does not compile and is not part of any project. It is kept because it records
  what was actually observed before the rework, and it maps each finding to the QA checkpoint that now
  pins it.
