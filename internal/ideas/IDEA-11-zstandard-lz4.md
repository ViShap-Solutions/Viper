# IDEA-11 — Zstandard and LZ4

**Class: plan.** **Status:** idea, partly decided. Taken into `v1.0.0-rc.2` and then deferred by the owner
on 2026-10-09 until an implementation is available officially — Zstandard in the BCL, LZ4 from a
source the owner accepts (`rc2/RC2-Plan.md` §3.9, A1).

## What and why

Viper compresses with Deflate (RFC 1951) and Brotli (RFC 7932). Zstandard (RFC 8878) and LZ4 are the
formats most systems choose when compression must cost little CPU; LZ4 trades ratio for speed further
still. Consumers moving data between services that already use them expect them.

## Where it was written

- `rework/Decisions.md` D9.2, D9.12; `rework/Rework-Plan.md` §13; `Audit-Future.md` §2.
- `rc2/RC2-Plan.md` §3 A1, stages P5c and P5d (deferred).

## What is already fixed — the owner's decisions of 2026-10-09

- **Built-in, not `Custom`.** `Custom = 255` with a name in the header is for the consumer's own
  algorithms. The library's algorithms take enum values of their own, like the others:
  `CompressionAlgorithm.Zstandard = 3`, `CompressionAlgorithm.Lz4 = 4`.
- **In `ViShap.Viper.Serialization`, not separate packages.** Public classes `ZstandardCompression` and
  `Lz4Compression` implementing `ICompressionAlgorithm`, resolved by a case of
  `AlgorithmCatalog.ResolveCompression` beside Deflate and Brotli. No new package, no change to CD.
- **No native dependency** in the shipped packages (`Rework-Plan.md` §15). A native library that is part
  of the .NET runtime itself (as Brotli is) counts as the platform, not as a dependency.
- **Deferred until official.** The owner does not take a third-party dependency for them; they come when
  an official implementation exists.

The two values `3` and `4` are therefore **reserved**: nothing else takes them, and they are not added to
the enum before an implementation ships (an enum value with no implementation is a trap — A1 option c).

## Rules any implementation keeps

- `ICompressionAlgorithm` exactly: decompression produces exactly `expectedLength` bytes, compression
  writes into the `IBufferWriter<byte>` the service supplies, bounded by `MaxCompressedBytes`; the
  service holds it to that, a breach is `BinaryConfigurationException`.
- Decompression reads hostile input: it is bounded by the declared length and by `MaxDecompressionRatio`
  (contract §5.10, §12), and every failure of the library is mapped to `BinaryFormatException` — nothing
  leaves under a framework name (INV-10).
- Wire: a new algorithm id in the compression record, written only when chosen; every existing frame and
  fixture unchanged. New frozen fixtures are **added**, one per id, and pin **decompression** of fixed
  bytes — not byte-for-byte compressor output, so the implementation may be replaced later without a
  compatibility break.
- Documents with the code: contract §3, §12, §22; QA checkpoints; `docs/algorithms-and-keys.md`; the
  Serialization README; `PublicSurfaceTests` and `MemberSurfaceTests`; `AotAnalysisTests` reports nothing
  new.

## Options — where the implementation comes from

| | Option | Gives | Costs | Status |
|---|---|---|---|---|
| a | Managed dependencies in `Serialization`: `ZstdSharp.Port` (C# port of zstd, MIT) and `K4os.Compression.LZ4` (MIT) | Both on net10 today, for every consumer, exactly like Brotli | Two third-party dependencies for every consumer; licence, maintenance, no native code and AOT cleanliness to verify | **rejected by the owner** (no third-party dependency) |
| b | Zstandard from the BCL: `System.IO.Compression.ZstandardStream`, present in .NET 11 previews. Multi-target `net10.0;net11.0`; on net10 `Zstandard` is `BinaryFormatNotSupportedException` at `Build()` and on read, as `ChaCha20Poly1305` is where the platform lacks it | No dependency; platform-maintained | Not on net10; ties the feature to the .NET 11 release; LZ4 still has no BCL source | **the path for Zstandard**, once the API ships in a stable release |
| c | Own implementation | No dependency | A large decompressor over hostile input written here, with no external review | not recommended |
| d | Enum values in Core, implementations in optional packages plugged in through the builder | Core without dependencies | A new extension point for built-in ids; contradicts "like the others" and INV-7 | not recommended |

For **LZ4** no official source is in sight: when the owner revisits it, the choice is between a, c and
dropping it.

## Format sub-decisions (recommended, to confirm when the idea is taken up)

| Question | Options | Recommended | Why |
|---|---|---|---|
| LZ4 container | block format · frame format | **block** | The V1 header already declares the length and carries the checksum; the frame format duplicates both |
| Zstandard frame checksum | on · off | **off** | The same: the checksum service covers the raw payload |
| Zstandard content size in the frame | written · not written | **written** (zstd default) | Harmless; the declared length is still the bound |
| Compression level | fixed · mapped from `System.IO.Compression.CompressionLevel` as Brotli does | **mapped**, as Brotli | One knob for every algorithm |

## Open questions

1. When .NET 11 is stable: multi-target, or wait until Viper's minimum target is net11?
2. LZ4: is a third-party managed library acceptable for it alone, or is it dropped?

## Version impact

| Surface | Version |
|---|---|
| API: a new class and enum value each; wire: a new algorithm id written only when chosen | **minor** (`Development-Workflow.md` §5.3; §5.5 "new built-in algorithm") |
| Adding `net11.0` as a target framework | **minor** (§5.3 "a new target framework") |
| Replacing the implementation later with the same format | **patch** — decompression of existing frames unchanged |
| Making either the default compression | **major** (§5.5 "a new default algorithm") |

## Preconditions

- `ZstandardStream` (or a span-based equivalent) in a stable .NET release.
- For LZ4: the owner's choice of source.
