# IDEA-17 — Length padding of an encrypted frame

**Class: plan.** **Status:** idea.

## What and why

An encrypted frame hides its content but not its size: the ciphertext is exactly as long as the
(compressed) payload plus the cipher's overhead, and the header states `onDiskLength`. An observer
learns the length of every value, and with compression before encryption, an attacker who can place
data beside a secret and watch the frame's length can learn about the secret (the CRIME/BREACH class).
Padding the plaintext to a fixed set of sizes before encryption hides the exact length.

## Where it was written

- The owner's review of an outside assessment of `v1.0.0`, 2026-10-27, which named the exact
  `GetCiphertextLength` as a limit on random padding.

## What is already fixed

- `IEncryptionAlgorithm.GetCiphertextLength` is exact (contract §13), which is what lets a frame be sized
  and budget-checked before the destination is touched. Random-length padding inside a cipher is
  therefore not expressible, and need not be: padding chosen by the pipeline before encryption keeps
  every length exact.
- The plaintext of an encrypted frame is not declared in the header; it is checked after decryption,
  before decompression (contract §5.10). A padding length inside the plaintext fits that order.
- A new service record is a **minor** on the wire: an old reader refuses only the frames that carry it,
  with `BinaryFormatNotSupportedException` (`Development-Workflow.md` §5.3). `IDEA-01` also proposes a new
  critical record; numbers are assigned when either is taken.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | Nothing; `docs/algorithms-and-keys.md` states that an encrypted frame reveals its length, and that compressing secrets beside attacker-controlled data leaks through it | An honest statement | No mitigation |
| b | `WithLengthPadding(bucket)`: the plaintext padded to the next multiple of a fixed size, the padding length stored inside the encrypted plaintext; a critical record marks a padded frame | Lengths hidden to the bucket; one integer to configure | Up to one bucket of extra bytes per frame; a read checks the padding within the phase budget |
| c | b with power-of-two buckets | Fewer distinct sizes on the wire | Up to twice the size |
| d | b with a custom padding policy | Fits any traffic shape | A policy interface in Core, held to an exact length like the algorithms |

```text
plaintext before encryption:   [ compressed payload ][ 0x00 … 0x00 ][ varint padding length ]
                               └──────────── a multiple of the bucket ─────────────┘
```

Recommended when taken up: **a** at once — it changes only a document — and **b** when a consumer needs
it. `Build()` refuses padding without encryption: without it the padding hides nothing.

## Open questions

1. Is the padding length a varint at the end of the plaintext, or a fixed-width field?
2. Does `RequireEncryption` imply anything about padding, or is a `RequireLengthPadding` policy needed
   to refuse an unpadded frame?
3. Is the bucket recorded in the header record (an observer learns the policy, not the length) or
   implied?

## Version impact

| Option | Surface | Version |
|---|---|---|
| a | documents | **patch** |
| b, c | API: a builder method; wire: a new critical record, never written by default | **minor** |
| d | API: a policy interface in Core | **minor** |

## Preconditions

- The order of phases unchanged: checksum over the raw payload → compress → pad → encrypt; the
  checksum still covers the payload, not the padding.
- Hostile tests for a padding length larger than the plaintext and for padding bytes that are not zero.
