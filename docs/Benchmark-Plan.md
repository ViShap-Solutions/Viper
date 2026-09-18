# ViShap.Viper — Benchmark Plan

**Target release:** v1.0.0  
**Status:** Performance release specification  
**Framework:** BenchmarkDotNet  
**Primary comparison goal:** reproducible, fair measurements across equivalent supported scenarios

---

# 1. Purpose

The benchmark suite measures:

- serialization time;
- deserialization time;
- round-trip time;
- throughput;
- allocations;
- GC activity;
- payload size;
- compression ratio;
- encryption overhead;
- cold-start behavior;
- warm steady-state behavior.

Benchmarks are not correctness tests and do not replace QA.

---

# 2. Approved competitor set

The mandatory comparison set is:

```text
1. ViShap.Viper.BinarySerializer
2. System.Text.Json
3. protobuf-net
4. MessagePack for C#
5. Orleans.Serialization
6. MemoryPack
```

## Explicitly removed

```text
ZeroFormatter
System.Xml.Serialization.XmlSerializer
```

Reason:

- the established benchmark adapter layer did not produce a working, fair executable path for these two serializers;
- keeping a broken adapter would create false data, an artificially incomplete matrix, or a benchmark that measures adapter failure instead of serialization;
- they are therefore removed from the mandatory comparison set rather than being silently approximated.

This is **not** a claim that either library is intrinsically inferior or irrelevant. It is a benchmark-harness compatibility decision.

A future reintroduction requires:

- [ ] working adapter
- [ ] equivalent fixture representation
- [ ] documented feature mapping
- [ ] successful isolated benchmark run
- [ ] review of fairness before inclusion

---

# 3. Benchmark project structure

```text
benchmarks/
└── ViShap.Viper.Serialization.Benchmarks/
    ├── Program.cs
    ├── BenchmarkConfig.cs
    ├── Models/
    ├── DataSets/
    ├── Adapters/
    │   ├── IBenchmarkSerializer.cs
    │   ├── ViperAdapter.cs
    │   ├── SystemTextJsonAdapter.cs
    │   ├── ProtobufNetAdapter.cs
    │   ├── MessagePackAdapter.cs
    │   ├── OrleansAdapter.cs
    │   └── MemoryPackAdapter.cs
    ├── Scenarios/
    ├── Reports/
    ├── Baselines/
    └── Reporting/
        ├── ReportManifest.cs
        ├── ChartGenerator.*
        └── README.md
```

The benchmark adapter abstraction should be minimal:

```csharp
public interface IBenchmarkSerializer
{
    string Name { get; }

    byte[] Serialize<T>(T value);

    T Deserialize<T>(byte[] payload);
}
```

Streaming adapters, if implemented, should be a separate interface so buffered and streaming APIs are never compared accidentally.

---

# 4. Version locking

The benchmark repository must pin:

- .NET SDK/runtime;
- BenchmarkDotNet;
- competitor package versions;
- OS;
- CPU;
- memory;
- GC mode;
- architecture.

No floating dependency versions are acceptable for a published baseline.

- [ ] all benchmark package versions are pinned
- [ ] runtime version recorded
- [ ] hardware recorded
- [ ] BenchmarkDotNet version recorded
- [ ] benchmark git revision recorded

---

# 5. Fairness rules

## Required

- [ ] compare equivalent logical data
- [ ] separate compressed and uncompressed modes
- [ ] separate reflection/source-generated modes where relevant
- [ ] exclude I/O, logging, console output and build time from timed operations
- [ ] warm each serializer for warm benchmarks
- [ ] use a true fresh-process strategy for cold-start benchmarks
- [ ] report payload size separately from timing
- [ ] report allocations separately
- [ ] unsupported scenarios are marked `Unsupported`, never silently altered
- [ ] publish a matrix, not a single winner
- [ ] keep absolute values visible
- [ ] pin all versions

A benchmark that requires changing the logical fixture to fit a serializer must be marked unsupported for that serializer/scenario.

---

# 6. Dataset catalog

## DATA-01 — TinyFlat

Approx. 100–300 bytes for binary serializers.

- 5–10 scalar fields
- short string
- Guid
- timestamp

- [ ] deterministic fixture
- [ ] pre-generated equivalent payloads

## DATA-02 — MediumFlat

Approx. 1–10 KB.

- 20–50 scalar/string fields
- several nested objects

## DATA-03 — CollectionMedium

Approx. 10 KB.

- 100–500 small records
- primitive and string fields

## DATA-04 — CollectionLarge

Approx. 1 MB.

- 10,000–30,000 records

## DATA-05 — DictionaryHeavy

Approx. 1 MB.

- 10,000+ key/value pairs
- deterministic keys
- nested scalar values

## DATA-06 — DeepGraph

Depths:

```text
5
10
25
50
```

and a separate boundary-safe workload below the configured maximum.

## DATA-07 — UnicodeHeavy

- multi-byte UTF-8
- emoji
- combining sequences
- multilingual strings

## DATA-08 — Incompressible

High-entropy data.

## DATA-09 — HighlyCompressible

Highly repetitive data.

## DATA-10 — SharedReferenceDAG

Shared object instances appearing through multiple paths.

Run identity-preservation comparisons only for serializers that can represent the logical identity semantics without silently changing the fixture.

---

# 7. Benchmark modes

## 7.1 Cold start

Cold start means a genuinely fresh process before the measured serializer operation.

Do **not** call the serializer once before timing and then label the result cold.

Static cache warm-up is a major part of this architecture.

Required approach:

```text
fresh process
→ load serializer
→ perform first operation
→ capture first-use metrics
```

A separate benchmark process/harness is preferred over attempting to reset static state in-process.

- [ ] true fresh-process cold benchmark
- [ ] no shared static state from previous case
- [ ] environment manifest captures process/runtime details

## 7.2 Warm steady state

Warm benchmark:

```text
process initialized
→ serializer initialized
→ representative warm-up
→ BenchmarkDotNet measurement
```

The warm benchmark must not include cold cache construction unless explicitly named as a mixed scenario.

---

# 8. Scenario matrix

For each applicable serializer/dataset:

| Scenario | Required metrics |
|---|---|
| cold serialize | first-op time, allocations, GC |
| warm serialize | mean, distribution, allocations, GC |
| cold deserialize | first-op time, allocations, GC |
| warm deserialize | mean, distribution, allocations, GC |
| round trip | total time, allocations, payload size |
| stream serialize | time, allocations, produced bytes |
| stream deserialize | time, allocations |
| large payload | time, throughput MB/s, allocations |
| Unicode | time, allocations, payload size |
| collection-heavy | time, allocations, payload size |
| dictionary-heavy | time, allocations, payload size |

Viper-specific:

| Scenario | Required metrics |
|---|---|
| None compression | time, allocations, size |
| Deflate | compression/decompression time, ratio, size |
| Brotli | compression/decompression time, ratio, size |
| CRC32 | checksum overhead |
| AES-GCM | encrypt/decrypt time, throughput, allocations |
| compression + checksum + AES-GCM | complete pipeline |
| keyed contract | time, allocations, output size |
| PreserveReferences | time, allocations, output size |

---

# 9. Formatter microbenchmarks

Representative microbenchmarks:

- [ ] primitive
- [ ] string
- [ ] array
- [ ] List
- [ ] Dictionary
- [ ] ImmutableArray
- [ ] immutable collection
- [ ] nested POCO
- [ ] polymorphism

Microbenchmarks are diagnostic, not the primary industry comparison.

---

# 10. Metrics

Every published benchmark result should expose, where BenchmarkDotNet supports it:

- mean;
- median/p50 when available from exported measurement/distribution data;
- standard deviation;
- min/max where useful;
- allocated bytes/op;
- Gen0;
- Gen1;
- Gen2;
- operations/sec;
- payload bytes;
- compression ratio;
- throughput;
- cold-start latency;
- warm latency.

Do not replace absolute values with normalized ratios.

Normalized charts may additionally show:

```text
Viper = 1.0x
```

but the absolute metric remains visible.

---

# 11. Allocation and GC measurement

Dedicated allocation scenarios should isolate:

- payload writer;
- checksum;
- compression;
- encryption;
- header;
- final byte[] assembly.

Additional workload:

- [ ] boxing-heavy `List<int>`
- [ ] large array allocation
- [ ] large string allocation
- [ ] compressed payload temporary buffers
- [ ] encrypted payload temporary buffers

Where process-level peak memory is relevant, use a separate process measurement rather than inferring peak working set from per-operation allocation alone.

---

# 12. Payload-size measurements

Record serialized size independently from timing.

For each scenario:

```text
Serializer
Dataset
Mode
PayloadBytes
```

For compression:

```text
raw bytes
compressed bytes
compression ratio
```

For encryption:

```text
compressed bytes
encrypted/on-disk bytes
encryption overhead
```

Do not compare an encrypted Viper result directly to a no-encryption competitor without labeling the configuration.

---

# 13. Benchmark fairness for model adaptation

Adapters may require serializer-specific annotations/configuration.

Allowed:

- adding the minimum metadata required for the same logical model;
- a serializer-specific adapter;
- a source-generated model when that is the serializer's normal supported mode.

Not allowed:

- removing fields to make a serializer succeed;
- changing collection semantics silently;
- duplicating shared references when the scenario is measuring identity;
- using a completely different logical dataset.

Such scenarios become:

```text
Unsupported
```

rather than misleadingly benchmarked.

---

# 14. Required output artifacts

A benchmark run must produce raw and presentation artifacts:

```text
BenchmarkReports/
├── results.csv
├── results.json
├── results.md
├── summary.html
├── environment.json
├── payload-sizes.csv
├── memory.csv
├── charts/
│   ├── serialize-time.png
│   ├── deserialize-time.png
│   ├── roundtrip-time.png
│   ├── throughput.png
│   ├── allocated-bytes.png
│   ├── gc-gen0.png
│   ├── gc-gen1.png
│   ├── gc-gen2.png
│   ├── payload-size.png
│   ├── compression-ratio.png
│   ├── compression-throughput.png
│   ├── encryption-throughput.png
│   ├── cold-start.png
│   └── warm-start.png
└── manifest.md
```

---

# 15. Who produces the charts?

BenchmarkDotNet is responsible for benchmark measurements and standard exported reports.

The project must also contain a **separate reporting/charting step** that consumes the machine-readable BenchmarkDotNet output and produces the release charts.

Therefore:

```text
BenchmarkDotNet run
        ↓
CSV/JSON/raw reports
        ↓
ChartGenerator / reporting step
        ↓
PNG/HTML charts
        ↓
release report
```

The charts should not be drawn manually after every benchmark run.

- [ ] chart generation is automated
- [ ] charts are reproducible from raw result files
- [ ] chart generator version is recorded
- [ ] chart axis units are explicit
- [ ] lower-is-better/higher-is-better is labeled
- [ ] library and dataset names are visible
- [ ] absolute charts are present
- [ ] normalized charts, if used, are secondary

---

# 16. Chart families

Required:

- [ ] time per operation
- [ ] operations/sec
- [ ] allocated bytes/op
- [ ] Gen0/Gen1/Gen2
- [ ] payload bytes
- [ ] compression ratio
- [ ] compression throughput
- [ ] encryption throughput
- [ ] cold vs warm startup
- [ ] scaling curve small/medium/large

The charting layer must not manufacture values that do not exist in raw benchmark output.

---

# 17. Baselines and regression policy

A baseline belongs to a specific:

```text
source revision
+
runtime
+
hardware
+
package lock
+
BenchmarkDotNet version
```

Later releases are compared only against compatible baseline environments.

Review thresholds:

- [ ] >10% stable throughput regression triggers review
- [ ] >10% allocation increase triggers review
- [ ] payload-size regression beyond documented tolerance triggers review
- [ ] statistically significant cold-start regression triggers review
- [ ] unexpected Gen2 increase triggers review

These are performance-review triggers, not automatic correctness failures.

---

# 18. Benchmark classes

## `SerializeBenchmarks`

Parameters:

- dataset
- serializer
- mode
- payload size

## `DeserializeBenchmarks`

Use pre-generated serializer-specific bytes created outside the timed method.

## `RoundTripBenchmarks`

Serialize + deserialize in one timed operation.

## `StreamingBenchmarks`

Only compare meaningful equivalent stream APIs.

## `CompressionBenchmarks`

Viper only:

- None
- Deflate
- Brotli

## `EncryptionBenchmarks`

Viper only:

- AES-GCM encrypt
- AES-GCM decrypt

## `FormatterBenchmarks`

Representative formatter families.

## `ColdStartBenchmarks`

Fresh process semantics.

## `AllocationBenchmarks`

Stage-oriented allocation measurements.

---

# 19. Unsupported-scenario policy

A result is one of:

```text
Supported
Unsupported
Failed
NotApplicable
```

`Unsupported` is not a zero, not an omitted row, and not a guessed approximation.

Examples:

```text
ZeroFormatter → removed from mandatory matrix
XmlSerializer → removed from mandatory matrix
```

Any newly discovered incompatibility must be visible in the report.

---

# 20. Benchmark environment manifest

Record:

```text
OS
CPU model
logical/physical cores
RAM
architecture
.NET SDK
.NET runtime
GC mode
BenchmarkDotNet version
Viper source revision
competitor package versions
compiler/build configuration
date/time/timezone
```

- [ ] manifest generated automatically
- [ ] manifest stored with baseline
- [ ] benchmark revision recorded

---

# 21. Release checklist

## Harness

- [ ] BenchmarkDotNet project builds in Release.
- [ ] All five approved competitors have working adapters.
- [ ] ZeroFormatter is absent from the mandatory comparison set.
- [ ] XmlSerializer is absent from the mandatory comparison set.
- [ ] Unsupported scenarios are explicit.
- [ ] Adapter code is excluded from timed operation where appropriate.

## Datasets

- [ ] all datasets deterministic
- [ ] logical model is equivalent
- [ ] fixture sizes recorded
- [ ] reference-preservation scenario is only compared where semantics are representable

## Measurements

- [ ] cold serialize
- [ ] warm serialize
- [ ] cold deserialize
- [ ] warm deserialize
- [ ] round trip
- [ ] stream scenarios
- [ ] allocation metrics
- [ ] GC metrics
- [ ] payload sizes
- [ ] Viper compression metrics
- [ ] Viper encryption metrics

## Reporting

- [ ] raw CSV
- [ ] raw JSON
- [ ] Markdown summary
- [ ] HTML summary
- [ ] environment manifest
- [ ] payload-size report
- [ ] charts
- [ ] baseline package
- [ ] reproducibility instructions

## Release

- [ ] benchmark executed from Release build
- [ ] source revision frozen
- [ ] package/runtime versions frozen
- [ ] hardware/environment captured
- [ ] no unsupported scenario is silently presented as a valid measurement
- [ ] performance review completed for all threshold crossings

---

# 22. Definition of benchmark completion

Benchmarking is complete only when the raw results, environment manifest, baselines, and generated charts exist together and are traceable to one source revision and one locked dependency/runtime environment.

The benchmark report must present facts and measurements. It must not reduce the result to a single unexplained "winner".
