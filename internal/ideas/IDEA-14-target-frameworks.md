# IDEA-14 — Target frameworks, platforms and Unity

**Class: plan.** **Status:** idea.

## What and why

The runtime packages target `net10.0` alone; the generator targets `netstandard2.0`, as every Roslyn
component does. nuget.org lists `net10.0-android`, `-browser`, `-ios`, `-maccatalyst`, `-macos`, `-tvos`
and `-windows` as compatible, which says only that NuGet will install the package there: CI builds and
tests on Windows and Linux x64 and publishes native AOT on Linux, and no other platform has been run. An
application on .NET 8 or 9, on .NET Framework, or in Unity cannot reference the packages at all.

## Where it was written

- The owner's review of the nuget.org page of `v1.0.0-rc.2`, 2026-10-26.

## What is already fixed

- A new target framework is a **minor** (`Development-Workflow.md` §5.3); dropping one is a **major**
  (§5.2). Shipping `v1.0.0` on `net10.0` alone closes no door.
- .NET 10 is the LTS supported until November 2028; .NET 8 (LTS) and .NET 9 (STS) leave support in
  November 2026.
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
