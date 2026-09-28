# PERF-07 — Remove the fixed per-call allocations the frame still makes

**Status:** Open
**Raised:** 2026-09-28, while working rework R4 (ALLOC-10…ALLOC-20)
**Touches the wire / the public surface / a security boundary:** no

## What was measured

`AllocationTargetBenchmarks` in `Measurements/48c7bf5-20260928T082209Z`, publication job,
`MemoryDiagnoser`, bytes per operation after warm-up:

| Cell | Target (`Rework-Plan.md` §11) | Measured | Reference cell |
|---|---|---|---|
| ALLOC-10 write V1 → `IBufferWriter` | 0 | 272 | — |
| ALLOC-11 write V0 → `IBufferWriter` | 0 | 136 | — |
| ALLOC-15 `Serialize` → `byte[]` | the array | 384 | the array alone: 112 |
| ALLOC-16 `SerializePooled` | one `PooledPayload` | 304 | — |
| ALLOC-17 read a record of primitives | the record | 152 | the record alone: 80 |
| ALLOC-18 read a graph of 256 records | the graph | 22 659 | the graph alone: 22 584 |

The engine itself adds nothing to any of these: `Limits/AllocationTests` (ALC-01…ALC-05) shows the
record costs exactly what a null root costs on write, and exactly the returned objects beyond a
null root on read, in Debug and Release. What remains is the frame around the payload.

## What the numbers suggest

Two sources, both measured by allocation sampling during R4:

- **Write, 136 B per buffer.** Each `PayloadBuffer` is an object with two `List<>`s (72 + 32 + 32 B).
  V0 builds one, V1 two — the payload and the header — which is 136 and 272 B exactly. The segments
  themselves come from the pool.
- **Read, 72 B.** `AlgorithmCatalog.ResolveCompression/ResolveChecksum/ResolveEncryption` return
  `new NoCompression()`, `new NoChecksum()`, `new NoEncryption()` (or `new Deflate()`, …) on every
  read: three objects of 24 B. ALLOC-17: 80 B record + 72 B = 152 B, exactly.

## What is proposed

1. **Reuse `PayloadBuffer` objects** — a per-thread cache of two instances, taken and returned the
   way the reference tables are (`Rent`/`Return`, cleared, capped). `EncodedFrame` returns them when
   disposed. The write targets of ALLOC-10, ALLOC-11 and ALLOC-16 then reach 0 and one
   `PooledPayload`.
2. **Return shared instances of the stateless built-in algorithms** from `AlgorithmCatalog`. They
   hold no state, and a custom algorithm keeps its own registration. ALLOC-17 then reaches the
   record alone. The catalog is rewritten in R5, which is the natural place.

## What it would cost

(1) must keep INV-15: a buffer in the cache is always cleared and never shared between two live
frames; a frame disposed on another thread (after an `await`) returns its buffers to that thread's
cache, which is harmless. A double dispose must not return one buffer twice. (2) is trivial.

## What is not known

The time these allocations cost is not separated from the rest of the call; at 136–272 B per call
it is likely a few nanoseconds and matters for GC pressure under load, not for a single call.
ALLOC-12 (Brotli, 296 B), ALLOC-13 (Deflate, 840 B, a `DeflateStream`) and ALLOC-14 (AES-GCM,
432 B) include the phases' own objects, which R5 rewrites; they are not attributed here.

## Outcome

Filled in by the repository owner.
