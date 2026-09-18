using System.Collections;
using System.Collections.ObjectModel;

namespace ViShap.Viper.Formatters;

internal sealed class ReadOnlyDictionaryFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() is var def &&
        (def == typeof(ReadOnlyDictionary<,>) || def == typeof(IReadOnlyDictionary<,>));

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        Type keyType = args[0], valueType = args[1];
        var entries = ((IEnumerable)value).Cast<object>().ToList();
        writer.WriteInt32(writer.ValidateDictionaryEntryCountForWrite(entries.Count, "Dictionary entry count"));

        foreach (var entry in entries)
        {
            var accessors = DictionaryAccessorCache.GetEntryAccessors(entry.GetType());
            writer.WriteElement(accessors.KeyGetter(entry), keyType);
            writer.WriteElement(accessors.ValueGetter(entry), valueType);
        }
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        Type keyType = args[0], valueType = args[1];
        var dict = (IDictionary)ActivatorCache.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));

        int count = DeserializationGuard.ValidateCount(
            reader,
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxDictionaryEntries,
            "Dictionary entry count");
        
        for (int i = 0; i < count; i++)
            dict.Add(reader.ReadElement(keyType)!, reader.ReadElement(valueType));

        var wrapperType = typeof(ReadOnlyDictionary<,>).MakeGenericType(keyType, valueType);
        var dictInterface = typeof(IDictionary<,>).MakeGenericType(keyType, valueType);
        return ActivatorCache.GetOneArgConstructor(wrapperType, dictInterface)(dict);
    }
}