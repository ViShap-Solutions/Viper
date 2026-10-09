# IDEA-12 — Error correction (Reed–Solomon)

**Class: plan.** **Status:** idea. Proposed 2026-10-09; not for `rc.2`.

## What and why

The checksum and the AEAD tag **detect** damage; a damaged frame is refused. Reed–Solomon parity would let
a reader **repair** a frame: archives, unreliable media, one-way links and channels with no retransmission.
It would set Viper apart; whether any library of the `Benchmark-Plan.md` §5 roster offers it is to be
checked when the idea is taken up.

## Where it was written

- The owner's request, 2026-10-09. No earlier document names it.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | **Inside the format**: a new header service (number after encryption) carrying the code parameters, parity appended to the frame | One frame, one read call | See the costs below — they are structural |
| **b** | **Outside the format**: a package `ViShap.Viper.ErrorCorrection` that takes a finished Viper frame as opaque bytes, wraps it in a code, and on read repairs it before `Deserialize` | Format, engine, invariants and AAD untouched; the engine never sees parity or a decoder; usable with any frame, V0 included | A second container around the frame; two calls (or a helper) instead of one |
| c | Do nothing; rely on the storage and transport layers | No surface | No repair where those layers do not provide it |

Costs of **a**, each checked against the contract:

- **Decoder over hostile input.** A new attack surface inside the read path. The BCL has no Reed–Solomon;
  it means GF(256) code written here or a dependency, and no native dependency is accepted in the shipped
  packages (`Rework-Plan.md` §15).
- **The header protects itself.** The code parameters must sit in the header, which then needs protection
  of its own — a circle. The header is the AEAD's associated data and has a hard 4 096-byte bound
  (contract §11, §22).
- **Canonical form (INV-9).** Different frame bytes — parity outside the AAD in particular — would decode
  to the same frame, breaking "every wire field has one encoding".
- **Limits and budgets.** Parity inflates `onDiskLength`; `MaxDecompressionRatio` (§5.10) and the wire
  budget need new rules for it.
- **A new algorithm family.** It is neither compression, nor checksum, nor cipher: a new `I*Algorithm`
  interface in Core. A separate MAC family is already out of scope (`rework/Rework-Plan.md`, out-of-scope
  list).
- **Applicability.** Disks, ZFS, TCP and QUIC already correct errors below the frame.

Recommended: **b**, and only if a consumer needs repair; **c** otherwise. **a** is not recommended.

## Option b in more detail

- Container: magic, version, code parameters (data and parity shard sizes), shards with their parity, a
  checksum per shard so the decoder knows which shards are erasures (erasure decoding corrects twice as
  many as blind error decoding).
- Its own limits: a maximum container size and a maximum parity ratio, validated before any allocation;
  the decoder is held to the same hostile-input discipline as the engine (every length checked against
  the bytes present).
- Its own exceptions under `BinarySerializerException` (an unrecoverable container is
  `BinaryIntegrityException`), so a caller's `catch` stays one.
- GF(256) arithmetic is small and well specified; a managed implementation of our own is feasible, with
  published test vectors and a property test (any `parity` shards lost → recovered).

## Open questions

1. Is there a consumer who needs repair rather than detection?
2. For b: own GF(256) implementation or a dependency? A package of this repository, shipped on the
   libraries' tag (`cd.yml` packs every package from one tag), or a cycle of its own?

## Version impact

| Form | Surface | Version |
|---|---|---|
| b — a new package; no change to the libraries | none in the existing packages | **minor** for the repository's cycle |
| a — a new header service, written only when asked | API, wire | **minor** (§5.3), but with the structural costs above |
| a — on by default | wire | **major** |

## Preconditions

- A concrete consumer need.
- For b: a design brief for the container, approved by the owner, before any code.
