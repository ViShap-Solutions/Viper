namespace ViShap.Viper.Formatters;

internal sealed class MultiDimensionalArrayFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType.IsArray && declaredType.GetArrayRank() > 1;

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var array = (Array)value;
        var elementType = declaredType.GetElementType()!;

        writer.WriteInt32(array.Rank);
        for (int d = 0; d < array.Rank; d++) writer.WriteInt32(array.GetLength(d));

        foreach (var item in array) writer.WriteElement(item, elementType);
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

        DeserializationGuard.ValidateTotalElements(
            reader,
            lengths,
            reader.Budget.Limits.MaxArrayLength,
            "Multi-dimensional array");

        var array = Array.CreateInstance(
            elementType,
            lengths);

        Fill(array, new int[rank], 0, reader, elementType);
        
        return array;
    }

    private static void Fill(Array array, int[] indices, int dimension, BinaryPayloadReader reader, Type elementType)
    {
        if (dimension == array.Rank) { array.SetValue(reader.ReadElement(elementType), indices); return; }
        for (int i = 0; i < array.GetLength(dimension); i++)
        {
            indices[dimension] = i;
            Fill(array, indices, dimension + 1, reader, elementType);
        }
    }
}