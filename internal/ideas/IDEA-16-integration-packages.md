# IDEA-16 — Integration packages

**Class: plan.** **Status:** idea.

## What and why

Viper is reached through `BinarySerializer` alone. An application that wants it on an HTTP endpoint, in
a distributed cache or on a real-time hub writes the adapter itself, each time with the same choices: a
media type, where the options and the context come from, how a `BinarySerializerException` becomes a
response. Small packages around the serializer would make those choices once.

## Where it was written

- The owner's review of an outside assessment of `v1.0.0`, 2026-10-27.

## What is already fixed

- The engine is complete for these uses: `IBufferWriter<byte>`, `ReadOnlySequence<byte>`, `Stream`,
  `PipeReader`/`PipeWriter`, asynchronous reads and writes at the frame edge, type information for
  native AOT.
- `cd.yml` packs exactly four packages from one tag and checks the count; a new package changes it.
- An integration package adds no format and no behaviour to the engine; it may depend on the framework it
  adapts, which the engine packages must not.

## Options

| | Package | Gives | Costs |
|---|---|---|---|
| a | `ViShap.Viper.AspNetCore` — input and output formatters for MVC and a `Results` helper for minimal APIs, one media type | Viper frames as request and response bodies | A media type to choose and register; mapping of the exception families to status codes; ASP.NET Core versions to follow |
| b | `ViShap.Viper.Caching` — an `IHybridCacheSerializer<T>` and an `IDistributedCache` extension | Encrypted, bounded cache entries in one call | Depends on `Microsoft.Extensions.Caching.*` |
| c | `ViShap.Viper.SignalR` — an `IHubProtocol` | Hub messages as Viper frames | The hub protocol frames messages itself, so V0 inside it; invocation messages carry arguments of types known only at run time, which the context has to list |
| d | None; `docs/` shows each adapter as a complete example | No package to maintain | Each consumer copies and owns the adapter |

Recommended when taken up: **d** first for every adapter — it costs a page and proves the shape — then a
package for the one consumers ask for, **a** or **b** most likely.

## Open questions

1. Do integration packages ship on the libraries' tag (`cd.yml` packs every package from one tag) or on
   a version line of their own?
2. The media type: a vendor type (`application/vnd.vishap.viper`) or none registered?
3. For a: how a `BinaryLimitException` or `BinaryIntegrityException` maps to a status code, and whether
   the message reaches the client.

## Version impact

| Surface | Version |
|---|---|
| A new package; the libraries unchanged | **minor** for the repository's cycle, or a version line of its own |
| A documentation page with an adapter (d) | **patch** |

## Preconditions

- The decision how a package that is not one of the four is versioned and released (shared with
  `IDEA-06`).
