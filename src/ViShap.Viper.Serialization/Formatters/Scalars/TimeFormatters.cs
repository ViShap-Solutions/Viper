namespace ViShap.Viper.Formatters;

internal sealed class DateTimeFormatter : IScalarFormatter<DateTime>
{
    public int MinimumWireSize => sizeof(long);

    public void Write(ref WireWriter writer, DateTime value) => writer.WriteInt64(value.ToBinary());

    public DateTime Read(ref WireReader reader) => DateTime.FromBinary(reader.ReadInt64());
}

internal sealed class DateTimeOffsetFormatter : IScalarFormatter<DateTimeOffset>
{
    public int MinimumWireSize => 2 * sizeof(long);

    public void Write(ref WireWriter writer, DateTimeOffset value)
    {
        writer.WriteInt64(value.Ticks);
        writer.WriteInt64(value.Offset.Ticks);
    }

    public DateTimeOffset Read(ref WireReader reader)
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

internal sealed class TimeSpanFormatter : IScalarFormatter<TimeSpan>
{
    public int MinimumWireSize => sizeof(long);

    public void Write(ref WireWriter writer, TimeSpan value) => writer.WriteInt64(value.Ticks);

    public TimeSpan Read(ref WireReader reader) => TimeSpan.FromTicks(reader.ReadInt64());
}

internal sealed class DateOnlyFormatter : IScalarFormatter<DateOnly>
{
    public int MinimumWireSize => sizeof(int);

    public void Write(ref WireWriter writer, DateOnly value) => writer.WriteInt32(value.DayNumber);

    public DateOnly Read(ref WireReader reader)
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

internal sealed class TimeOnlyFormatter : IScalarFormatter<TimeOnly>
{
    public int MinimumWireSize => sizeof(long);

    public void Write(ref WireWriter writer, TimeOnly value) => writer.WriteInt64(value.Ticks);

    public TimeOnly Read(ref WireReader reader)
    {
        long ticks = reader.ReadInt64();

        try
        {
            return new TimeOnly(ticks);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BinaryFormatException($"TimeOnly ticks {ticks} is outside the supported range.", ex);
        }
    }
}

internal sealed class TimeZoneInfoFormatter : IScalarFormatter<TimeZoneInfo>
{
    public int MinimumWireSize => 1;

    public void Write(ref WireWriter writer, TimeZoneInfo value) =>
        writer.WriteString(value.ToSerializedString());

    public TimeZoneInfo Read(ref WireReader reader)
    {
        string serialized = reader.ReadString();

        try
        {
            return TimeZoneInfo.FromSerializedString(serialized);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidTimeZoneException
                                      or System.Runtime.Serialization.SerializationException)
        {
            throw new BinaryFormatException("TimeZoneInfo payload is not a valid serialized time zone.", ex);
        }
    }
}
