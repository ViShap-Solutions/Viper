# IDEA-10 — Asynchronous engine

**Class: plan.** **Status:** idea. Recorded, not scheduled, in `rc2/RC2-Plan.md` §2 (contract §21.3,
"deferred by design").

## What and why

Asynchronous methods await only the frame's bytes; the engine runs synchronously over a frame held in
memory. A frame therefore occupies memory up to its size, bounded by the wire budget. An asynchronous
engine would decode while bytes arrive, for very large frames over slow transports.

## Where it was written

- Contract §21.3; `CLAUDE.md`, "Versioned envelope" ("the engine never awaits").

## What is already fixed

- `WireReader`/`WireWriter` are `ref struct`s over memory, which is what makes every length check exact
  against `Remaining` (barrier 1). A `ref struct` cannot cross an `await`.
- Encryption and checksums need the whole frame before the payload may be trusted.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | Do nothing; large data goes as several frames | Barriers untouched | A frame is held whole |
| b | Resumable codecs over a `PipeReader`, plaintext unphased frames only | Bounded memory for huge unprotected frames | A second engine whose length checks are no longer exact against memory; every barrier re-proved |
| c | A chunked V2 envelope (IDEA-08 b) where each chunk is a whole in-memory frame | Bounded memory with the existing engine | Depends on IDEA-08 |

Recommended when taken up: **c** through IDEA-08; b is not consistent with the byte monopoly as designed.

## Open questions

1. Is there a consumer whose frames exceed what they can hold in memory?

## Version impact

| Surface | Version |
|---|---|
| New asynchronous overloads; existing methods unchanged | **minor** |

## Preconditions

- IDEA-08 b, if option c.
