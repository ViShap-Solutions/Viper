using System.Collections;
using System.Reflection;

namespace ViShap.Viper.Serialization.Benchmarks.Verification;

/// <summary>
/// Compares a restored value with the value it came from, by structure rather than by reference, so a
/// round trip is verified before any timing exists (Benchmark-Plan FAIR-24).
/// </summary>
/// <remarks>
/// Unordered containers are compared as contents, because that is all a serializer promises for them.
/// The first difference found is reported with the path that reaches it.
/// </remarks>
internal static class StructuralComparer
{
    internal static bool Equal(object? expected, object? actual, out string difference)
    {
        var visited = new HashSet<(object, object)>(ReferencePairComparer.Instance);
        difference = string.Empty;

        return Compare(expected, actual, "$", visited, ref difference);
    }

    private static bool Compare(
        object? expected,
        object? actual,
        string path,
        HashSet<(object, object)> visited,
        ref string difference)
    {
        if (expected is null && actual is null)
        {
            return true;
        }

        if (expected is null || actual is null)
        {
            difference = $"{path}: one side is null ({Describe(expected)} vs {Describe(actual)})";
            return false;
        }

        if (ReferenceEquals(expected, actual))
        {
            return true;
        }

        var type = expected.GetType();

        if (IsScalar(type))
        {
            if (!expected.Equals(actual))
            {
                difference = $"{path}: {Describe(expected)} != {Describe(actual)}";
                return false;
            }

            return true;
        }

        if (!visited.Add((expected, actual)))
        {
            return true;
        }

        if (expected is IDictionary expectedMap && actual is IDictionary actualMap)
        {
            return CompareDictionary(expectedMap, actualMap, path, visited, ref difference);
        }

        if (expected is IEnumerable expectedItems && actual is IEnumerable actualItems)
        {
            return CompareEnumerable(expectedItems, actualItems, path, visited, ref difference);
        }

        return CompareMembers(expected, actual, type, path, visited, ref difference);
    }

    private static bool CompareDictionary(
        IDictionary expected,
        IDictionary actual,
        string path,
        HashSet<(object, object)> visited,
        ref string difference)
    {
        if (expected.Count != actual.Count)
        {
            difference = $"{path}: {expected.Count} entries != {actual.Count}";
            return false;
        }

        foreach (DictionaryEntry entry in expected)
        {
            if (!actual.Contains(entry.Key))
            {
                difference = $"{path}: key {Describe(entry.Key)} is missing";
                return false;
            }

            if (!Compare(entry.Value, actual[entry.Key], $"{path}[{Describe(entry.Key)}]", visited, ref difference))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CompareEnumerable(
        IEnumerable expected,
        IEnumerable actual,
        string path,
        HashSet<(object, object)> visited,
        ref string difference)
    {
        var left = Materialize(expected);
        var right = Materialize(actual);

        if (left.Count != right.Count)
        {
            difference = $"{path}: {left.Count} elements != {right.Count}";
            return false;
        }

        var ordered = string.Empty;
        var sameOrder = true;

        for (var i = 0; i < left.Count; i++)
        {
            if (!Compare(left[i], right[i], $"{path}[{i}]", visited, ref ordered))
            {
                sameOrder = false;
                break;
            }
        }

        if (sameOrder)
        {
            return true;
        }

        // An unordered container round trips its contents, not its iteration order.
        var matched = new bool[right.Count];

        foreach (var item in left)
        {
            var found = false;

            for (var i = 0; i < right.Count && !found; i++)
            {
                if (matched[i])
                {
                    continue;
                }

                var ignored = string.Empty;

                if (Compare(item, right[i], path, visited, ref ignored))
                {
                    matched[i] = true;
                    found = true;
                }
            }

            if (!found)
            {
                difference = ordered;
                return false;
            }
        }

        return true;
    }

    private static bool CompareMembers(
        object expected,
        object actual,
        Type type,
        string path,
        HashSet<(object, object)> visited,
        ref string difference)
    {
        var actualType = actual.GetType();

        if (type != actualType && !type.IsAssignableFrom(actualType) && !actualType.IsAssignableFrom(type))
        {
            difference = $"{path}: {type.Name} restored as {actualType.Name}";
            return false;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (!Compare(property.GetValue(expected), property.GetValue(actual), $"{path}.{property.Name}", visited, ref difference))
            {
                return false;
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!Compare(field.GetValue(expected), field.GetValue(actual), $"{path}.{field.Name}", visited, ref difference))
            {
                return false;
            }
        }

        return true;
    }

    private static List<object?> Materialize(IEnumerable items)
    {
        var list = new List<object?>();

        foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(Guid)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan)
        || type == typeof(DateOnly)
        || type == typeof(TimeOnly)
        || type == typeof(Uri)
        || type == typeof(Version);

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string text => $"\"{Trim(text)}\"",
        IEnumerable and not string => $"{value.GetType().Name}",
        _ => value.ToString() ?? value.GetType().Name,
    };

    private static string Trim(string text) => text.Length <= 32 ? text : text[..32] + "…";

    private sealed class ReferencePairComparer : IEqualityComparer<(object, object)>
    {
        internal static readonly ReferencePairComparer Instance = new();

        public bool Equals((object, object) x, (object, object) y) =>
            ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((object, object) pair) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Item1),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Item2));
    }
}
