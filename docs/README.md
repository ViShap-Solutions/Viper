# Viper documentation

Viper is a binary serializer for .NET 10. It writes objects as self-describing frames, reads frames
from memory, streams and pipes, and can compress, checksum and encrypt a frame. It is built to read
bytes it does not trust: every declared length, count and nesting level is bounded before anything is
allocated for it, and every failure is an exception from one hierarchy.

```bash
dotnet add package ViShap.Viper
```

## Pages

| Page | What it covers |
|---|---|
| [Getting started](getting-started.md) | installing the packages, a first round trip, one serializer per configuration |
| [Entry points](entry-points.md) | writing to and reading from buffers, streams and pipes; asynchronous use; `Populate`; streams of frames |
| [Options and limits](options-and-limits.md) | the options builder, its validation, and every `SerializationLimits` value |
| [Formats: V1 and V0](formats.md) | the self-describing frame and the headerless codec, and how to choose between them |
| [Contracts and schema evolution](contracts.md) | positional and keyed layouts, member attributes, evolving a type, supplying a contract |
| [The source generator](generator.md) | generated type contracts, the context, diagnostics VPR001–VPR019 |
| [References](references.md) | preserving shared and cyclic references, and what happens without it |
| [Polymorphism](polymorphism.md) | `[BinaryUnion]`: serializing a base type and its derived types |
| [Algorithms and keys](algorithms-and-keys.md) | compression, checksums, encryption, key providers, custom algorithms |
| [Supported types](supported-types.md) | every type the serializer handles and how it comes back |
| [Exceptions](exceptions.md) | the exception hierarchy and when each exception is raised |
| [Diagnostics](diagnostics.md) | inspecting a header, dumping a frame, comparing two frames |
| [Trimming and native AOT](aot.md) | what the reflection path asks of an application built with trimming or AOT |

## The packages

| Package | Contains |
|---|---|
| [`ViShap.Viper`](../src/ViShap.Viper/METAPACK-README.md) | the meta-package: references the three below |
| [`ViShap.Viper.Core`](../src/ViShap.Viper.Core/CORE-README.md) | attributes, algorithm and key contracts, exceptions |
| [`ViShap.Viper.Serialization`](../src/ViShap.Viper.Serialization/SERIALIZATION-README.md) | the serializer, built-in algorithms and key providers, diagnostics |
| [`ViShap.Viper.Generator`](../src/ViShap.Viper.Generator/GENERATOR-README.md) | the build-time source generator of type contracts; nothing of it ships with your application |

The public API is documented in XML and ships beside the assemblies, so every type and member is
described on hover in the editor.

## Versioning

Viper follows Semantic Versioning for three things: the public API, the bytes on the wire, and the
behaviour the documentation describes — which inputs are accepted, which exception a failure raises,
the default limits. A payload written by a 1.x release is read by every later 1.x release. A new
capability is added as a new header record; an older reader refuses only a frame that uses it, with a
`BinaryFormatNotSupportedException`, and reads every other frame as before.
