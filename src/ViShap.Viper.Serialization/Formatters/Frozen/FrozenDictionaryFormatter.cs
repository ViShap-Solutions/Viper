using System.Collections;
using System.Collections.Frozen;

namespace ViShap.Viper.Formatters;

internal sealed class FrozenDictionaryFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(FrozenDictionary<,>);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var args = declaredType.GetGenericArguments();
        Type keyType = args[0], valueType = args[1];

        var entries = ((IEnumerable)value).Cast<object>().ToList();
        writer.WriteInt32(writer.ValidateDictionaryEntryCountForWrite(entries.Count, "FrozenDictionary entry count"));
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

        var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        var temp = (IDictionary)ActivatorCache.CreateInstance(dictType);

        int count = DeserializationGuard.ValidateCount(
            reader, 
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxDictionaryEntries, 
            "FrozenDictionary entry count");
        
        for (int i = 0; i < count; i++) temp.Add(reader.ReadElement(keyType)!, reader.ReadElement(valueType));

        return FrozenFactoryCache.GetToFrozenDictionary(keyType, valueType)(temp);
    }
}