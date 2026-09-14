using System.Collections;
using System.Text;

namespace ViShap.Viper.Diagnostics;

public static class ObjectGraphDumper
{
    public static string Dump(object? root)
    {
        var sb = new StringBuilder();
        Write(sb, root, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? value, int depth, HashSet<object> seen)
    {
        string indent = new(' ', depth * 2);

        if (value is null) { sb.AppendLine($"{indent}null"); return; }

        var type = value.GetType();

        if (type.IsPrimitive || value is string or Guid or DateTime or TimeSpan or decimal or Enum)
        {
            sb.AppendLine($"{indent}{value} ({type.Name})");
            return;
        }

        if (!type.IsValueType && !seen.Add(value))
        {
            sb.AppendLine($"{indent}<already shown — shared reference, {type.Name}>");
            return;
        }

        if (value is IDictionary dict)
        {
            sb.AppendLine($"{indent}{type.Name} [{dict.Count} entries]");
            foreach (DictionaryEntry entry in dict)
            {
                sb.AppendLine($"{indent}  {entry.Key}:");
                Write(sb, entry.Value, depth + 2, seen);
            }
            return;
        }

        if (value is IEnumerable enumerable)
        {
            var items = enumerable.Cast<object?>().ToList();
            sb.AppendLine($"{indent}{type.Name} [{items.Count} items]");
            foreach (var item in items) Write(sb, item, depth + 1, seen);
            return;
        }

        sb.AppendLine($"{indent}{type.Name}");
        var plan = TypeAccessorCache.GetOrBuild(type);
        foreach (var member in plan.Members)
        {
            sb.Append($"{indent}  {member.Name}: ");
            var memberValue = member.Getter(value);
            if (memberValue is null || memberValue.GetType().IsPrimitive || memberValue is string or Guid or DateTime or TimeSpan or decimal or Enum)
            {
                sb.AppendLine($"{memberValue ?? "null"}");
            }
            else
            {
                sb.AppendLine();
                Write(sb, memberValue, depth + 2, seen);
            }
        }
    }
}