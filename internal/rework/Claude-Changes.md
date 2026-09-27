# CLAUDE.md — changes required by the rework

**Applies to:** `CLAUDE.md` as of 2026-09-27.
**Rule:** `CLAUDE.md` is what every agent and every skill reads before touching the repository, so it
may not describe a system that no longer exists, not even between two stages. Each stage rewrites the
passages its code makes untrue, in the same change as the code, and never ahead of it
(`Rework-Plan.md` §0). The file keeps the conventions of `CLAUDE.md` itself: it describes the code as it
is, with no stage names, no decision IDs and no history.

Stages refer to `Rework-Plan.md` §12; plan sections are cited as "plan §n". Sections of `CLAUDE.md` are
cited by their heading.

The skills under `.claude/skills/` are outside the files a stage may change. They are reconciled once,
in R9a, with the owner's approval (`Rework-Plan.md` R9a); a skill that turns out to be misleading
earlier is reported to the owner by the stage that notices it.

---

# 1. R0 — applied

- **Where the authoritative information lives** — the `internal/rework/` paragraph: the stage has
  started, and the change files are four, this one included. The Progress table of `Rework-Plan.md`
  is where the current stage is read, so the paragraph names it instead of a stage.
- **Where the authoritative information lives** — after the paragraph on `Fixtures/Wire/*.bin`: the
  byte oracle `Fixtures/Oracle/oracle.txt` and `Format/OracleTests`; a mismatch is a change of
  behaviour fixed in `src/`, never a value to re-record.

# 2. Stage by stage

### R1 — Wire primitives on buffers

- **Architecture** — the layer diagram: `ValueReader / ValueWriter` becomes `WireReader / WireWriter`,
  `ref struct`s over buffers.
- **Three structural barriers** — 1, byte monopoly: named after `WireReader`/`WireWriter`; "every
  declared length is compared with the bytes physically remaining" restated against
  `WireReader.Remaining`.
- **Projects** — the `Io/` example of the namespace rule, if the folder or its types move.

### R2 — Pipeline on pooled buffers

- **Architecture** — `SerializationOperation` becomes the per-call `OperationState`, a struct passed
  by reference; the pipeline row says phases are transforms over pooled buffers.
- **Limits and budgets** — `MeteredReadStream`, `MeteredWriteStream` and `WindowReadStream` are gone:
  the wire budget, the exact remaining count and the keyed field window are properties of the reader
  and writer (plan §5.2). Budget and phase policy restated against `OperationState`.
- **Versioned envelope** — V0 no longer writes straight through: every write goes through the
  serializer's own buffer, is atomic, and a keyed write needs no seekable destination. The
  `NotSupportedException` sentence is removed.
- **Member layouts** — keyed: the length is patched in the serializer's buffer, so no layout needs a
  seekable stream; the V0 caveat is removed. References: cycle detection by the ancestor stack.
- **Where the authoritative information lives** — the `Streams/` folder of the test layout, as the
  QA plan now names it.

### R3 — Public surface and non-seekable reading

- **Architecture** — the public API row: the buffer-first surface of plan §9; `StreamExtensions` is
  gone from the list of what sits in the bare `ViShap.Viper` namespace.
- **Versioned envelope** — "Reads therefore require a seekable stream" is replaced: the router decodes
  the magic from the buffered source, and every entry point reads a non-seekable source. The V0
  boundary rules of plan §7 and the asynchronous rule of plan §9.5 are stated. `BinaryHeaderPeek`,
  `FromHeader` and `FromStream` are no longer mentioned.

### R4 — Typed engine

- **Architecture** — the formatter row: typed shapes with engine-owned codecs; a contracts row for
  `TypeContract<T>` / `ReflectedContract<T>`.
- **Three structural barriers** — 3, engine-owned traversal: the codecs own the loop; a type contract
  receives only `MemberWriter` / `MemberReader`. `GraphReader`/`GraphWriter` renamed or removed as the
  code decides.
- **Adding a formatter** — rewritten whole: `IScalarFormatter<T>`, `ISequenceShape<TC,TE>`,
  `IMapShape<TM,TK,TV>`, typed composites, `EnumFormatter<TEnum>`, `FormatterCache<T>`, and what
  remains of `FormatterRegistry`. The sentence that `ITypeFormatter` stays internal is restated for the
  new interfaces.
- **Member layouts** — construction goes through the type contract; the reflected contract is the one
  shipped in v1.0.
- **Projects** — the `Cache/` folder is gone.

### R5 — Algorithm contracts

- **Projects** — Core's list: the algorithm interfaces as they now are, and `HkdfKeyProvider` in
  whichever project plan §8 places it.
- **Algorithms** — one method per direction, no default members; the built-ins under their
  family-suffixed names (`DeflateCompression`, `BrotliCompression`, `Crc32Checksum`,
  `Aes256GcmEncryption`) and the new ones; `KeySizeInBytes` checked at `Build()`.

### R6 — The final format

- **Versioned envelope** — the V1 header as a list of service records with its 4 KiB bound; the
  associated data is the exact header bytes; the write order restated.
- **Member layouts** — keyed field framing, the reference frame as one varint with null included, and
  the null fold, as plan §6.3 defines them.
- **Conventions** and **Where the authoritative information lives** — the fixtures are re-frozen once:
  the exception is recorded and the "never regenerated" rule restored in the same change
  (`Rework-Plan.md` §0). The oracle paragraph added in R0 is removed.

### R8 — Generator ground

- **Architecture** — the reflection path's public entry points carry `[RequiresDynamicCode]` /
  `[RequiresUnreferencedCode]`; the conformance suite `CONF-*` is named beside the contract seam.

### R9a — Reconciliation

- **Where the authoritative information lives** — `Architecture-Audit.md` becomes the historical
  record of the first rework; `internal/rework/` is described as completed; `docs/` as it then is.
- **Current state** — rewritten for the released system: test count, stages closed, fixtures.
- **Where the authoritative information lives** — every file under `internal/` listed with its class:
  normative, plan, operational or historical. A historical file is named as such, with the note that
  its rules no longer apply [D9.28].
- The whole file read once against `src/`, `tests/` and the contract, and searched for every
  `Retired.md` entry; anything a stage missed is fixed here and reported.
