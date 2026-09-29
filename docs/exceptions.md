# Exceptions

Every failure that belongs to Viper's domain — the data, the configuration, the types or the
destination — is a `BinarySerializerException`. Each subtype answers a different question, so a handler
can decide what to do without parsing a message. The exception classes are in `ViShap.Viper.Exceptions`.

```text
BinarySerializerException
├── BinaryConfigurationException
├── BinaryFormatException
│   └── BinaryLimitException
├── BinaryFormatNotSupportedException
├── BinaryIntegrityException
├── BinaryEncryptionException
│   └── BinaryEncryptionKeyException
├── BinaryStreamException
└── BinaryTypeException
```

| Exception | The question it answers | Raised for |
|---|---|---|
| `BinaryConfigurationException` | is the setup wrong? | invalid limits or option combinations at `Build()`; a fixed key of the wrong length; an algorithm that breaks its own stated behaviour; a registered algorithm factory that throws or returns `null` |
| `BinaryFormatException` | are the bytes malformed? | truncated or structurally invalid input; a malformed number, header or keyed structure; an empty input; trailing bytes after a frame; a duplicate key or element; invalid UTF-8; an unidentified frame when `AllowV0Fallback` is off |
| `BinaryLimitException` | are the bytes well formed but too large? | any configured limit: depth, a length or count, the cumulative budgets, a phase size, the wire size |
| `BinaryFormatNotSupportedException` | is this recognized but not available here? | an unsupported format version; an unknown algorithm id; a custom algorithm nobody registered; a header record that must be understood and is not; a cipher the platform lacks |
| `BinaryIntegrityException` | can the data be trusted? | a checksum mismatch; a failed authentication tag — wrong key, modified payload or modified header; a frame that does not satisfy `RequireEncryption` or `RequireChecksum` |
| `BinaryEncryptionException` | did the encryption itself fail? | an operational failure that is neither a format nor an integrity nor a key problem |
| `BinaryEncryptionKeyException` | is the key missing, wrong, or unusable? | no key; a resolver that returns nothing; a key id that does not match; a key of the wrong length; a key provider that cannot supply material |
| `BinaryStreamException` | did the stream or pipe fail? | an `IOException` from a stream, from `PipeReader.ReadAsync` or from `PipeWriter.FlushAsync`, with the original as `InnerException`; a buffer writer that hands out an empty span |
| `BinaryTypeException` | can the type be handled? | an invalid contract or union declaration; a delegate; a value whose runtime type needs a union declaration and has none; a type that cannot be constructed; a cycle without `PreserveReferences`; `Populate` on a type that has a dedicated encoding |

`BinaryLimitException` derives from `BinaryFormatException`, and `BinaryEncryptionKeyException` from
`BinaryEncryptionException`, so catch the more specific type first.

Where a lower-level failure is part of the diagnosis it is kept as `InnerException`: an `IOException`
under `BinaryStreamException`, a `CryptographicException` under `BinaryIntegrityException` or
`BinaryEncryptionException`, an `InvalidDataException` from a compressor under `BinaryFormatException`,
and whatever a registered algorithm factory threw under `BinaryConfigurationException`.

## A handler

```csharp
using ViShap.Viper;
using ViShap.Viper.Exceptions;

string Classify(Action action)
{
    try
    {
        action();
        return "ok";
    }
    catch (BinaryLimitException) { return "limit: the input is larger than this reader accepts"; }
    catch (BinaryFormatException) { return "format: the input is malformed"; }
    catch (BinaryIntegrityException) { return "integrity: the input cannot be trusted"; }
    catch (BinaryEncryptionKeyException) { return "key: no usable key"; }
    catch (BinaryFormatNotSupportedException) { return "not supported here"; }
    catch (BinaryTypeException) { return "type: this type cannot be serialized"; }
    catch (BinaryConfigurationException) { return "configuration: the setup is wrong"; }
    catch (BinarySerializerException e) { return "other: " + e.GetType().Name; }
}

var serializer = new BinarySerializer();
byte[] frame = serializer.Serialize("hello");

Console.WriteLine(Classify(() => serializer.Deserialize<string>(frame)));
Console.WriteLine(Classify(() => serializer.Deserialize<string>(frame[..^1])));
Console.WriteLine(Classify(() => serializer.Deserialize<string>(ReadOnlySpan<byte>.Empty)));
Console.WriteLine(Classify(() => serializer.Serialize<Action>(() => { })));
```

## Which side is at fault

| Exception | Usually means |
|---|---|
| `BinaryFormatException`, `BinaryLimitException` | the bytes: corrupt, hostile, or produced by something else |
| `BinaryIntegrityException` | tampering, corruption, a wrong key, or a downgrade the policy refuses |
| `BinaryEncryptionKeyException`, `BinaryFormatNotSupportedException` | the reader is missing a key, an algorithm registration or a platform capability the frame needs |
| `BinaryConfigurationException`, `BinaryTypeException` | the program: a defect a developer fixes, and the same on every call |
| `BinaryStreamException` | the environment: the stream or connection failed |

## Failures that keep their .NET type

These stay outside `BinarySerializerException` on purpose:

- `ArgumentNullException` — a required argument is `null`. A `null` array read is the exception: it is an
  empty span, and an empty input is a `BinaryFormatException`.
- `ArgumentException` — an invalid argument passed directly by the caller.
- `NotSupportedException` — an unsupported capability: a V0 payload read from a stream that cannot seek,
  and any asynchronous read that meets V0.
- `ObjectDisposedException` — a `PooledPayload`, `SecretKey` or key provider used after it was disposed.
- `OperationCanceledException` — a cancelled asynchronous call.

Nothing that comes out of a payload leaves under a framework name. If a container refuses a value from
the payload with an `ArgumentException`, it is reported as a `BinaryFormatException` with the original as
its inner exception, because that refusal is a statement about the payload and not about your arguments.
