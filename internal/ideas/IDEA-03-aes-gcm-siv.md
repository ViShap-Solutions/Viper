# IDEA-03 — AES-GCM-SIV

**Class: plan.** **Status:** idea. Deferred from `v1.0.0-rc.2` by the owner on 2026-10-09
(`rc2/RC2-Plan.md` §3.9, A1).

## What and why

AES-GCM loses confidentiality and authenticity for every frame under a key once a nonce repeats.
AES-GCM-SIV (RFC 8452) is nonce-misuse resistant: a repeated nonce reveals only that two plaintexts were
equal. It is for consumers who cannot guarantee unique nonces — many writers sharing one key, restored VM
snapshots.

## Where it was written

- `rework/Decisions.md` D9.2, D9.12; `rework/Rework-Plan.md` §13; `Audit-Future.md` §2.
- `rc2/RC2-Plan.md` §3 A1, stage P5e (removed from the plan when deferred).

## What is already fixed

- Built-in algorithms take an enum value of their own, like `Aes256Gcm = 1` and `ChaCha20Poly1305 = 2`
  (the owner, 2026-10-09, for Zstandard and LZ4 — `Custom` is for the consumer's own algorithms, not for
  the library's). AES-GCM-SIV would be `EncryptionAlgorithm.Aes256GcmSiv = 3`.
- It implements `IEncryptionAlgorithm` exactly: `KeySizeInBytes` 32, an exact `GetCiphertextLength`
  (plaintext + 16-byte tag + nonce as the frame carries it), associated data always taken.
- The BCL does not provide it (net10). No native dependency is accepted.

## Options — where the primitive comes from

| | Option | Gives | Costs |
|---|---|---|---|
| a | A maintained managed library (candidate: `BouncyCastle.Cryptography`, MIT — GCM-SIV support, maintenance and AOT/trim cleanliness to be verified) | Proven implementation | A large dependency in `ViShap.Viper.Serialization` for one algorithm |
| b | Our own implementation over `System.Security.Cryptography.Aes` (ECB block) and POLYVAL | No dependency | Cryptographic code written here; constant-time POLYVAL; a security review the project does not have |
| c | Wait until the BCL provides it | No dependency, platform-maintained | No date; may never come |

Recommended when the idea is taken up: **a** if a library passes the checks (managed, maintained, clear
licence, no AOT warning, RFC 8452 vectors pass); otherwise **c**. Never b without an external review.

## Open questions

1. Is nonce-misuse resistance needed by a real consumer? Viper draws a fresh random 96-bit nonce per
   frame, which is safe for about 2³² frames per key; `HkdfKeyProvider` per-key-id derivation narrows it
   further.
2. If a large dependency is the only path: in `Serialization`, or the one exception where an algorithm
   lives in a package of its own?

## Version impact

| Surface | Version |
|---|---|
| API: a new class and enum value; wire: a new algorithm id, written only when chosen | **minor** (§5.3, §5.5 "new built-in algorithm") |

## Preconditions

- A dependency that passes the checks above, or an external review of an own implementation.
