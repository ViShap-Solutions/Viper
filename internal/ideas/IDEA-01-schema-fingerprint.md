# IDEA-01 — Schema fingerprint

**Class: plan.** **Status:** idea. Deferred from `v1.0.0-rc.2` by the owner on 2026-10-09
(`rc2/RC2-Plan.md` §3.9, F1).

## What and why

A positional payload carries no member names and no types: a reader that uses the wrong class, or an
older version of the right one with members reordered, reads values into the wrong members. It either
fails somewhere in the middle with a `BinaryFormatException` that names a symptom, or — when the kinds
happen to line up — succeeds with wrong data. A fingerprint of the root type's shape, written into the V1
header, lets the reader refuse such a frame at once and say why.

## Where it was written

- `rework/Decisions.md` D9.2 — "a new critical service with a new number", after the release.
- `rework/Owner-Review.md` P10, D-2 — the questions: CLR type names, nested and generic types; a keyed
  contract exists to evolve, so only positional layouts make sense; extensible header records allow it to
  be added later without breaking anything.
- `rework/Rework-Plan.md` §13.
- `rc2/RC2-Plan.md` §3 F1, stage P5a (removed from the plan when deferred).

## What is already fixed

- The header carries service records `(number << 1) | critical`, length, body, in ascending number
  (contract §11, §22). Checksum is 1, compression 2, encryption 3; the fingerprint would be **4**,
  critical, after encryption, inside the 4 096-byte header bound.
- V1 only: V0 has no header.
- An unknown critical record is `BinaryFormatNotSupportedException` for a reader that predates it
  (contract §22), so an old reader refuses a fingerprinted frame clearly and reads every other frame as
  before.
- The value must come from the contract description, so the reflected and the generated contract give
  the same fingerprint (INV-12, `CONF-*`).

## Options — what is hashed

| | What | Gives | Costs | Example |
|---|---|---|---|---|
| a | Ordered member names and CLR type names | Detects every change | A renamed class or member breaks the fingerprint though the wire is compatible; CLR names differ by assembly version and platform | `Order{Id:System.Int32,Lines:List<Line>}` → hash |
| **b** | The wire shape of the root: for a positional contract, the ordered sequence of member wire kinds, recursively through member-encoded children; for a keyed contract nothing | Survives renames; identical for reflected and generated contracts; catches what it exists for | A reorder of two members of the same kind is not detected — the wire cannot tell them apart either | `P[i32, seq[P[str, i32]]]` → hash |
| c | A user-declared number, `[BinarySchema(3)]` | Trivially stable, human-controlled | Detects nothing the user forgets to bump | `[BinarySchema(3)] class Order` |

Recommended: **b** — it is the only one that is both automatic and does not punish a compatible change.

To decide with it:

- **Exception.** Recommended `BinaryFormatException` ("schema mismatch"), carrying both values. Not
  `BinaryIntegrityException`: nothing was tampered with.
- **Switch.** Recommended opt-in, `WithSchemaFingerprint()` on the builder; the default writes nothing.
- **Hash.** A 64-bit non-cryptographic hash (XxHash3, already a dependency) over a canonical byte
  spelling of the shape; the spelling is part of the contract so a second implementation computes the
  same value.
- **Reader without the option** — recommended: it verifies a record that is present, since the frame
  asks for it.

## Open questions

1. b, a or c.
2. Recursion: does the fingerprint descend into member-encoded children, into union arms, into element
   types of collections? (Recommended: yes for all three, stopping at keyed contracts.)
3. Does a positional root with a keyed child hash the child as "keyed" only? (Recommended: yes.)

## Version impact

| Form | Surface | Version |
|---|---|---|
| Opt-in (recommended) | API: one builder method; wire: a new service number, written only when asked | **minor** (`Development-Workflow.md` §5.3: "a new header service") |
| On by default | wire: every frame changes; old readers refuse every new frame | **major** (§5.2) |

## Preconditions

- The contract description is identical between reflected and generated contracts — holds since P4.
- New frozen fixtures are **added** for the fingerprinted frame; no existing fixture changes.
