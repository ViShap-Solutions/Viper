# Benchmark baselines

This directory intentionally contains no fabricated benchmark numbers.

Populate it with actual BenchmarkDotNet output from the same source revision and record the hardware/runtime manifest. Review thresholds from the QA plan are:

- >10% regression in stable throughput;
- >10% allocation increase;
- documented payload-size tolerance exceeded;
- statistically significant cold-start regression;
- unexpected Gen2 increase.
