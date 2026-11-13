# IDEA-14 — Target frameworks, platforms and Unity

**Class: plan.** **Status:** idea; option b (`net8.0`/`net9.0`) rejected by the owner on 2026-11-14 — see
"Option b measured" below.

## What and why

The runtime packages target `net10.0` alone; the generator targets `netstandard2.0`, as every Roslyn
component does. nuget.org lists `net10.0-android`, `-browser`, `-ios`, `-maccatalyst`, `-macos`, `-tvos`
and `-windows` as compatible, which says only that NuGet will install the package there: CI builds and
tests on Windows and Linux x64 and publishes native AOT on Linux, and no other platform has been run. An
application on .NET 8 or 9, on .NET Framework, or in Unity cannot reference the packages at all.

## Where it was written

- The owner's review of the nuget.org page of `v1.0.0-rc.2`, 2026-10-26.
- An outside assessment of `v1.0.0`, reviewed by the owner on 2026-11-13, which asked for `net8.0`
  beside `net10.0` "if the architecture allows it without loss"; measured on 2026-11-14 (below).

## What is already fixed

- A new target framework is a **minor** (`Development-Workflow.md` §5.3); dropping one is a **major**
  (§5.2). Shipping `v1.0.0` on `net10.0` alone closes no door.
- .NET 10 is the LTS supported until November 2028; .NET 8 (LTS) and .NET 9 (STS) left support on
  2026-11-10 and receive no security fix after it.
- `ChaCha20Poly1305Encryption` on a platform without it is `BinaryFormatNotSupportedException` at `Build()`
  and on read (contract §4.1, §8.4). `Aes256GcmEncryption` has no such rule: where the platform lacks AES-GCM
  (`AesGcm.IsSupported` is `false` in the browser) the failure it raises has not been pinned.
- The runtime code uses APIs that exist only on modern .NET: `Int128`/`UInt128`, `Half`, `Rune`,
  `DateOnly`/`TimeOnly`, frozen collections, `[FeatureSwitchDefinition]` and `[FeatureGuard]`, which
  `docs/supported-types.md` and the AOT design rely on.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | `net10.0` alone, and the README and `docs/getting-started.md` say which platforms CI runs | An honest statement of what is verified | Nothing new for older runtimes |
| b | a, plus `net8.0`/`net9.0` | Applications not yet on .NET 10 | Both leave support within weeks of `v1.0.0`; conditional code around .NET 9/10 APIs; CI per target. Not recommended |
| c | a, plus `netstandard2.1` for Unity (Mono and IL2CPP) | Unity projects, through the generator — IL2CPP is ahead-of-time, so the context is the only path | The types of §23 missing there leave the supported set, so one payload is not readable everywhere; C# 9 in Unity; the reflection path cut or rewritten; a second build of the engine to keep byte-identical; Unity's own test matrix |
| d | Wait for Unity's move to CoreCLR, then add the modern target Unity runs | Unity on the same build as everyone else | Depends on Unity's schedule |
| e | Verify the platforms nuget.org lists (browser, Android, iOS): a CI job per platform; `Aes256GcmEncryption` held to the rule of ChaCha20-Poly1305 | The compatible list becomes a tested list | A device or emulator matrix; a contract change for AES-GCM availability |

Recommended when taken up: **a** now — it changes only documents; **e** for AES-GCM before any mobile
or browser claim; **d** over **c** unless a Unity consumer asks.

## Option b measured — `net8.0`, 2026-11-14

A copy of `src/` was built for `net8.0` with SDK 10.0.404, outside the repository, to replace the
estimate with a list. Nothing in the repository was changed by it.

### The library compiles after five small changes

The runtime packages fail with 28 errors, all of them from five causes:

| Cause | Why on `net8.0` | Change |
|---|---|---|
| `PipeReader`, `PipeWriter`, `ReadResult` | in-box on `net10.0`, a package on `net8.0` | `System.IO.Pipelines` as a dependency of the `net8.0` build |
| `System.Threading.Lock` in `BinarySerializerContext` | a .NET 9 type; the `lock` statement needs its `EnterScope` | `object` under `#if` |
| `[FeatureSwitchDefinition]`, `[FeatureGuard]` in `Engine/ReflectionPath` | .NET 9 attributes | internal copies under `#if` — which compile, but see the native AOT obstacle |
| `Array.CreateInstanceFromArrayType` in the multi-dimensional array formatter | a .NET 9 method | `Array.CreateInstance(elementType, lengths)` |
| `WriteElements(Elements(array))` infers `ReadOnlySpan<T>` from a `Span<T>` | first-class spans, C# 14; `net8.0` defaults to C# 12 | an explicit conversion |

With those, the whole of `ViShap.Viper.Core` and `ViShap.Viper.Serialization` compiles. About a day.

### What costs more than the library

1. **The generator writes code that fails at run time on .NET 8.** For a non-public member of a generic
   type it emits `[UnsafeAccessor]` inside a generic class (`Emitter.Accessors`). Generic support in
   `UnsafeAccessor` arrived in .NET 9: on .NET 8 such a contract compiles and fails when first used. The
   generator would have to read the consumer's runtime version from the compilation and either refuse the
   member with a diagnostic or reach it another way — a new rule that `ReflectedContract`, the conformance
   suites and `ByteIdentityTests` must then agree with.
2. **Native AOT loses its guarantee.** The reflection path is removed because `[FeatureSwitchDefinition]`
   and `[FeatureGuard]` tell the trimmer and the AOT compiler that it is unreachable when the switch is
   off. The .NET 8 AOT compiler predates them: a `net8.0` consumer publishing with native AOT would compile
   the reflection path and get IL2026/IL3050 warnings from the library. The rule that nothing in `src/`
   suppresses a trimming or AOT warning rules out silencing them; a second mechanism — an embedded
   `ILLink.Substitutions.xml` per switch — could stand in for the attributes on `net8.0`, and would have
   to be kept equal to them, with its own native AOT job in CI.
3. **Every test twice.** The 2 293 tests, the generator's tests, `DocumentationExampleTests` (every
   example of `docs/` and the READMEs compiled and run) and the native AOT job run per target framework.
   Differences are expected and must be triaged one by one: since .NET 9 Deflate runs on zlib-ng, so a
   compressed frame may carry other bytes on `net8.0` than on `net10.0` — both valid, as the format only
   requires that it decompresses, but a test comparing compressed bytes fails on one of them.
4. **The type set is the same on both**, which is the one thing that is cheap: `Int128`, `Half`, `Rune`,
   `DateOnly`, `TimeOnly`, frozen collections and `UnsafeAccessor` for non-generic types are all in .NET 8.

### Why it is not done

- **.NET 8 is out of support since 2026-11-10.** It receives no security fix. Viper's purpose is reading
  bytes it does not trust; to support it officially on a runtime whose own parsers, cryptography and
  compression no longer get security fixes contradicts that purpose, and every report of a runtime
  vulnerability would reach Viper's users on `net8.0` with no fix to point them to.
- **Adding a target is a minor, removing it is a major** (`Development-Workflow.md` §5.2, §5.3). A
  `net8.0` build added in `v1.1.0` would have to stay until `v2.0.0`: years of a second build, a second
  test run, a second AOT mechanism and a generator rule for a runtime that was already dead when it was
  added.
- **The audience moves anyway.** Teams still on .NET 8 are migrating precisely because it is no longer
  patched; .NET 10 is the LTS until November 2028. A library that starts on .NET 10 meets them where
  they are going.
- **.NET 9 is not an alternative.** It left support on the same day.
- **It does not reach Unity.** Unity runs Mono or IL2CPP on the .NET Standard 2.1 profile with C# 9 —
  option c, a separate build of the engine without `Int128`, `Half`, Brotli, AES-GCM and the other
  types and APIs that profile lacks, through the context only, because IL2CPP compiles ahead of time.
  A `net8.0` target changes none of that. Unity's announced move to CoreCLR (option d) would make a
  modern target the Unity path; which runtime version Unity ships it on is to be checked against Unity's
  roadmap when this idea is taken up, and only then does a target other than `net10.0` buy Unity.

### If it is ever reopened

The cost above is the list to plan against, in this order: the five library changes; the generator's
rule for generic types on .NET 8, with the conformance runs agreeing; the substitutions file and a
`net8.0` native AOT job; the test matrix and the triage of what differs. The trigger would be a concrete
consumer who cannot move to .NET 10 — not the size of the .NET 8 market in general.

## Open questions

1. Is Unity a target audience of Viper, and which Unity version and scripting backend?
2. Which of the listed platforms should the README claim, and which stay "compatible, not verified"?
3. For e: which exception `Aes256GcmEncryption` raises where the platform lacks AES-GCM, and whether
   `Build()` refuses it as it refuses ChaCha20-Poly1305.

## Version impact

| Option | Surface | Version |
|---|---|---|
| a | documents | **patch** |
| b, c, d | a new target framework | **minor**; the wire does not change, but a type missing on the new target is not supported there |
| e | behaviour: the AES-GCM failure brought into the exception hierarchy of contract §8 | **patch** if contract §8 already requires it, **minor** with a contract change otherwise |

## Preconditions

- For c: the list of §23 types and APIs that `netstandard2.1` lacks, and the generator checked under the
  Roslyn version Unity ships.
- For e: a runner or emulator for each platform claimed.
