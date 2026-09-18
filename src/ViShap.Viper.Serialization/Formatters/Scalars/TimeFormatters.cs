namespace ViShap.Viper.Formatters;

internal sealed class DateTimeFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(DateTime);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteInt64(((DateTime)value).ToBinary());

    public object Read(ValueReader reader, Type declaredType) =>
        DateTime.FromBinary(reader.ReadInt64());
}

internal sealed class DateTimeOffsetFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(DateTimeOffset);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var value2 = (DateTimeOffset)value;
        writer.WriteInt64(value2.Ticks);
        writer.WriteInt64(value2.Offset.Ticks);
    }

    public object Read(ValueReader reader, Type declaredType)
    {
        long ticks = reader.ReadInt64();
        long offsetTicks = reader.ReadInt64();

        try
        {
            return new DateTimeOffset(ticks, new TimeSpan(offsetTicks));
        }
        catch (ArgumentException ex)
        {
            throw new BinaryFormatException(
                $"DateTimeOffset ticks {ticks} with offset {offsetTicks} is not a valid value.", ex);
        }
    }
}

internal sealed class TimeSpanFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(TimeSpan);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteInt64(((TimeSpan)value).Ticks);

    public object Read(ValueReader reader, Type declaredType) =>
        TimeSpan.FromTicks(reader.ReadInt64());
}

internal sealed class DateOnlyFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(DateOnly);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteInt32(((DateOnly)value).DayNumber);

    public object Read(ValueReader reader, Type declaredType)
    {
        int dayNumber = reader.ReadInt32();

        try
        {
            return DateOnly.FromDayNumber(dayNumber);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BinaryFormatException(
                $"DateOnly day number {dayNumber} is outside the supported range.", ex);
        }
    }
}

internal sealed class TimeOnlyFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(TimeOnly);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteInt64(((TimeOnly)value).Ticks);

    public object Read(ValueReader reader, Type declaredType)
    {
        long ticks = reader.ReadInt64();

        try
        {
            return new TimeOnly(ticks);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BinaryFormatException(
                $"TimeOnly ticks {ticks} is outside the supported range.", ex);
        }
    }
}

internal sealed class TimeZoneInfoFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(TimeZoneInfo);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteString(((TimeZoneInfo)value).ToSerializedString());

    public object Read(ValueReader reader, Type declaredType)
    {
        string serialized = reader.ReadString();

        try
        {
            return TimeZoneInfo.FromSerializedString(serialized);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidTimeZoneException
                                      or System.Runtime.Serialization.SerializationException)
        {
            throw new BinaryFormatException(
                "TimeZoneInfo payload is not a valid serialized time zone.", ex);
        }
    }
}
