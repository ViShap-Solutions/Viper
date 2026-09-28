using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace ViShap.Viper.Diagnostics;

/// <summary>
/// A frame read for a person: its header, what each phase did, and — when a type was given — the
/// payload as a tree of <see cref="BinaryDumpNode"/> with the offset, the length and the value of every
/// node. When the frame does not read, the dump keeps what was read up to the failure and says where
/// the failure is. Produced by <see cref="BinaryFormatDumper"/>.
/// </summary>
/// <remarks>
/// A dump of an encrypted frame read with its key shows the plaintext: <see cref="ToString"/>,
/// <see cref="ToJson"/> and <see cref="ToHex"/> render the decrypted values and bytes. Key material is
/// never part of a dump.
/// </remarks>
public sealed class BinaryDump
{
    private static readonly NumberFormatInfo Grouped = new() { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };

    private readonly byte[] _payload;
    private readonly bool _payloadIsStored;
    private readonly string? _typeName;
    private readonly int? _compressedLength;

    internal BinaryDump(
        int formatVersion,
        BinaryHeaderInfo? header,
        int frameLength,
        byte[] payload,
        bool payloadIsStored,
        int? compressedLength,
        bool? checksumVerified,
        bool? decrypted,
        string? typeName,
        DumpTrace? trace,
        BinarySerializerException? failure)
    {
        FormatVersion = formatVersion;
        Header = header;
        HeaderLength = header?.HeaderLength ?? 0;
        FrameLength = frameLength;
        _payload = payload;
        _payloadIsStored = payloadIsStored;
        PayloadLength = payloadIsStored ? 0 : payload.Length;
        _compressedLength = compressedLength;
        ChecksumVerified = checksumVerified;
        Decrypted = decrypted;
        _typeName = typeName;
        Root = trace?.Root;
        NodeCount = trace?.NodeCount ?? 0;
        MaxDepth = trace?.MaxDepth ?? 0;
        Failure = failure;

        if (failure is not null && trace?.Deepest is { } deepest)
        {
            FailureOffset = deepest.Offset;
            FailurePath = trace.DeepestPath;
        }

        trace?.Abandon();
    }

    /// <summary>The wire format version: 1 for a frame with a header, 0 for bytes without one.</summary>
    public int FormatVersion { get; }

    /// <summary>The header, or <see langword="null"/> for version 0 bytes and for a header that could not be read.</summary>
    public BinaryHeaderInfo? Header { get; }

    /// <summary>The bytes the header occupies; 0 without a header.</summary>
    public int HeaderLength { get; }

    /// <summary>The length of the payload after decryption and decompression; 0 when those phases were not undone.</summary>
    public int PayloadLength { get; }

    /// <summary>
    /// Whether the checksum the header records matches the payload; <see langword="null"/> when the
    /// frame carries no checksum or the read stopped before the checksum could be verified.
    /// </summary>
    public bool? ChecksumVerified { get; }

    /// <summary>
    /// Whether the frame was decrypted; <see langword="null"/> when it is not encrypted, and
    /// <see langword="false"/> when no key was given or the frame did not decrypt.
    /// </summary>
    public bool? Decrypted { get; }

    /// <summary>The payload as a tree, when a type was given; after a failure, the tree up to it.</summary>
    public BinaryDumpNode? Root { get; }

    /// <summary>What a read of the frame raises, or <see langword="null"/> when the frame read completely.</summary>
    public BinarySerializerException? Failure { get; }

    /// <summary>The offset in the payload of the value that failed, when a type was given.</summary>
    public long? FailureOffset { get; }

    /// <summary>The path of the value that failed, such as <c>Order.Lines[2].Note</c>, when a type was given.</summary>
    public string? FailurePath { get; }

    /// <summary>The deepest nesting of the tree.</summary>
    public int MaxDepth { get; }

    /// <summary>The number of nodes in the tree.</summary>
    public int NodeCount { get; }

    internal int FrameLength { get; }

    /// <summary>The report a person reads: the header and the phases, the payload as a tree, and the failure.</summary>
    /// <returns>The same text on every machine: values render with the invariant culture and times in UTC.</returns>
    public override string ToString()
    {
        var text = new StringBuilder();

        if (FormatVersion == 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"Viper V0 payload · {Number(FrameLength)} bytes · no header").Append('\n');
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"Viper V1 frame · {Number(FrameLength)} bytes · header {Number(HeaderLength)} bytes").Append('\n');
            if (Header is { } header)
                AppendServices(text, header);
        }

        if (Root is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"payload {Number(PayloadLength)} bytes as {_typeName} · depth {MaxDepth} · {Number(NodeCount)} nodes").Append('\n');
            AppendNode(text, Root, prefix: string.Empty, childPrefix: string.Empty, isRoot: true);
        }
        else if (!_payloadIsStored && _payload.Length > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"payload {Number(PayloadLength)} bytes").Append('\n');
            text.Append(ToHex());
        }
        else if (_payloadIsStored && _payload.Length > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"stored bytes {Number(_payload.Length)}, not decoded").Append('\n');
        }

        if (Failure is { } failure)
        {
            text.Append(CultureInfo.InvariantCulture, $"failure: {failure.GetType().Name}: {failure.Message}").Append('\n');
            if (FailureOffset is { } offset)
                text.Append(CultureInfo.InvariantCulture, $"failure at @{offset:X4} ({offset}) in {FailurePath}").Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The same report as <see cref="ToString"/>, as JSON.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("formatVersion", FormatVersion);
            json.WriteNumber("frameLength", FrameLength);
            json.WriteNumber("headerLength", HeaderLength);
            json.WriteNumber("payloadLength", PayloadLength);

            if (Header is { } header)
            {
                json.WriteStartObject("header");
                json.WriteBoolean("preserveReferences", header.PreserveReferences);
                json.WriteString("compression", header.Compression.ToString());
                WriteNullable(json, "customCompressionName", header.CustomCompressionName);
                if (header.UncompressedLength is { } uncompressed)
                    json.WriteNumber("uncompressedLength", uncompressed);
                json.WriteString("checksumAlgorithm", header.ChecksumAlgorithm.ToString());
                WriteNullable(json, "customChecksumName", header.CustomChecksumName);
                json.WriteString("checksum", Convert.ToHexString(header.Checksum.Span));
                json.WriteString("encryption", header.Encryption.ToString());
                WriteNullable(json, "customEncryptionName", header.CustomEncryptionName);
                WriteNullable(json, "keyId", header.KeyId);
                json.WriteNumber("onDiskLength", header.OnDiskLength);
                json.WriteEndObject();
            }

            WriteNullable(json, "checksumVerified", ChecksumVerified);
            WriteNullable(json, "decrypted", Decrypted);
            json.WriteNumber("maxDepth", MaxDepth);
            json.WriteNumber("nodeCount", NodeCount);

            if (Failure is { } failure)
            {
                json.WriteStartObject("failure");
                json.WriteString("type", failure.GetType().Name);
                json.WriteString("message", failure.Message);
                if (FailureOffset is { } offset)
                    json.WriteNumber("offset", offset);
                WriteNullable(json, "path", FailurePath);
                json.WriteEndObject();
            }

            if (Root is not null)
            {
                json.WritePropertyName("root");
                WriteNode(json, Root);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>The same report as <see cref="ToString"/>, as XML: one element per node, nested as the tree is.</summary>
    public string ToXml()
    {
        var text = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, NewLineChars = "\n" };

        using (var xml = XmlWriter.Create(text, settings))
        {
            xml.WriteStartElement("dump");
            Attribute(xml, "formatVersion", FormatVersion);
            Attribute(xml, "frameLength", FrameLength);
            Attribute(xml, "headerLength", HeaderLength);
            Attribute(xml, "payloadLength", PayloadLength);
            Attribute(xml, "checksumVerified", ChecksumVerified);
            Attribute(xml, "decrypted", Decrypted);
            Attribute(xml, "maxDepth", MaxDepth);
            Attribute(xml, "nodeCount", NodeCount);

            if (Header is { } header)
            {
                xml.WriteStartElement("header");
                xml.WriteAttributeString("preserveReferences", header.PreserveReferences ? "true" : "false");
                xml.WriteAttributeString("compression", header.Compression.ToString());
                Attribute(xml, "customCompressionName", header.CustomCompressionName);
                Attribute(xml, "uncompressedLength", header.UncompressedLength);
                xml.WriteAttributeString("checksumAlgorithm", header.ChecksumAlgorithm.ToString());
                Attribute(xml, "customChecksumName", header.CustomChecksumName);
                xml.WriteAttributeString("checksum", Convert.ToHexString(header.Checksum.Span));
                xml.WriteAttributeString("encryption", header.Encryption.ToString());
                Attribute(xml, "customEncryptionName", header.CustomEncryptionName);
                Attribute(xml, "keyId", header.KeyId);
                Attribute(xml, "onDiskLength", header.OnDiskLength);
                xml.WriteEndElement();
            }

            if (Failure is { } failure)
            {
                xml.WriteStartElement("failure");
                xml.WriteAttributeString("type", failure.GetType().Name);
                Attribute(xml, "offset", FailureOffset);
                Attribute(xml, "path", FailurePath);
                xml.WriteString(failure.Message);
                xml.WriteEndElement();
            }

            if (Root is not null)
                WriteNode(xml, Root);

            xml.WriteEndElement();
        }

        return text.ToString();
    }

    /// <summary>
    /// Every byte of the payload — or, when it was not decrypted, of the stored bytes — sixteen to a
    /// line, each line labelled with the path of the node its first byte belongs to.
    /// </summary>
    public string ToHex()
    {
        var text = new StringBuilder();
        for (int line = 0; line < _payload.Length; line += 16)
        {
            int count = Math.Min(16, _payload.Length - line);
            text.Append(CultureInfo.InvariantCulture, $"@{line:X4}  ");

            for (int index = 0; index < 16; index++)
            {
                if (index < count)
                    text.Append(_payload[line + index].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                else
                    text.Append("   ");
            }

            string label = _payloadIsStored ? "stored bytes" : Root is null ? "payload" : PathAt(Root, Root.Name, line);
            text.Append(' ').Append(label).Append('\n');
        }

        return text.ToString();
    }

    private void AppendServices(StringBuilder text, BinaryHeaderInfo header)
    {
        if (header.Compression != CompressionAlgorithm.None)
        {
            string algorithm = header.CustomCompressionName ?? header.Compression.ToString();
            string sizes = header.UncompressedLength is { } uncompressed && _compressedLength is { } compressed && compressed > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{Number(uncompressed)} → {Number(compressed)} bytes (×{(double)uncompressed / compressed:0.0})")
                : string.Create(CultureInfo.InvariantCulture, $"{Number(header.UncompressedLength ?? 0)} bytes uncompressed");
            text.Append(CultureInfo.InvariantCulture, $"  compression  {algorithm,-14} {sizes}").Append('\n');
        }

        if (header.ChecksumAlgorithm != ChecksumAlgorithm.None)
        {
            string algorithm = header.CustomChecksumName ?? header.ChecksumAlgorithm.ToString();
            string state = ChecksumVerified switch { true => "verified", false => "does not match", null => "not verified" };
            text.Append(CultureInfo.InvariantCulture, $"  checksum     {algorithm,-14} {Hex(header.Checksum.Span)}   {state}").Append('\n');
        }

        if (header.Encryption != EncryptionAlgorithm.None)
        {
            string algorithm = header.CustomEncryptionName ?? header.Encryption.ToString();
            string key = header.KeyId is null ? "no key id" : "key id \"" + header.KeyId + "\"";
            string state = Decrypted == true ? "decrypted, header authenticated" : "not decrypted";
            text.Append(CultureInfo.InvariantCulture, $"  encryption   {algorithm,-14} {key}   {state}").Append('\n');
        }

        if (header.PreserveReferences)
            text.Append("  references   preserved").Append('\n');
    }

    private static void AppendNode(StringBuilder text, BinaryDumpNode node, string prefix, string childPrefix, bool isRoot)
    {
        string label = isRoot ? node.TypeName : prefix + node.Name;
        text.Append(CultureInfo.InvariantCulture, $"@{node.Offset:X4}  {Pad(label, 30)} {Pad(isRoot ? string.Empty : node.TypeName, 16)} {Describe(node)}");
        text.Append('\n');

        var children = node.ChildList;
        for (int index = 0; index < children.Count; index++)
        {
            bool last = index == children.Count - 1;
            AppendNode(
                text,
                children[index],
                childPrefix + (last ? "└─ " : "├─ "),
                childPrefix + (last ? "   " : "│  "),
                isRoot: false);
        }
    }

    private static string Describe(BinaryDumpNode node) => node.Kind switch
    {
        BinaryDumpNodeKind.Null => "null",
        BinaryDumpNodeKind.Scalar => node.Value ?? string.Empty,
        BinaryDumpNodeKind.Sequence => string.Create(CultureInfo.InvariantCulture, $"{Count(node)} items{Id(node)}"),
        BinaryDumpNodeKind.Map => string.Create(CultureInfo.InvariantCulture, $"{Count(node)} entries{Id(node)}"),
        BinaryDumpNodeKind.Object => string.Create(CultureInfo.InvariantCulture, $"object · {Count(node)} members{Id(node)}"),
        BinaryDumpNodeKind.KeyedObject => string.Create(CultureInfo.InvariantCulture, $"keyed object · {Count(node)} fields{Id(node)}"),
        BinaryDumpNodeKind.KeyedField => string.Create(CultureInfo.InvariantCulture, $"key {node.Key} · {node.Length} bytes"),
        BinaryDumpNodeKind.UnknownKeyedField => string.Create(CultureInfo.InvariantCulture, $"key {node.Key} · unknown, skipped · {node.Length} bytes"),
        BinaryDumpNodeKind.Union => string.Create(CultureInfo.InvariantCulture, $"union tag {node.UnionTag}{Id(node)}"),
        BinaryDumpNodeKind.BackReference => string.Create(CultureInfo.InvariantCulture, $"→ #{node.ReferenceId} {node.ReferenceTarget}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"composite{Id(node)}")
    };

    private static string Count(BinaryDumpNode node) =>
        Number(node.DeclaredCount ?? node.ChildList.Count);

    private static string Id(BinaryDumpNode node) => node.ReferenceId is { } id ? " · #" + id.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string Pad(string value, int width) => value.Length >= width ? value + " " : value.PadRight(width);

    private static string Number(long value) => value.ToString("N", Grouped);

    private static string Hex(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length * 3);
        for (int index = 0; index < bytes.Length; index++)
        {
            if (index > 0)
                text.Append(' ');
            text.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>The path of the deepest node that contains <paramref name="offset"/>.</summary>
    private static string PathAt(BinaryDumpNode node, string path, long offset)
    {
        foreach (var child in node.ChildList)
        {
            if (offset >= child.Offset && offset < child.Offset + Math.Max(child.Length, 1))
            {
                string childPath = child.Name.StartsWith('[') || child.Name.StartsWith('{')
                    ? path + child.Name
                    : path + "." + child.Name;
                return PathAt(child, childPath, offset);
            }
        }

        return path;
    }

    private static void WriteNode(XmlWriter xml, BinaryDumpNode node)
    {
        xml.WriteStartElement("node");
        xml.WriteAttributeString("name", node.Name);
        xml.WriteAttributeString("type", node.TypeName);
        xml.WriteAttributeString("kind", node.Kind.ToString());
        Attribute(xml, "offset", node.Offset);
        Attribute(xml, "length", node.Length);
        Attribute(xml, "value", node.Value);
        Attribute(xml, "key", node.Key);
        Attribute(xml, "unionTag", node.UnionTag);
        Attribute(xml, "referenceId", node.ReferenceId);
        Attribute(xml, "referenceTarget", node.ReferenceTarget);

        foreach (var child in node.ChildList)
            WriteNode(xml, child);

        xml.WriteEndElement();
    }

    private static void Attribute(XmlWriter xml, string name, string? value)
    {
        if (value is not null)
            xml.WriteAttributeString(name, value);
    }

    private static void Attribute(XmlWriter xml, string name, long? value)
    {
        if (value is { } number)
            xml.WriteAttributeString(name, number.ToString(CultureInfo.InvariantCulture));
    }

    private static void Attribute(XmlWriter xml, string name, bool? value)
    {
        if (value is { } flag)
            xml.WriteAttributeString(name, flag ? "true" : "false");
    }

    private static void WriteNode(Utf8JsonWriter json, BinaryDumpNode node)
    {
        json.WriteStartObject();
        json.WriteString("name", node.Name);
        json.WriteString("type", node.TypeName);
        json.WriteString("kind", node.Kind.ToString());
        json.WriteNumber("offset", node.Offset);
        json.WriteNumber("length", node.Length);
        WriteNullable(json, "value", node.Value);
        if (node.Key is { } key)
            json.WriteNumber("key", key);
        if (node.UnionTag is { } tag)
            json.WriteNumber("unionTag", tag);
        if (node.ReferenceId is { } id)
            json.WriteNumber("referenceId", id);
        WriteNullable(json, "referenceTarget", node.ReferenceTarget);

        if (node.ChildList.Count > 0)
        {
            json.WriteStartArray("children");
            foreach (var child in node.ChildList)
                WriteNode(json, child);
            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteNullable(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
            json.WriteNull(name);
        else
            json.WriteString(name, value);
    }

    private static void WriteNullable(Utf8JsonWriter json, string name, bool? value)
    {
        if (value is { } flag)
            json.WriteBoolean(name, flag);
        else
            json.WriteNull(name);
    }
}
