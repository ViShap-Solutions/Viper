using System.Collections;
using System.Reflection;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.DataSets;

/// <summary>
/// Carries a value of the Viper models into the namesake models of another library, member by member,
/// so every library is measured on the data the corpus generated once from its fixed seed (FAIR-08,
/// FAIR-12). Runs outside every timed region.
/// </summary>
/// <remarks>
/// Identity is kept: an instance reached twice becomes one instance reached twice, and a cycle stays a
/// cycle, so a shared or cyclic dataset projects to the same graph. A value that mentions no model —
/// a string, a scalar, a list of integers — is shared rather than copied: the corpus is never mutated
/// (Benchmark-Plan §7.4), and a copy would only cost memory.
/// </remarks>
internal static class Projection
{
    internal static object? Project(object? source, ModelFamily family) =>
        new Projector(family).Project(source);

    private sealed class Projector(ModelFamily family)
    {
        private readonly Dictionary<object, object> _projected = new(ReferenceEqualityComparer.Instance);

        internal object? Project(object? source)
        {
            if (source is null)
                return null;

            var type = source.GetType();

            if (!ModelFamilies.MentionsModel(type))
                return source;

            if (type.IsEnum)
                return Enum.ToObject(ModelFamilies.Map(type, family), Convert.ToInt64(source, System.Globalization.CultureInfo.InvariantCulture));

            if (_projected.TryGetValue(source, out var known))
                return known;

            var target = ModelFamilies.Map(type, family);

            return source switch
            {
                Array array => ProjectArray(array, target),
                IDictionary dictionary when !ModelFamilies.IsModel(type) => ProjectDictionary(source, dictionary, target),
                IEnumerable items when !ModelFamilies.IsModel(type) => ProjectSequence(source, items, target),
                _ => ProjectObject(source, type, target),
            };
        }

        private Array ProjectArray(Array array, Type target)
        {
            if (array.Rank != 1)
                throw new NotSupportedException($"A multidimensional array of models ({array.GetType()}) has no projection.");

            var result = Array.CreateInstance(target.GetElementType()!, array.Length);
            _projected.Add(array, result);

            for (var i = 0; i < array.Length; i++)
                result.SetValue(Project(array.GetValue(i)), i);

            return result;
        }

        private object ProjectDictionary(object source, IDictionary dictionary, Type target)
        {
            var result = (IDictionary)Activator.CreateInstance(target)!;
            _projected.Add(source, result);

            foreach (DictionaryEntry entry in dictionary)
                result.Add(Project(entry.Key)!, Project(entry.Value));

            return result;
        }

        private object ProjectSequence(object source, IEnumerable items, Type target)
        {
            var definition = target.GetGenericTypeDefinition();
            var element = target.GetGenericArguments()[0];

            var buffer = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in items)
                buffer.Add(Project(item));

            // A stack enumerates from its top and its constructor pushes in order, so the items go in
            // reversed to come out as they were.
            if (definition == typeof(Stack<>))
                buffer = Reverse(buffer, element);

            object result = definition == typeof(List<>)
                ? buffer
                : Activator.CreateInstance(target, buffer)
                    ?? throw new NotSupportedException($"No projection for {target}.");

            _projected.Add(source, result);
            return result;
        }

        private static IList Reverse(IList items, Type element)
        {
            var reversed = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            for (var i = items.Count - 1; i >= 0; i--)
                reversed.Add(items[i]);

            return reversed;
        }

        private object ProjectObject(object source, Type type, Type target)
        {
            var result = Activator.CreateInstance(target, nonPublic: true)
                ?? throw new InvalidOperationException($"{target} cannot be constructed.");

            // Registered before its members, so a member that leads back to it finds it.
            _projected.Add(source, result);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    continue;

                var destination = target.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"{target} has no member '{property.Name}' of {type}.");

                destination.SetValue(result, Project(property.GetValue(source)));
            }

            return result;
        }
    }
}
