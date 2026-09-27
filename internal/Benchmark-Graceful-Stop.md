# Benchmark harness — graceful stop (backlog)

**Status:** decided by the owner on 2026-09-27, variant C; not started. Outside the rework and outside
every current plan: it gates nothing and is worked on its own branch.
**Branch:** `benchmark/graceful-stop`, cut from `release/v1.0.0` after the R3 branch is merged, and
returned to it through a pull request (`Development-Workflow.md` §2.5).

## The problem

`TrackRunner` keeps every suite's summary in memory and writes `results.csv`, `memory.csv`,
`components.csv`, `run.json` and the report only when the whole run ends. A run that is interrupted —
Ctrl+C, a killed process, a reboot — leaves none of them. What survives is BenchmarkDotNet's own
export of every suite that finished (`results/*-report-full.json`, `*-report.csv`), which nothing in
the harness reads back. A suite interrupted halfway is lost whole: BenchmarkDotNet exports a suite
only when it ends.

## Variant C — a stop on request, and recovery from the exports

**A. Stop between suites.**

- The first Ctrl+C, or a `STOP` file in the run's directory, means "finish the current suite, then
  stop"; a second Ctrl+C stops at once.
- On a stop the harness still writes `results.csv`, `memory.csv`, `components.csv`, the report and
  `run.json` from the suites that finished. `run.json` records `interrupted: true` and names the
  suites that did not run or did not finish.
- An interrupted run is always a Measurement, never a Baseline, whatever the tag and the tree.

**B. Recovery from the exports.**

- `--report <directory>` rebuilds `results.csv` and the rest from `results/*-report-full.json` when
  `results.csv` is absent, and marks the run interrupted. It works after any death of the process.
- The parameters are taken from each benchmark's `FullName`: the `Parameters` field of the export
  shortens long values (`DATA-(...)Large [24]`), so it cannot key a cell.

**Not covered, by design:** the cells of a suite interrupted halfway. Keeping them would mean running
BenchmarkDotNet one cell at a time, which costs a build and a process start per cell.

## Done when

- A run stopped with one Ctrl+C or a `STOP` file after its second suite produces the results, the
  report and `run.json` of the first two suites, marked interrupted, and names the rest as not run.
- A run killed during its third suite is turned into the same by `--report <directory>`.
- Neither can ever be recorded as a Baseline.
- The harness's own checks cover both paths; `--verify` and `--smoke` pass.
