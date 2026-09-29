# CLAUDE.md — changes required by the rework

**Class: historical.** The record of the pre-release rework. It binds nothing that ships, and its rules stop applying when `v1.0.0` is released; until then it is the evidence that R9 reconciles the system against.

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

### R1 — Wire primitives on buffers — applied

- **Architecture** — the layer diagram: `ValueReader / ValueWriter` becomes `WireReader / WireWriter`,
  `ref struct`s over buffers.
- **Three structural barriers** — 1, byte monopoly: named after `WireReader`/`WireWriter`; "every
  declared length is compared with the bytes physically remaining" restated against
  `WireReader.Remaining`.
- **Projects** — the `Io/` example of the namespace rule, if the folder or its types move. *(The
  folder stayed; nothing to change.)*
- Moved here from R2 by the owner's decision of 2026-09-27 (`Owner-Review.md` log 53), because V0
  now writes through `PayloadBuffer`: **Versioned envelope** (the V0 `NotSupportedException` sentence
  removed; the write is atomic) and **Member layouts** (the keyed V0 caveat removed). **Limits and
  budgets**: the read half — the `WireReader` classification and `Slice` window replace
  `MeteredReadStream`/`WindowReadStream`; `MeteredWriteStream` stays until R2.

### R2 — Pipeline on pooled buffers — applied

- **Architecture** — the pipeline row says phases are transforms over pooled buffers. *(The
  `OperationState` half of this entry moved to R4: `Contract-Changes.md` §2.2 and §6 place the
  struct in R4, and `Decisions.md` introduces it with the typed codecs that take it by `ref`. A
  transcription error corrected in R2.)*
- **Limits and budgets** — `MeteredReadStream`, `MeteredWriteStream` and `WindowReadStream` are gone:
  the wire budget, the exact remaining count and the keyed field window are properties of the reader
  and writer (plan §5.2). *(The read half was applied in R1; restating budget and phase policy
  against `OperationState` moved to R4 with the struct.)*
- **Versioned envelope** — applied in R1.
- **Member layouts** — keyed: applied in R1. References: cycle detection by the ancestor stack.
- **Where the authoritative information lives** — the `Streams/` folder of the test layout, as the
  QA plan now names it.

### R3 — Public surface and non-seekable reading — applied

- **Architecture** — the public API row: the buffer-first surface of plan §9; `StreamExtensions` is
  gone from the list of what sits in the bare `ViShap.Viper` namespace.
- **Versioned envelope** — "Reads therefore require a seekable stream" is replaced: the router decodes
  the magic from the buffered source, and every entry point reads a non-seekable source. The V0
  boundary rules of plan §7 and the asynchronous rule of plan §9.5 are stated. `BinaryHeaderPeek`,
  `FromHeader` and `FromStream` are no longer mentioned.

### R4 — Typed engine — applied

- **Architecture** — `SerializationOperation` becomes the per-call `OperationState`, a struct passed
  by reference *(moved here from R2)*; the formatter row: typed shapes with engine-owned codecs; a
  contracts row for `TypeContract<T>` / `ReflectedContract<T>`.
- **Limits and budgets** — budget and phase policy restated against `OperationState` *(moved here
  from R2)*.
- **Three structural barriers** — 3, engine-owned traversal: the codecs own the loop; a type contract
  receives only `MemberWriter` / `MemberReader`. `GraphReader`/`GraphWriter` renamed or removed as the
  code decides.
- **Adding a formatter** — rewritten whole: `IScalarFormatter<T>`, `ISequenceShape<TC,TE>`,
  `IMapShape<TM,TK,TV>`, typed composites, `EnumFormatter<TEnum>`, `FormatterCache<T>`, and what
  remains of `FormatterRegistry`. The sentence that `ITypeFormatter` stays internal is restated for the
  new interfaces.
- **Member layouts** — construction goes through the type contract; the reflected contract is the one
  shipped in v1.0.
- **Projects** — the `Cache/` folder is gone. *(`CLAUDE.md` never named the folder; nothing to change.
  `GraphReader`/`GraphWriter` were removed: the codecs own the traversal and `Graph` is the entry for
  one payload.)*

### R5 — Algorithm contracts — applied

- **Projects** — Core's list: the algorithm interfaces as they now are, and `HkdfKeyProvider` in
  whichever project plan §8 places it.
- **Algorithms** — one method per direction, no default members; the built-ins under their
  family-suffixed names (`DeflateCompression`, `BrotliCompression`, `Crc32Checksum`,
  `Aes256GcmEncryption`) and the new ones; `KeySizeInBytes` checked at `Build()`.

### R6 — The final format — applied

- **Versioned envelope** — the V1 header as a list of service records with its 4 KiB bound; the
  associated data is the exact header bytes; the write order restated.
- **Member layouts** — keyed field framing, the reference frame as one varint with null included, and
  the null fold, as plan §6.3 defines them.
- **Diagnostics** — a paragraph after the architecture: `ViShap.Viper.Diagnostics` reads a frame
  for a person — header, phases and, with a type, the payload as a tree with offsets, lengths, values,
  and the path and offset of a failure; it rides on the engine's trace seam (`OperationState.Trace`,
  null outside the dumper), which only codecs call. Dumps of the fixtures are the first thing to look
  at when a compatibility or oracle-style test fails.
- **Conventions** and **Where the authoritative information lives** — the fixtures are re-frozen once:
  the exception is recorded and the "never regenerated" rule restored in the same change
  (`Rework-Plan.md` §0). The oracle paragraph added in R0 is removed.

### R8 — Generator ground — applied

- **Architecture** — the reflection path's public entry points carry `[RequiresDynamicCode]` /
  `[RequiresUnreferencedCode]`; the conformance suite `CONF-*` is named beside the contract seam.

### R9a — Reconciliation — applied

- **Where the authoritative information lives** — `Architecture-Audit.md` becomes the historical
  record of the first rework; `internal/rework/` is described as completed; `docs/` as it then is.
- **Current state** — rewritten for the released system: test count, stages closed, fixtures.
- **Where the authoritative information lives** — every file under `internal/` listed with its class:
  normative, plan, operational or historical. A historical file is named as such, with the note that
  its rules no longer apply [D9.28].
- The whole file read once against `src/`, `tests/` and the contract, and searched for every
  `Retired.md` entry; anything a stage missed is fixed here and reported.
