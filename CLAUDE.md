# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Targets .NET 10 (`net10.0`), SDK 10.0.x.

```powershell
dotnet restore Viper.sln
dotnet build Viper.sln --configuration Release
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj

# Single test / class / trait
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj --filter "FullyQualifiedName~Serialize_Deserialize_ByteArray_ReferenceType"
dotnet test tests/ViShap.Viper.Serialization.Tests/ViShap.Viper.Serialization.Tests.csproj --filter "FullyQualifiedName~HostileInputTests"

# Benchmarks (BenchmarkDotNet; must run Release)
dotnet run --project benchmarks/ViShap.Viper.Serialization.Benchmarks --configuration Release
```

CI (`.github/workflows/ci.yml`) runs restore → build → the serialization test project on every PR/push to `main`. CD (`cd.yml`) fires on `v*` tags: it *requires the tag to point exactly at `origin/main` HEAD*, packs all three packages with `-p:Version=<tag minus v>`, and pushes to NuGet. Version comes solely from the tag — no version properties in the `.csproj` files.

## Where the authoritative information lives

`docs/` is reserved for the official, consumer-facing Viper documentation shipped at release; it is
currently empty pending that content. Everything below is engineering material — for the contributor
and for Claude Code — and lives under `internal/`.

- `internal/System-Contract.md` — **the normative contract and the source of truth.** Public API surface
  (§3), limits and budgets (§5–6), stream mechanisms (§7), exception taxonomy (§8), versions and
  header (§10–11), compression and encryption (§12–13), member layouts (§14), polymorphism (§15),
  references (§16), the byte-level wire format (§22), the supported types with their encodings (§23),
  and the release checklist (§24). Read the relevant section before changing behavior; update it in
  the same commit when behavior changes.
- `internal/Architecture-Audit.md` — why the architecture looks like this: the audit that produced it,
  the alternatives that were rejected and why, the invariants, and the implementation status.
- `internal/QA-Plan.md` — the release-gate test plan, realigned with the contract. Checkpoint list only,
  staged M0–M8; §30 records confirmed defects and the resolved contract questions. The method for
  working it lives in the `viper_tester` skill, not in the plan.
- `internal/Benchmark-Plan.md` — the post-release performance plan, realigned with the contract, measured
  against the `v1.0.0` tag and re-run per v1.x. It gates no release: correctness ships a version,
  and an open item here blocks only a performance *claim* (§28, and the quality gate of §24). Checkpoint
  list only, staged B0–B9: the competitor roster and why each library is in or out, the capability
  tiers that keep a comparison like-for-like, the data corpus, the workloads, the fairness rules and
  §27 for findings. The method for working it lives in the `viper_bencher` skill, not in the plan.
  Benchmark work is read-only over the library: it touches `benchmarks/`, the plan itself and
  `internal/performance/`, and nothing else. Its §18 component suites measure internals directly and
  granted an `InternalsVisibleTo`.
- `internal/performance/` — where a measurement becomes a suggestion and stops: one `PERF-nn-*.md` per
  proposed optimization or extension point, cited to the cells that motivate it, for the owner to
  decide on. Nothing here has been applied.
- `internal/audit/` — the historical record of the audit that led to the rework: the original probes
  (`Problems.cs`, superseded, do not compile), the first remediation design and its review. Kept for
  provenance; `Problems.cs` maps each finding to the test that now pins it.

Current state: the architecture rework described in the audit is complete and `src/` matches the
contract. The public API is fully XML-documented and `GenerateDocumentationFile` is on, so the docs
ship beside the assemblies. CS1591 stays a warning — `Api/PublicSurfaceTests` is what holds the line,
by comparing the exported surface with the generated XML file.

Public XML documentation is written for the NuGet consumer reading it on hover: what the member does,
what it takes, what it returns, which exception it raises. It never cites `internal/System-Contract.md` and
never records project history.

The test project follows the layout in `internal/QA-Plan.md` §2 — `Algorithms/`, `Api/`, `Concurrency/`,
`Contracts/`, `Diagnostics/`, `Exceptions/`, `Fixtures/`, `Format/`, `Hostile/`, `Limits/`,
`Metadata/`, `References/`, `RoundTrip/`, `Streams/`. Shared helpers live in `Fixtures/` (`AssertEx`,
`Wire`, `Mutate`, `Concurrent`, `Cultures`, stream doubles) and are themselves tested. A test names no
culture and no time zone: those come from the host, so the suite is green with and without
globalization data (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`).

`Fixtures/Wire/*.bin` are the frozen v1.0.0 payloads, read by `Format/CompatibilityTests` against the
frozen shapes in `Fixtures/Compatibility.cs`. They are never regenerated: a rebuilt fixture agrees
with whatever the code became, so a failure there is a compatibility break, not a fixture to refresh.

Every stage M0 through M8 is closed: nothing hand-written survives, and
`Api/PublicSurfaceTests` compares the exported surface against §3 by reflection, so adding a public
type fails the build's test run until the contract lists it.

## Projects

- `ViShap.Viper.Core` — contracts only, no dependencies: attributes, the `CompressionAlgorithm`/`ChecksumAlgorithm`/`EncryptionAlgorithm` enums with their `I*Algorithm` primitives, `SecretKey`/`IKeyProvider`, and the exception hierarchy (all derive from `BinarySerializerException`). Core carries **no policy**: no limits, no orchestration, nothing that enforces a resource ceiling.
- `ViShap.Viper.Serialization` — the entire engine. Depends on Core + `System.IO.Hashing`.
- `ViShap.Viper` — meta-package, references both, ships no code.

**Namespaces do not follow the folder/assembly layout.** Everything roots at `ViShap.Viper.*` regardless of project (e.g. `src/ViShap.Viper.Serialization/Io/` → `ViShap.Viper.Io`). The *public* API (`BinarySerializer`, `BinarySerializerOptions`, `StreamExtensions`, the attributes) sits in the bare `ViShap.Viper` namespace so consumers need one `using`. `GlobalUsings.cs` imports every sub-namespace, so new files in the Serialization project usually need no `using` for in-project types.

Most engine types are `internal`; `AssemblyInfo.QA.cs` grants `InternalsVisibleTo("ViShap.Viper.Serialization.Tests")`.

## Architecture

Documented normatively in `internal/System-Contract.md` §2; the reasoning behind it is in `internal/Architecture-Audit.md`. Layers, top to bottom:

```text
BinarySerializer            creates exactly one SerializationOperation per public call
SerializationOperation      Limits snapshot, Budget, PhaseBudget, Keys, policies
FormatPipeline (V0 | V1)    framing, phase order, header + AAD, phase sizes
PayloadEngine               traversal: depth, graph nodes, references, TypeContract
ValueReader / ValueWriter   the only access to payload bytes
Formatters                  type encoding only
Algorithms                  pure mechanics over spans
```

Dependencies point strictly downwards. **No type below `Pipeline/` may reference `SerializationLimits`.**

### Three structural barriers

These are why the codebase does not carry a security check in every class. Do not work around them:

1. **Byte monopoly.** `ValueReader`/`ValueWriter` (`Io/`) are the only types that touch payload bytes; there is no raw stream accessor. Fixed-size reads throw `BinaryFormatException` on truncation, strings and blobs are bounded by their limits, and every declared length is compared with the bytes physically remaining before anything is allocated.
2. **Validated counts.** A loop bound over wire data exists only as an `ElementCount`, whose sole factory checks the count against its limit and charges the element budget. There is no other way to obtain one, so "read a length, then allocate" is not expressible.
3. **Engine-owned traversal.** `GraphReader`/`GraphWriter` (`Engine/`) own all recursion: depth scopes, node budget, reference identity and scopes, cycle detection, the keyed layout, and the element loop of every container. A formatter never writes a loop over attacker-controlled data.

### Adding a formatter

Pick the shape, implement its interface (`Formatters/ITypeFormatter.cs`), and register it in `FormatterRegistry` **before** anything that would also claim the type:

- `IScalarFormatter` — self-contained values with no children and no data-driven allocation.
- `ISequenceFormatter` — element type plus a builder; the engine owns count, loop, depth, nodes and identity. Set `BuilderIsInstance = false` when the final object only exists after `Complete`, and `ReverseOnWrite` for LIFO containers.
- `IMapFormatter` — the same, for key/value entries.
- `ICompositeFormatter` — a fixed, type-determined child layout (tuples, pairs, lazies) or an irregular one (array rank). The engine has already charged depth, nodes and identity; any count still comes from `ReadCount`.

No shape fits a plain object: a type no formatter claims is member-encoded through `TypeContract`. `FormatterRegistry.Resolve` returning `null` means exactly that — there is no catch-all formatter that could shadow a specific one.

`ITypeFormatter` is deliberately `internal` for v1.0; publishing it would freeze the traversal protocol.

### Versioned envelope

`BinarySerializer` builds one pipeline per supported version. Writing uses `options.WriteVersion` (default `BinaryFormatConstants.LatestVersion` = 1). Reading a V1 payload is *self-describing*: `FormatRouter` peeks magic `0x52455342` + version and dispatches; a stream without the magic is read as V0 only when `AllowV0Fallback` is set, and is otherwise rejected rather than guessed at. **Reads therefore require a seekable stream.**

V0 and V1 are peers with different jobs, not a current format and a deprecated one — see `internal/System-Contract.md` §10.

- **V1** (`V1FormatPipeline`) — full envelope: `BinaryFormatHeaderV1` followed by the payload. Write order is serialize → checksum over the raw payload → compress → build AAD → encrypt → header. Read reverses it, verifies every declared length, and requires the payload to be consumed exactly. Only V1 supports reference framing. The header is bound to authenticated encryption as associated data, so no header field can be altered without breaking the tag.
- **V0** (`V0FormatPipeline`) — the compact codec: a bare payload with no header at all, for transports that already supply their own context (private or tightly coordinated channels, IPC, protocols with their own framing). Having no header it has no reference framing and no compression/checksum/encryption phase — configured algorithms are simply not applied on a V0 write. Everything the payload itself expresses is unchanged: the full §23 type set, unions, **keyed contracts**, limits, budgets and metering, and for one value under one layout the payload bytes are identical to V1's. Because V0 writes straight through instead of buffering, a keyed write needs a seekable destination, otherwise `NotSupportedException`. A V0 payload is unauthenticated by construction, so `Build()` refuses `RequireEncryption`/`RequireChecksum` together with `WithVersion(0)` or `AllowV0Fallback` — both directions, so no operation-time check is needed. It may be embedded in a larger stream, so it does not require the source to end with the payload, and nothing in it identifies it, which is why reading one takes an explicit `AllowV0Fallback`.

Adding a format version means a pipeline registered in `BinarySerializer`; the router picks it up. Formatters, the engine, the algorithms and the limits are untouched.

### Member layouts

`TypeContract` (`Engine/`) is the single materialized description of a concrete type, used identically by reader and writer:

- **Positional** (default) — members ordered by `[BinaryOrder]` then ordinal name. Public read/write properties and public non-readonly fields are included; non-public ones need `[BinaryInclude]`; `[BinaryIgnore]` excludes. Compiler-generated fields and indexers are skipped. A delegate-typed member is **rejected** — it carries behaviour, not data — so it must be marked `[BinaryIgnore]`. Field order *is* the wire format.
- **Keyed** (`[BinaryContract]` plus `[BinaryKey(n)]` on every eligible member) — each field is written as `key, int32 length, payload`, sorted by key. Unknown keys are length-skipped, which is what makes schema evolution tolerant. Payload-level, so it works under both format versions; the length is patched after the field is written, so it requires a seekable payload stream — invisible under V1, which buffers, but under V0 the caller's destination must seek.

The two are mutually exclusive, and every contradiction is rejected when the contract is built: `[BinaryKey]` without `[BinaryContract]`, `[BinaryOrder]`/`[BinaryInclude]` on a contract, an unmarked contract member, `[BinaryKey]` together with `[BinaryIgnore]`, `[BinaryInclude]` together with `[BinaryIgnore]`, duplicate keys or orders.

Polymorphism: `[BinaryUnion(tag, typeof(Derived))]` on a base class or interface; a one-byte discriminator precedes the members. Tags must fit in a byte, and only tags travel — never type names. Writing a value whose runtime type differs from the declared type **without** a union map is `BinaryTypeException`, because the reader could not reconstruct it.

References: with `PreserveReferences`, a marker byte and object id precede every structural reference-typed value, containers included. Ids are unique but visible only along the ancestor chain, so a back reference never crosses two sibling keyed fields and skipping an unknown field can never dangle. Without the option a cycle throws `BinaryTypeException`. Member-encoded types are constructed through a parameterless constructor.

### Limits and budgets

`SerializationLimits` is the public, immutable policy, validated once when options are built. `SerializationBudget` is the per-operation accounting (elements, graph nodes, keyed fields, depth); `PhaseBudget` is the per-phase size policy. `MeteredReadStream`/`MeteredWriteStream` count bytes relative to where the operation started; `WindowReadStream` exposes one declared subrange.

Limit breaches throw `BinaryLimitException`; malformed data throws `BinaryFormatException`; unsupported versions or algorithms throw `BinaryFormatNotSupportedException`; tampering and protection downgrades throw `BinaryIntegrityException`.

### Algorithms

Each family has a public primitive (`I*Algorithm`, span-based, policy-free) and an internal service (`CompressionService`, `ChecksumService`, `EncryptionService`) that the pipeline calls **inside** the phase barrier. Custom algorithms are registered on the options builder and snapshotted into an `AlgorithmCatalog`; there is no process-wide registry, and built-ins cannot be substituted.

Key material is a `SecretKey` (always an owned copy) obtained from an `IKeyProvider`. The serializer never zeroes memory it does not own.

## Conventions

- Tests are xUnit, `[Fact]`-based, named `Method_Scenario_Expectation`, organised by concern into the `internal/QA-Plan.md` §2 folders listed above, with shared types in `Fixtures/`.
- Work happens on `feature/*` / `bugfix/*` branches merged into `main` via PR.
- Any change to what goes on the wire (formatter encoding, header fields, member ordering, reference framing) is a compatibility break unless it goes behind a new format version or a keyed contract.
- Exception constructors keep the inner exception on the same line as the message, never on its own line.
- Do not add `catch (BinarySerializerException) { throw; }` unless the catch performs real cleanup.
- Comments describe what the code does, for the NuGet consumer reading XML docs on hover or the next
  engineer reading the file. Nothing in `src/` or `tests/` addresses the reader personally or records
  history — no "note:", no "before the fix", no audit or refactoring narrative. Findings and the
  reasoning behind a decision belong in `internal/`.
- The repository owner makes every commit. Never commit, push, or open a PR. Leave finished work in
  the working tree and report the changed paths.
- Whenever the work reaches a natural commit point — a QA-plan section or stage closed, a defect
  fixed, a session wrapped up — end the report with a ready commit message in English. Keep it
  terse, as the history is: one subject line, optionally a short clause after a dash naming the
  consequence. No body, no bullet list, no trailer. The detail belongs in the report and in `internal/`.
