using System.Collections;

namespace ViShap.Viper.Formatters;

internal sealed class MultiDimensionalArrayFormatter : ITypeFormatter
{
    private const int CapacityHint = 1024;

    public bool CanHandle(Type declaredType) =>
        declaredType.IsArray && declaredType.GetArrayRank() > 1;

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var array = (Array)value;
        var elementType = declaredType.GetElementType()!;
        var lengths = new int[array.Rank];

        for (int d = 0; d < array.Rank; d++)
            lengths[d] = array.GetLength(d);

        writer.WriteInt32(array.Rank);
        writer.ValidateTotalArrayElementsForWrite(lengths, "Multi-dimensional array");

        for (int d = 0; d < lengths.Length; d++)
            writer.WriteInt32(lengths[d]);

        foreach (var item in array)
            writer.WriteElement(item, elementType);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        var elementType = declaredType.GetElementType()!;
        int expectedRank = declaredType.GetArrayRank();
        int rank = reader.RawReader.ReadInt32();

        if (rank != expectedRank)
            throw new BinaryFormatException(
                $"Array rank {rank} does not match the declared array rank {expectedRank}.");

        var lengths = new int[rank];
        for (int d = 0; d < rank; d++)
            lengths[d] = reader.RawReader.ReadInt32();

        long total = DeserializationGuard.ValidateTotalElements(
            reader,
            lengths,
            reader.Budget.Limits.MaxArrayLength,
            "Multi-dimensional array");

        var listType = typeof(List<>).MakeGenericType(elementType);
        var items = (IList)ActivatorCache.GetOneArgConstructor(
            listType,
            typeof(int))(Math.Min((int)total, CapacityHint));

        int totalCount = checked((int)total);
        for (int i = 0; i < totalCount; i++)
            items.Add(reader.ReadElement(elementType));

        var array = Array.CreateInstance(elementType, lengths);
        int flatIndex = 0;
        Fill(array, new int[rank], 0, items, ref flatIndex);
        return array;
    }

    private static void Fill(
        Array array,
        int[] indices,
        int dimension,
        IList items,
        ref int flatIndex)
    {
        if (dimension == array.Rank)
        {
            array.SetValue(items[flatIndex++], indices);
            return;
        }

        for (int i = 0; i < array.GetLength(dimension); i++)
        {
            indices[dimension] = i;
            Fill(array, indices, dimension + 1, items, ref flatIndex);
        }
    }
}