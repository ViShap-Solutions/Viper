# IDEA-06 — Command-line tool and schema-driven reading

**Class: plan.** **Status:** idea. Recorded, not scheduled, in `rc2/RC2-Plan.md` §2.

## What and why

`BinaryFormatDumper` reads a frame for a person, but only from code that has the CLR type. A command-line
tool (`viper dump frame.bin`) would let an operator inspect a captured frame — header, phases, payload —
without writing a program, and with a schema, read it as a tree without the type.

## Where it was written

- `rework/Decisions.md` §9.32, "after the release".

## What is already fixed

- `DumpHeader` and `Dump` (hex payload after undoing the phases) need no type; `Dump<T>` needs `T`.
- The wire carries no type description; positional members carry no names or kinds.

## Options

| | Option | Gives | Costs |
|---|---|---|---|
| a | A `dotnet tool` over `DumpHeader` and `Dump` only | Header and phases readable at once; no new library surface | No payload structure without a type |
| b | a, plus loading `T` from an assembly path (`--type MyApp.Order --assembly MyApp.dll`) | Full tree from a build of the producing application | Loads untrusted assemblies into the tool's process; reflection |
| c | a, plus a schema format (exported by the generator from a context) that drives a typeless reader | Typeless reading | A schema language to specify and freeze; a second reader to keep identical to the engine |

Recommended when taken up: **a**, then **b**; c only with a separate design brief.

## Open questions

1. Is the tool a package of this repository and this release cycle, or its own?
2. For c: who produces the schema, and is it versioned with the wire?

## Version impact

| Surface | Version |
|---|---|
| A new tool package; no change to the libraries | **minor** for the repository's cycle, or a version line of its own |
| A schema export added to the generator | **minor** |

## Preconditions

- The decision whether a tool ships on the libraries' tag (`cd.yml` packs every package from one tag).
