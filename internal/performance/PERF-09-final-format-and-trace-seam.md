# PERF-09 — The final format (R6): sizes, the header of service records, and the cost of the trace seam

**Status:** Open — the seam's cost accepted by the owner on 2026-09-29 (`Owner-Review.md` log 63): the seam stays as built for v1.0; narrowing it (a generic struct observer, or the test hoisted out of element loops) is an internal change that can follow the release. The header-parse proposal below is open.
**Raised:** 2026-09-29, while working rework R6 (SIZE-02, SIZE-03, SIZE-05, SIZE-09, SIZE-10, MICRO-08, MICRO-19, WL-01…WL-06)
**Touches the wire / the public surface / a security boundary:** no — every figure below is a measurement of what R6 built; nothing here has been applied

## What was measured

Two Measurements on the same machine, one after the other, nothing else running:

- `Measurements/76b9aa5-20260928T172015Z` — the R6 format, **before** the trace seam: `ProfileMatrixBenchmarks`, `HeaderBenchmarks`, the size tables;
- `Measurements/76b9aa5-20260928T191759Z` — the same suites **after** the trace seam, plus `DumperBenchmarks` (MICRO-19).

Compared with BASE-02: each against `Baselines/pre-rework/` (`comparison-pre-rework.md` in each run), and the second against the first (`comparison-76b9aa5-20260928T172015Z.md` in the second run).

## Sizes — SIZE-02, SIZE-03, SIZE-05 (`payload-sizes.csv`, `pre-rework` → R6)

Every payload of every profile is smaller or equal; none grew.

| Checkpoint | pre-rework | R6 |
|---|---|---|
| SIZE-02 — the V1 envelope on DATA-01, B-P0 minus B-P7 | 29 bytes | 8 bytes |
| SIZE-03 — reference framing on DATA-01, B-P1 minus B-P0 | 5 bytes | 0 bytes (the frame replaces the null flag) |
| SIZE-03 — DATA-10 shared DAG under B-P1 | 7 200 bytes | 2 774 bytes (−61.5 %) |
| DATA-11 cyclic graph under B-P1 | 7 829 bytes | 4 334 bytes (−44.6 %) |
| SIZE-05 — per string, DATA-16 (50 000 strings) | 700 799 bytes | 650 778 bytes: one byte less per string (the null flag folded into the length) |
| SIZE-05 — per element of a nullable-element list, DATA-17 | 19 915 bytes | 19 393 bytes |
| DATA-06 deep graph, 5…511 levels, B-P1 | 161…13 854 bytes | 110…11 215 bytes (−19…−32 %) |
| DATA-01 TinyFlat, B-P0 | 120 bytes | 98 bytes (−18 %) |

## SIZE-09, SIZE-10 (`format-sizes.csv`, R6 only)

| Case | Bytes |
|---|---|
| header, no service | 8 |
| header, CRC-32 | 15 |
| header, CRC-32 + Deflate | 19 |
| header, CRC-32 + Deflate + AES-256-GCM with key id "k7" | 25 |
| wide nullable record, 16 members, all null | 17 |
| wide nullable record, 16 members, all set | 71 |
| list of 1 000 strings of 8 bytes | 9 002 |
| list of 1 000 null strings | 1 002 |

The fixed header it replaced was 29 bytes with no string, plus two bytes and the bytes of each custom name and key id, plus the checksum. Before the fold a null string cost one byte and a present one a flag byte more than now.

## MICRO-08 — the header (`components.csv`)

| Cell | pre-rework | R6, before the seam | Ratio |
|---|---|---|---|
| write, no service | 90.7 ns, 0 B | 31.8 ns, 0 B | ×0.35 |
| write, all fields | 232.3 ns, 192 B | 162.8 ns, 0 B | ×0.70 |
| parse, no service | 121.6 ns, 72 B | 36.4 ns, 0 B | ×0.30 |
| parse, all fields | 404.7 ns, 1 312 B | 562.3 ns, 1 376 B | **×1.39, slower** |
| build associated data | 84–125 ns, 240–608 B | — | gone: the associated data is the header |

**The one regression: parsing the widest header.** Three service records with custom names, a key id and a checksum are parsed in 562 ns against 405 ns. The likely costs, not yet profiled: each record is read through its own `WireReader.Slice`; the header is rebuilt with a `with` expression once per record (a copy of an eleven-field record struct each time); and the custom names are three strings plus the key id. The default header — the one almost every frame carries — is ×0.30.

Proposal for the owner, not applied: parse the records into locals and build the header once at the end, and read a body with the outer reader bounded by its length instead of a slice. Expected: the widest parse back below `pre-rework`; nothing changes on the wire.

## The trace seam — WL-01 / WL-04 / WL-06, before → after (`results.csv`, 78 cells each)

The seam adds one test of `OperationState.Trace` per value read — in every codec's read — and `WireReader` gained the offset a keyed-field window starts at. Writing is untouched by both, so the write cells are the control for the noise between the two runs.

| Cells | Median ratio | Range | faster / within error / slower |
|---|---|---|---|
| WL-01 serialize (control) | 0.998 | 0.86…1.37 | 31 / 16 / 31 |
| WL-04 deserialize | **1.015** | 0.92…1.29 | 16 / 16 / 46 |
| WL-06 round trip | 1.012 | 0.90…1.32 | 22 / 11 / 45 |

By dataset, the read median: DATA-01 ×1.005, DATA-02 ×1.017, DATA-03 ×1.032, DATA-04 ×1.021, DATA-05 ×1.024, DATA-07 ×0.999; the write median of the same datasets is ×0.99…×1.01. No allocation changed beyond the sampling of the diagnoser (a few bytes on 2–3 MB).

**Reading:** the control is symmetric around 1.0; the reads are not. The switched-off seam costs about 1.5 % on the median read and 2–3 % on reads of many small records, with no allocation. By the plan (§9.7, the R6 gate) that is a measurable difference, and the stage stops for the owner.

## MICRO-19 — the dumper (informational)

| Cell | Time | Allocated |
|---|---|---|
| `Dump<T>` of DATA-01 (12 nodes) | 2.2 µs | 4.4 KB |
| `Dump<T>` of DATA-04 (a node per record and per member) | 184 ms | 82 MB |
| `Dump<T>` of DATA-08 (900 000-byte array, 900 000 nodes) | 472 ms | 285 MB |

Linear in the number of nodes, which is what the checkpoint guards. A byte array is a node per byte, which is what makes DATA-08 heavy.

## Also in these runs

`cold-start.csv`: all 18 cells slower than `pre-rework` in both runs and within error between them — the typed engine's first use, already written up in PERF-06; the seam does not move it.
