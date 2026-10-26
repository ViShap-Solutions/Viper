# IDEA-09 — Constant-time checksum comparison

**Class: plan.** **Status:** idea. Recorded, not scheduled, in `rc2/RC2-Plan.md` §2 (contract §21.3,
"deferred by design, not claimed").

## What and why

The checksum service compares the stored and computed hash with an ordinary span comparison, which can
return early at the first differing byte. For a keyed MAC that leaks timing; for an unkeyed checksum it
does not matter, because an attacker can compute the right value themselves.

## Where it was written

- Contract §21.3.

## What is already fixed

- Viper's checksums (CRC-32, XxHash3, XxHash128) are unkeyed integrity checks, not authentication.
  Authentication is the AEAD cipher's tag, compared by the BCL in constant time.
- A custom `IChecksumAlgorithm` could be keyed; the service would compare its output the same way.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | `CryptographicOperations.FixedTimeEquals` in the checksum service | Safe for a keyed custom checksum | Nanoseconds per frame |
| b | Do nothing; document that a checksum is not authentication | No change | A keyed custom checksum would leak timing |

Recommended when taken up: **a** — the cost is negligible and it removes the caveat.

## Open questions

None beyond whether to do it.

## Version impact

| Surface | Version |
|---|---|
| Internal comparison only; no API, wire or behavior change | **patch** (§5.4, internal change) |

## Preconditions

None.
