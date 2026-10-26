# IDEA-13 — Text of a dump's parts: a node and a difference

**Class: plan.** **Status:** idea.

## What and why

`BinaryDump` renders itself four ways — `ToString()`, `ToJson()`, `ToXml()`, `ToHex()` — but the parts
a consumer holds on their own do not: `BinaryDumpNode` and `BinaryDumpDifference` keep the `ToString()`
of `object`, so `Console.WriteLine(BinaryFormatDumper.Compare(...))` prints
`ViShap.Viper.Diagnostics.BinaryDumpDifference`, and a node in a debugger or a log line shows its type
name. Comparing two frames is the call whose answer is most often printed, and today the consumer writes
the line themselves from `Path`, `Expected` and `Actual`.

## Where it was written

- The owner's consumer check of `v1.0.0-rc.2` from NuGet, 2026-10-26: `Compare` in a console program.

## What is already fixed

- `BinaryDump.ToString()` renders a node as one line of the tree: offset, name, type, kind or value
  (`@0011  ├─ Id   Int32   7`); values with the invariant culture, times in UTC, strings cut at 256
  characters (`docs/diagnostics.md`, Rules).
- The header has every form already: text from `DumpHeader`, a structure from `BinaryFormatInspector.Peek`
  (`BinaryHeaderInfo`, a record struct with its own `ToString()`), and JSON and XML as the `header`
  element of `Dump(frame, options).ToJson()` / `ToXml()`, which needs no type.
- `BinaryDumpDifference` holds `Path`, `Expected` and `Actual`; either node is `null` where one tree has a
  node the other lacks.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | Nothing; `docs/diagnostics.md` shows the line built from `Path`, `Expected.Value`, `Actual.Value` (it does today) | No surface added | Every consumer writes the same line; a debugger shows type names |
| b | `ToString()` on `BinaryDumpNode` (the node's tree line without the branch drawing) and on `BinaryDumpDifference` (`path: expected → actual`) | Printing and logging work as they do for `BinaryDump`; debugger display for free | Two overrides on the public surface (§3, `PublicSurfaceTests`); the text is not a contract, as for `BinaryDump` |
| c | b, plus `ToJson()` / `ToXml()` on `BinaryDumpDifference` | A difference in a CI report as data | Two more renderers to keep equal to `BinaryDump`'s; the same data is reachable from the two dumps' JSON |
| d | b, plus JSON and XML for `DumpHeader` | — | Duplicates `Peek` and the `header` element of the untyped dump; not recommended |

```text
b — BinaryDumpNode.ToString()          @0011  Id  Int32  7
b — BinaryDumpDifference.ToString()    Order.Id: 7 → 8
                                       Order.Lines[2]: (absent) → Line object · 3 members
```

Recommended when taken up: **b** — the gap is printing, not data; c only if a consumer asks for a
difference as data.

## Open questions

1. The text of a missing node in a difference: `(absent)`, `—` or `null` (the last collides with a null
   value).
2. Whether `BinaryDumpNode.ToString()` carries the offset, which makes two equal nodes of two frames
   print differently.

## Version impact

| Option | Surface | Version |
|---|---|---|
| b | API: two `ToString()` overrides; behaviour: the text of a method no contract promised | **minor** (§5.3: a new member); inside `v1.0.0` only through an `rc.3` |
| c | API: two new methods | **minor** |

## Preconditions

- None. The renderers of `BinaryDump` already produce the node's line; b reuses them.
