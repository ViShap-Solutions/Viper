using System.Buffers;
using System.IO.Pipelines;
using ViShap.Viper;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Exceptions;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;

// A consumer of the three packages, built with native AOT analysis by Api/AotAnalysisTests (EXT-07).
// A line that ends in a list of warning codes is expected to report exactly those codes; no other
// line, and no line of the packages themselves, may report a trimming or AOT warning.

byte[] key = new byte[32];

var options = BinarySerializerOptions.Configure()
    .WithCompression(new BrotliCompression())
    .WithChecksum(new XxHash3Checksum())
    .WithEncryption(new Aes256GcmEncryption(), key, "k1")
    .PreserveReferences()
    .WithLimits(new SerializationLimits { MaxDepth = 64 })
    .RequireEncryption()
    .Build();

var reader = BinarySerializerOptions.Configure()
    .WithKeys(new HkdfKeyProvider(key))
    .Build();

var headerless = BinarySerializerOptions.Configure()
    .WithVersion(0)
    .AllowV0Fallback()
    .WithChecksum(new Crc32Checksum())
    .WithCompression(new DeflateCompression())
    .Build();

using var provider = new StaticKeyProvider(key, "k1");
_ = new ChaCha20Poly1305Encryption();
_ = new XxHash128Checksum();
_ = BinarySerializerOptions.Configure().WithKeys(provider).WithKeys(id => key).Build();

var serializer = new BinarySerializer(options);
var order = new Order { Id = 7, Lines = ["a", "b"] };

var buffer = new ArrayBufferWriter<byte>();
serializer.Serialize(buffer, order); // IL2026 IL3050
byte[] frame = serializer.Serialize(order); // IL2026 IL3050
using (var pooled = serializer.SerializePooled(order)) // IL2026 IL3050
    _ = pooled.Span.Length;
serializer.Serialize(new MemoryStream(), order); // IL2026 IL3050
await serializer.SerializeAsync(new MemoryStream(), order); // IL2026 IL3050
await serializer.SerializeAsync(new Pipe().Writer, order); // IL2026 IL3050

_ = serializer.Deserialize<Order>(frame); // IL2026 IL3050
_ = serializer.Deserialize<Order>(frame, out int bytesConsumed); // IL2026 IL3050
_ = serializer.Deserialize<Order>(new ReadOnlySequence<byte>(frame)); // IL2026 IL3050
_ = serializer.Deserialize<Order>(new ReadOnlySequence<byte>(frame), out SequencePosition consumed); // IL2026 IL3050
_ = serializer.Deserialize<Order>(new MemoryStream(frame)); // IL2026 IL3050
_ = await serializer.DeserializeAsync<Order>(new MemoryStream(frame)); // IL2026 IL3050
_ = await serializer.DeserializeAsync<Order>(PipeReader.Create(new MemoryStream(frame))); // IL2026 IL3050
await foreach (var each in serializer.DeserializeAsyncEnumerable<Order>(new MemoryStream(frame))) // IL2026 IL3050
    _ = each;
await foreach (var each in serializer.DeserializeAsyncEnumerable<Order>(PipeReader.Create(new MemoryStream(frame)))) // IL2026 IL3050
    _ = each;

serializer.Populate(frame, order); // IL2026 IL3050
serializer.Populate(frame, order, out bytesConsumed); // IL2026 IL3050
serializer.Populate(new ReadOnlySequence<byte>(frame), order); // IL2026 IL3050
serializer.Populate(new ReadOnlySequence<byte>(frame), order, out consumed); // IL2026 IL3050
serializer.Populate(new MemoryStream(frame), order); // IL2026 IL3050
await serializer.PopulateAsync(new MemoryStream(frame), order); // IL2026 IL3050
await serializer.PopulateAsync(PipeReader.Create(new MemoryStream(frame)), order); // IL2026 IL3050

_ = BinaryFormatDumper.Dump<Order>(frame, options); // IL2026 IL3050
_ = BinaryFormatDumper.Dump<Order>(new ReadOnlySequence<byte>(frame), options); // IL2026 IL3050
_ = BinaryFormatDumper.DumpValue(order, options); // IL2026 IL3050
_ = BinaryFormatDumper.Compare<Order>(frame, frame, options); // IL2026 IL3050

BinaryDump dump = BinaryFormatDumper.Dump(frame, reader);
_ = dump.ToString();
_ = BinaryFormatDumper.DumpHeader(frame);
_ = BinaryFormatDumper.DumpHeader(new ReadOnlySequence<byte>(frame));
_ = BinaryFormatDumper.DumpHeader(new MemoryStream(frame));
BinaryHeaderInfo? header = BinaryFormatInspector.Peek(frame);
_ = header?.KeyId;
_ = headerless.WriteVersion;

try
{
    SerializationLimits.Default.Validate();
}
catch (BinarySerializerException failure)
{
    _ = failure.Message;
}

internal sealed class Order
{
    public int Id { get; set; }

    public List<string> Lines { get; set; } = [];
}
