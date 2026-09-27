# Retired — what the rework removed or renamed

**Purpose.** A name, rule or term that the rework removes survives silently wherever nobody looks for
it. This ledger is where each stage records what it removed, so that R9 can search the whole
repository for every entry and prove that nothing of the previous system is left
(`Rework-Plan.md` §0.12, R9) [D9.28].

**Rule.** A stage appends a row for everything it removes or renames — a type, a member, an option, a
command-line argument, a term, a documented rule, a checkpoint — in the same change as the removal.
A row names what replaced it, or says that nothing did.

**The R9 sweep.** Every `Searched as` value is searched in `src/`, `tests/`, `benchmarks/`, the XML
documentation, `CLAUDE.md`, `.claude/skills/` and every living document of `internal/`. The only hits
allowed are in historical documents, in explicit `retired in Rn` markers, and in the frozen
artifacts under `benchmarks/.../Baselines/`, which BASE-08 forbids rewriting. Each row's last column
records the result.

| Stage | Removed or renamed | Kind | Replaced by | Searched as | R9 sweep |
|---|---|---|---|---|---|
| R0 | `RunKind.Partial` | harness type member | `RunKind.Measurement` | `RunKind.Partial`, `Partial run`, `partial run`, `Partial-прогон` | |
| R0 | "Pre-release (`beta`, `rc`) — Partial по умолчанию" | workflow rule | a full `--track A` on a pre-release tag is a Baseline, taken only by the owner's decision (`Development-Workflow.md` §6.2) | `Partial по умолчанию` | |
| R0 | `RunKind.Baseline` described as the record of a released version | harness documentation | the record of a tagged revision: a release, a pre-release on request, a local tag before a rework | `record of a released version`, `one per release` | |
| R0 | DATA-09 at 20 000 strings, target ~1 MB | benchmark dataset | 5 000 strings, target ~0.35 MB (Benchmark-Plan R-02) | `20_000` in `ShapeDatasets.cs`, `HighlyCompressible` with `~1 MB` | |
