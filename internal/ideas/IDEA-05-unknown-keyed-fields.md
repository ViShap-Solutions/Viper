# IDEA-05 — Preserving unknown keyed fields

**Class: plan.** **Status:** idea. Recorded, not scheduled, in `rc2/RC2-Plan.md` §2.

## What and why

A keyed reader skips a field whose key it does not know. If it then writes the object back, the field is
lost: an old service in the middle of a pipeline strips what a newer service added. Protocol Buffers keep
unknown fields for exactly this round trip.

## Where it was written

- `Audit-Future.md` §7.

## What is already fixed

- Unknown keys are length-skipped (contract §14.2, §22.3); this is what makes keyed evolution tolerant.
- Reference ids are explicit and visible only along the ancestor chain, so a skipped field takes part in
  nothing (contract §16.2). Preserving a field's bytes would replay a frame whose reference ids belong to
  the old graph.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | An opt-in member of type `BinaryUnknownFields` (opaque, holds key + bytes) that the contract fills on read and replays on write | Lossless round trip through an old service | Incompatible with `PreserveReferences` unless replayed bytes are refused or re-based; a budget for retained bytes; the bytes are opaque to the holder |
| b | The same, refused under `PreserveReferences` (`BinaryConfigurationException` at `Build()`) | Simple and sound | Covers only reference-free payloads |
| c | Do nothing | No new surface | Old services strip new fields |

Recommended when taken up: **b** first; a only after reference re-basing is designed.

## Open questions

1. Is a pass-through service a real topology for a consumer?
2. Do retained bytes count against the payload budget on write? (Recommended: yes.)

## Version impact

| Surface | Version |
|---|---|
| A new public type and an opt-in member; existing contracts unchanged | **minor** |
| Turning preservation on for every keyed contract | **major** (output changes) |

## Preconditions

- A design of the interaction with reference scopes (contract §16.2).
