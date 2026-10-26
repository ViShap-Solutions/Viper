using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace ViShap.Viper.Diagnostics;

/// <summary>
/// The observer the dumper hands the engine: it turns the events of one read into a tree of
/// <see cref="BinaryDumpNode"/>. When the read fails, the nodes still open are the path to the value
/// that failed, and the tree built so far is kept.
/// </summary>
internal sealed class DumpTrace : IWireTrace
{
    private const int MaxStringCharacters = 256;
    private const int MaxBlobBytes = 64;

    private readonly List<BinaryDumpNode> _open = [];
    private readonly List<long> _openFields = [];
    private readonly Dictionary<int, string> _referencePaths = [];
    private readonly List<string> _openPaths = [];
    private string? _label;
    private bool _continue;
    private string? _lastKeyRendering;
    private long _furthest;

    public BinaryDumpNode? Root { get; private set; }

    public int NodeCount { get; private set; }

    public int MaxDepth { get; private set; }

    /// <summary>The deepest node still open: after a failed read, the value that failed.</summary>
    public BinaryDumpNode? Deepest => _open.Count == 0 ? null : _open[^1];

    /// <summary>The path of <see cref="Deepest"/>, such as <c>Order.Lines[2].Note</c>.</summary>
    public string? DeepestPath => _openPaths.Count == 0 ? null : _openPaths[^1];

    public void Label(string name) => _label = name;

    public void LabelIndex(int index) => _label = $"[{index.ToString(CultureInfo.InvariantCulture)}]";

    public void LabelMapKey(int index) => _label = $"[{index.ToString(CultureInfo.InvariantCulture)}].Key";

    public void LabelItem(int item) => _label = $"Item{item.ToString(CultureInfo.InvariantCulture)}";

    public void LabelMapValue(int index) =>
        _label = _lastKeyRendering is { } key
            ? $"{{{key}}}"
            : $"[{index.ToString(CultureInfo.InvariantCulture)}].Value";

    public void Begin(Type declaredType, long offset)
    {
        Reach(offset);

        if (_continue)
        {
            _continue = false;
            return;
        }

        string typeName = TypeNames.Of(declaredType);
        string name = _label ?? (_open.Count == 0 ? typeName : "value");
        _label = null;

        var node = new BinaryDumpNode(name, typeName, offset);
        NodeCount++;

        string path;
        if (_open.Count == 0)
        {
            Root = node;
            path = name;
        }
        else
        {
            _open[^1].ChildList.Add(node);
            path = name.StartsWith('[') || name.StartsWith('{') ? _openPaths[^1] + name : _openPaths[^1] + "." + name;
        }

        _open.Add(node);
        _openPaths.Add(path);
        MaxDepth = Math.Max(MaxDepth, _open.Count);
    }

    public void Continue() => _continue = true;

    public void Null() => _open[^1].Kind = BinaryDumpNodeKind.Null;

    public void Reference(int id, bool back)
    {
        var node = _open[^1];
        node.ReferenceId = id;

        if (back)
        {
            node.Kind = BinaryDumpNodeKind.BackReference;
            node.ReferenceTarget = _referencePaths.TryGetValue(id, out var target) ? target : null;
        }
        else
        {
            _referencePaths[id] = _openPaths[^1];
        }
    }

    public void Shape(TraceShape kind, int count)
    {
        var node = _open[^1];
        if (node.Kind != BinaryDumpNodeKind.Union)
        {
            node.Kind = kind switch
            {
                TraceShape.Sequence => BinaryDumpNodeKind.Sequence,
                TraceShape.Map => BinaryDumpNodeKind.Map,
                TraceShape.Object => BinaryDumpNodeKind.Object,
                TraceShape.KeyedObject => BinaryDumpNodeKind.KeyedObject,
                _ => BinaryDumpNodeKind.Composite
            };
        }

        node.DeclaredCount = count;
    }

    public void Union(byte tag, Type runtimeType)
    {
        var node = _open[^1];
        node.Kind = BinaryDumpNodeKind.Union;
        node.UnionTag = tag;
        node.TypeName = TypeNames.Of(runtimeType);
    }

    public void Value<T>(T value)
    {
        string rendered = Render(value);
        _open[^1].Value = rendered;
        _lastKeyRendering = rendered;
    }

    public void Field(int key, int length, long offset)
    {
        Reach(offset);

        string name = _label ?? $"#{key.ToString(CultureInfo.InvariantCulture)}";
        _label = null;

        var node = new BinaryDumpNode(name, "keyed field", offset)
        {
            Kind = BinaryDumpNodeKind.KeyedField,
            Key = key
        };

        NodeCount++;
        _open[^1].ChildList.Add(node);
        _open.Add(node);
        _openPaths.Add(_openPaths[^1] + "." + name);
        _openFields.Add(offset);
        MaxDepth = Math.Max(MaxDepth, _open.Count);
    }

    public void EndField(bool known, long offset)
    {
        var node = _open[^1];
        if (!known)
        {
            node.Kind = BinaryDumpNodeKind.UnknownKeyedField;
            node.ChildList.Clear();
        }

        _openFields.RemoveAt(_openFields.Count - 1);
        Close(offset);
    }

    public void End(long offset) => Close(offset);

    /// <summary>Closes every node still open at the furthest offset the read reached, keeping the tree.</summary>
    public void Abandon()
    {
        while (_open.Count > 0)
            Close(_furthest);
    }

    private void Close(long offset)
    {
        Reach(offset);
        var node = _open[^1];
        node.Length = (int)(offset - node.Offset);
        _open.RemoveAt(_open.Count - 1);
        _openPaths.RemoveAt(_openPaths.Count - 1);
    }

    private void Reach(long offset) => _furthest = Math.Max(_furthest, offset);

    /// <summary>A scalar as a person reads it: invariant culture, times in UTC, long values cut.</summary>
    internal static string Render<T>(T value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string text:
                return Quote(text);
            case StringBuilder builder:
                return Quote(builder.ToString());
            case char character:
                return Quote(character.ToString());
            case bool flag:
                return flag ? "true" : "false";
            case DateTime moment:
                return (moment.Kind == DateTimeKind.Local ? moment.ToUniversalTime() : moment)
                    .ToString("O", CultureInfo.InvariantCulture) + (moment.Kind == DateTimeKind.Unspecified ? " (unspecified)" : "");
            case DateTimeOffset instant:
                return instant.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            case TimeZoneInfo zone:
                return Quote(zone.Id);
            case CultureInfo culture:
                return Quote(culture.Name);
            case Uri uri:
                return Quote(uri.OriginalString);
            case BitArray bits:
                return Bits(bits);
            case BigInteger number:
                return number.ToString(CultureInfo.InvariantCulture);
            case Enum member:
                return member.ToString();
            case float single:
                return single.ToString("R", CultureInfo.InvariantCulture);
            case double real:
                return real.ToString("R", CultureInfo.InvariantCulture);
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString() ?? string.Empty;
        }
    }

    private static string Quote(string text) =>
        text.Length <= MaxStringCharacters
            ? $"\"{text}\""
            : $"\"{text[..MaxStringCharacters]}\"… ({text.Length.ToString(CultureInfo.InvariantCulture)} characters)";

    private static string Bits(BitArray bits)
    {
        int shown = Math.Min(bits.Length, MaxBlobBytes * 8);
        var text = new StringBuilder(shown + 32);
        for (int index = 0; index < shown; index++)
            text.Append(bits[index] ? '1' : '0');

        if (shown < bits.Length)
            text.Append(CultureInfo.InvariantCulture, $"… ({bits.Length} bits)");

        return text.ToString();
    }
}

/// <summary>How a type is named in a dump: <c>List&lt;Line&gt;</c>, <c>Int32?</c>, <c>Int32[,]</c>.</summary>
internal static class TypeNames
{
    public static string Of(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Of(underlying) + "?";

        if (type.IsArray)
            return Of(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";

        if (!type.IsGenericType)
            return type.Name;

        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];

        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Of)) + ">";
    }
}
