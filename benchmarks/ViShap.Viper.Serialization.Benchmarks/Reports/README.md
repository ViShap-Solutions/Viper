# Benchmark reports

Run the BenchmarkDotNet project in **Release** configuration on a controlled machine.

Required artifacts:

- `results.csv`
- `results.json`
- `results.md`
- `summary.html`
- `memory.csv`
- `payload-sizes.csv`
- `charts/*.png`
- `environment.md`

The benchmark executable records the environment manifest before running. The reporting step must normalize each metric to Viper = `1.0x` while retaining the absolute values.

Do not commit fabricated measurements. `Baselines/` is populated only by an actual release benchmark run.
