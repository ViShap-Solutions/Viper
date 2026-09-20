namespace ViShap.Viper.Serialization.Benchmarks.Models.Viper;

/// <summary>
/// The member-count series of SCALE-04, and the keyed twin of DATA-18 that DIFF-03 subtracts.
/// </summary>
/// <remarks>
/// Every type here carries the same repeating member cycle as <see cref="WideObject"/> — string,
/// double, bool, long, decimal, Guid, DateTime, int — in the same order, so a positional type and the
/// keyed type of the same size differ in their layout and in nothing else. The 200-member positional
/// type of the series is <see cref="WideObject"/> itself.
/// </remarks>
internal static class WideModels
{
    /// <summary>
    /// Fills every property of a wide type from a fixed seed. The same switch serves every size and
    /// both layouts, so two types of one size always carry equal values.
    /// </summary>
    internal static T Populate<T>(T value, DataSets.DeterministicRandom rng)
        where T : notnull
    {
        foreach (var property in typeof(T).GetProperties())
        {
            object boxed = Type.GetTypeCode(property.PropertyType) switch
            {
                TypeCode.Int32 => rng.NextInt32(),
                TypeCode.Int64 => rng.NextInt64(),
                TypeCode.Double => rng.NextDouble() * 1000,
                TypeCode.Boolean => rng.NextBool(),
                TypeCode.Decimal => Math.Round((decimal)(rng.NextDouble() * 1000), 3),
                TypeCode.String => rng.NextAscii(6, 20),
                TypeCode.DateTime => rng.NextDateTime(),
                _ => rng.NextGuid(),
            };

            property.SetValue(value, boxed);
        }

        return value;
    }
}

/// <summary>SCALE-04 — 5 positional members.</summary>
internal sealed class WidePositional005
{
    public string M001 { get; set; } = string.Empty;

    public double M002 { get; set; }

    public bool M003 { get; set; }

    public long M004 { get; set; }

    public decimal M005 { get; set; }
}

/// <summary>SCALE-04 — 20 positional members.</summary>
internal sealed class WidePositional020
{
    public string M001 { get; set; } = string.Empty;

    public double M002 { get; set; }

    public bool M003 { get; set; }

    public long M004 { get; set; }

    public decimal M005 { get; set; }

    public Guid M006 { get; set; }

    public DateTime M007 { get; set; }

    public int M008 { get; set; }

    public string M009 { get; set; } = string.Empty;

    public double M010 { get; set; }

    public bool M011 { get; set; }

    public long M012 { get; set; }

    public decimal M013 { get; set; }

    public Guid M014 { get; set; }

    public DateTime M015 { get; set; }

    public int M016 { get; set; }

    public string M017 { get; set; } = string.Empty;

    public double M018 { get; set; }

    public bool M019 { get; set; }

    public long M020 { get; set; }
}

/// <summary>SCALE-04 — 50 positional members.</summary>
internal sealed class WidePositional050
{
    public string M001 { get; set; } = string.Empty;

    public double M002 { get; set; }

    public bool M003 { get; set; }

    public long M004 { get; set; }

    public decimal M005 { get; set; }

    public Guid M006 { get; set; }

    public DateTime M007 { get; set; }

    public int M008 { get; set; }

    public string M009 { get; set; } = string.Empty;

    public double M010 { get; set; }

    public bool M011 { get; set; }

    public long M012 { get; set; }

    public decimal M013 { get; set; }

    public Guid M014 { get; set; }

    public DateTime M015 { get; set; }

    public int M016 { get; set; }

    public string M017 { get; set; } = string.Empty;

    public double M018 { get; set; }

    public bool M019 { get; set; }

    public long M020 { get; set; }

    public decimal M021 { get; set; }

    public Guid M022 { get; set; }

    public DateTime M023 { get; set; }

    public int M024 { get; set; }

    public string M025 { get; set; } = string.Empty;

    public double M026 { get; set; }

    public bool M027 { get; set; }

    public long M028 { get; set; }

    public decimal M029 { get; set; }

    public Guid M030 { get; set; }

    public DateTime M031 { get; set; }

    public int M032 { get; set; }

    public string M033 { get; set; } = string.Empty;

    public double M034 { get; set; }

    public bool M035 { get; set; }

    public long M036 { get; set; }

    public decimal M037 { get; set; }

    public Guid M038 { get; set; }

    public DateTime M039 { get; set; }

    public int M040 { get; set; }

    public string M041 { get; set; } = string.Empty;

    public double M042 { get; set; }

    public bool M043 { get; set; }

    public long M044 { get; set; }

    public decimal M045 { get; set; }

    public Guid M046 { get; set; }

    public DateTime M047 { get; set; }

    public int M048 { get; set; }

    public string M049 { get; set; } = string.Empty;

    public double M050 { get; set; }
}

/// <summary>SCALE-04 — 100 positional members.</summary>
internal sealed class WidePositional100
{
    public string M001 { get; set; } = string.Empty;

    public double M002 { get; set; }

    public bool M003 { get; set; }

    public long M004 { get; set; }

    public decimal M005 { get; set; }

    public Guid M006 { get; set; }

    public DateTime M007 { get; set; }

    public int M008 { get; set; }

    public string M009 { get; set; } = string.Empty;

    public double M010 { get; set; }

    public bool M011 { get; set; }

    public long M012 { get; set; }

    public decimal M013 { get; set; }

    public Guid M014 { get; set; }

    public DateTime M015 { get; set; }

    public int M016 { get; set; }

    public string M017 { get; set; } = string.Empty;

    public double M018 { get; set; }

    public bool M019 { get; set; }

    public long M020 { get; set; }

    public decimal M021 { get; set; }

    public Guid M022 { get; set; }

    public DateTime M023 { get; set; }

    public int M024 { get; set; }

    public string M025 { get; set; } = string.Empty;

    public double M026 { get; set; }

    public bool M027 { get; set; }

    public long M028 { get; set; }

    public decimal M029 { get; set; }

    public Guid M030 { get; set; }

    public DateTime M031 { get; set; }

    public int M032 { get; set; }

    public string M033 { get; set; } = string.Empty;

    public double M034 { get; set; }

    public bool M035 { get; set; }

    public long M036 { get; set; }

    public decimal M037 { get; set; }

    public Guid M038 { get; set; }

    public DateTime M039 { get; set; }

    public int M040 { get; set; }

    public string M041 { get; set; } = string.Empty;

    public double M042 { get; set; }

    public bool M043 { get; set; }

    public long M044 { get; set; }

    public decimal M045 { get; set; }

    public Guid M046 { get; set; }

    public DateTime M047 { get; set; }

    public int M048 { get; set; }

    public string M049 { get; set; } = string.Empty;

    public double M050 { get; set; }

    public bool M051 { get; set; }

    public long M052 { get; set; }

    public decimal M053 { get; set; }

    public Guid M054 { get; set; }

    public DateTime M055 { get; set; }

    public int M056 { get; set; }

    public string M057 { get; set; } = string.Empty;

    public double M058 { get; set; }

    public bool M059 { get; set; }

    public long M060 { get; set; }

    public decimal M061 { get; set; }

    public Guid M062 { get; set; }

    public DateTime M063 { get; set; }

    public int M064 { get; set; }

    public string M065 { get; set; } = string.Empty;

    public double M066 { get; set; }

    public bool M067 { get; set; }

    public long M068 { get; set; }

    public decimal M069 { get; set; }

    public Guid M070 { get; set; }

    public DateTime M071 { get; set; }

    public int M072 { get; set; }

    public string M073 { get; set; } = string.Empty;

    public double M074 { get; set; }

    public bool M075 { get; set; }

    public long M076 { get; set; }

    public decimal M077 { get; set; }

    public Guid M078 { get; set; }

    public DateTime M079 { get; set; }

    public int M080 { get; set; }

    public string M081 { get; set; } = string.Empty;

    public double M082 { get; set; }

    public bool M083 { get; set; }

    public long M084 { get; set; }

    public decimal M085 { get; set; }

    public Guid M086 { get; set; }

    public DateTime M087 { get; set; }

    public int M088 { get; set; }

    public string M089 { get; set; } = string.Empty;

    public double M090 { get; set; }

    public bool M091 { get; set; }

    public long M092 { get; set; }

    public decimal M093 { get; set; }

    public Guid M094 { get; set; }

    public DateTime M095 { get; set; }

    public int M096 { get; set; }

    public string M097 { get; set; } = string.Empty;

    public double M098 { get; set; }

    public bool M099 { get; set; }

    public long M100 { get; set; }
}

/// <summary>SCALE-04 — 5 keyed members.</summary>
[BinaryContract]
internal sealed class WideKeyed005
{
    [BinaryKey(1)]
    public string M001 { get; set; } = string.Empty;

    [BinaryKey(2)]
    public double M002 { get; set; }

    [BinaryKey(3)]
    public bool M003 { get; set; }

    [BinaryKey(4)]
    public long M004 { get; set; }

    [BinaryKey(5)]
    public decimal M005 { get; set; }
}

/// <summary>SCALE-04 — 20 keyed members.</summary>
[BinaryContract]
internal sealed class WideKeyed020
{
    [BinaryKey(1)]
    public string M001 { get; set; } = string.Empty;

    [BinaryKey(2)]
    public double M002 { get; set; }

    [BinaryKey(3)]
    public bool M003 { get; set; }

    [BinaryKey(4)]
    public long M004 { get; set; }

    [BinaryKey(5)]
    public decimal M005 { get; set; }

    [BinaryKey(6)]
    public Guid M006 { get; set; }

    [BinaryKey(7)]
    public DateTime M007 { get; set; }

    [BinaryKey(8)]
    public int M008 { get; set; }

    [BinaryKey(9)]
    public string M009 { get; set; } = string.Empty;

    [BinaryKey(10)]
    public double M010 { get; set; }

    [BinaryKey(11)]
    public bool M011 { get; set; }

    [BinaryKey(12)]
    public long M012 { get; set; }

    [BinaryKey(13)]
    public decimal M013 { get; set; }

    [BinaryKey(14)]
    public Guid M014 { get; set; }

    [BinaryKey(15)]
    public DateTime M015 { get; set; }

    [BinaryKey(16)]
    public int M016 { get; set; }

    [BinaryKey(17)]
    public string M017 { get; set; } = string.Empty;

    [BinaryKey(18)]
    public double M018 { get; set; }

    [BinaryKey(19)]
    public bool M019 { get; set; }

    [BinaryKey(20)]
    public long M020 { get; set; }
}

/// <summary>SCALE-04 — 50 keyed members.</summary>
[BinaryContract]
internal sealed class WideKeyed050
{
    [BinaryKey(1)]
    public string M001 { get; set; } = string.Empty;

    [BinaryKey(2)]
    public double M002 { get; set; }

    [BinaryKey(3)]
    public bool M003 { get; set; }

    [BinaryKey(4)]
    public long M004 { get; set; }

    [BinaryKey(5)]
    public decimal M005 { get; set; }

    [BinaryKey(6)]
    public Guid M006 { get; set; }

    [BinaryKey(7)]
    public DateTime M007 { get; set; }

    [BinaryKey(8)]
    public int M008 { get; set; }

    [BinaryKey(9)]
    public string M009 { get; set; } = string.Empty;

    [BinaryKey(10)]
    public double M010 { get; set; }

    [BinaryKey(11)]
    public bool M011 { get; set; }

    [BinaryKey(12)]
    public long M012 { get; set; }

    [BinaryKey(13)]
    public decimal M013 { get; set; }

    [BinaryKey(14)]
    public Guid M014 { get; set; }

    [BinaryKey(15)]
    public DateTime M015 { get; set; }

    [BinaryKey(16)]
    public int M016 { get; set; }

    [BinaryKey(17)]
    public string M017 { get; set; } = string.Empty;

    [BinaryKey(18)]
    public double M018 { get; set; }

    [BinaryKey(19)]
    public bool M019 { get; set; }

    [BinaryKey(20)]
    public long M020 { get; set; }

    [BinaryKey(21)]
    public decimal M021 { get; set; }

    [BinaryKey(22)]
    public Guid M022 { get; set; }

    [BinaryKey(23)]
    public DateTime M023 { get; set; }

    [BinaryKey(24)]
    public int M024 { get; set; }

    [BinaryKey(25)]
    public string M025 { get; set; } = string.Empty;

    [BinaryKey(26)]
    public double M026 { get; set; }

    [BinaryKey(27)]
    public bool M027 { get; set; }

    [BinaryKey(28)]
    public long M028 { get; set; }

    [BinaryKey(29)]
    public decimal M029 { get; set; }

    [BinaryKey(30)]
    public Guid M030 { get; set; }

    [BinaryKey(31)]
    public DateTime M031 { get; set; }

    [BinaryKey(32)]
    public int M032 { get; set; }

    [BinaryKey(33)]
    public string M033 { get; set; } = string.Empty;

    [BinaryKey(34)]
    public double M034 { get; set; }

    [BinaryKey(35)]
    public bool M035 { get; set; }

    [BinaryKey(36)]
    public long M036 { get; set; }

    [BinaryKey(37)]
    public decimal M037 { get; set; }

    [BinaryKey(38)]
    public Guid M038 { get; set; }

    [BinaryKey(39)]
    public DateTime M039 { get; set; }

    [BinaryKey(40)]
    public int M040 { get; set; }

    [BinaryKey(41)]
    public string M041 { get; set; } = string.Empty;

    [BinaryKey(42)]
    public double M042 { get; set; }

    [BinaryKey(43)]
    public bool M043 { get; set; }

    [BinaryKey(44)]
    public long M044 { get; set; }

    [BinaryKey(45)]
    public decimal M045 { get; set; }

    [BinaryKey(46)]
    public Guid M046 { get; set; }

    [BinaryKey(47)]
    public DateTime M047 { get; set; }

    [BinaryKey(48)]
    public int M048 { get; set; }

    [BinaryKey(49)]
    public string M049 { get; set; } = string.Empty;

    [BinaryKey(50)]
    public double M050 { get; set; }
}

/// <summary>SCALE-04 — 100 keyed members.</summary>
[BinaryContract]
internal sealed class WideKeyed100
{
    [BinaryKey(1)]
    public string M001 { get; set; } = string.Empty;

    [BinaryKey(2)]
    public double M002 { get; set; }

    [BinaryKey(3)]
    public bool M003 { get; set; }

    [BinaryKey(4)]
    public long M004 { get; set; }

    [BinaryKey(5)]
    public decimal M005 { get; set; }

    [BinaryKey(6)]
    public Guid M006 { get; set; }

    [BinaryKey(7)]
    public DateTime M007 { get; set; }

    [BinaryKey(8)]
    public int M008 { get; set; }

    [BinaryKey(9)]
    public string M009 { get; set; } = string.Empty;

    [BinaryKey(10)]
    public double M010 { get; set; }

    [BinaryKey(11)]
    public bool M011 { get; set; }

    [BinaryKey(12)]
    public long M012 { get; set; }

    [BinaryKey(13)]
    public decimal M013 { get; set; }

    [BinaryKey(14)]
    public Guid M014 { get; set; }

    [BinaryKey(15)]
    public DateTime M015 { get; set; }

    [BinaryKey(16)]
    public int M016 { get; set; }

    [BinaryKey(17)]
    public string M017 { get; set; } = string.Empty;

    [BinaryKey(18)]
    public double M018 { get; set; }

    [BinaryKey(19)]
    public bool M019 { get; set; }

    [BinaryKey(20)]
    public long M020 { get; set; }

    [BinaryKey(21)]
    public decimal M021 { get; set; }

    [BinaryKey(22)]
    public Guid M022 { get; set; }

    [BinaryKey(23)]
    public DateTime M023 { get; set; }

    [BinaryKey(24)]
    public int M024 { get; set; }

    [BinaryKey(25)]
    public string M025 { get; set; } = string.Empty;

    [BinaryKey(26)]
    public double M026 { get; set; }

    [BinaryKey(27)]
    public bool M027 { get; set; }

    [BinaryKey(28)]
    public long M028 { get; set; }

    [BinaryKey(29)]
    public decimal M029 { get; set; }

    [BinaryKey(30)]
    public Guid M030 { get; set; }

    [BinaryKey(31)]
    public DateTime M031 { get; set; }

    [BinaryKey(32)]
    public int M032 { get; set; }

    [BinaryKey(33)]
    public string M033 { get; set; } = string.Empty;

    [BinaryKey(34)]
    public double M034 { get; set; }

    [BinaryKey(35)]
    public bool M035 { get; set; }

    [BinaryKey(36)]
    public long M036 { get; set; }

    [BinaryKey(37)]
    public decimal M037 { get; set; }

    [BinaryKey(38)]
    public Guid M038 { get; set; }

    [BinaryKey(39)]
    public DateTime M039 { get; set; }

    [BinaryKey(40)]
    public int M040 { get; set; }

    [BinaryKey(41)]
    public string M041 { get; set; } = string.Empty;

    [BinaryKey(42)]
    public double M042 { get; set; }

    [BinaryKey(43)]
    public bool M043 { get; set; }

    [BinaryKey(44)]
    public long M044 { get; set; }

    [BinaryKey(45)]
    public decimal M045 { get; set; }

    [BinaryKey(46)]
    public Guid M046 { get; set; }

    [BinaryKey(47)]
    public DateTime M047 { get; set; }

    [BinaryKey(48)]
    public int M048 { get; set; }

    [BinaryKey(49)]
    public string M049 { get; set; } = string.Empty;

    [BinaryKey(50)]
    public double M050 { get; set; }

    [BinaryKey(51)]
    public bool M051 { get; set; }

    [BinaryKey(52)]
    public long M052 { get; set; }

    [BinaryKey(53)]
    public decimal M053 { get; set; }

    [BinaryKey(54)]
    public Guid M054 { get; set; }

    [BinaryKey(55)]
    public DateTime M055 { get; set; }

    [BinaryKey(56)]
    public int M056 { get; set; }

    [BinaryKey(57)]
    public string M057 { get; set; } = string.Empty;

    [BinaryKey(58)]
    public double M058 { get; set; }

    [BinaryKey(59)]
    public bool M059 { get; set; }

    [BinaryKey(60)]
    public long M060 { get; set; }

    [BinaryKey(61)]
    public decimal M061 { get; set; }

    [BinaryKey(62)]
    public Guid M062 { get; set; }

    [BinaryKey(63)]
    public DateTime M063 { get; set; }

    [BinaryKey(64)]
    public int M064 { get; set; }

    [BinaryKey(65)]
    public string M065 { get; set; } = string.Empty;

    [BinaryKey(66)]
    public double M066 { get; set; }

    [BinaryKey(67)]
    public bool M067 { get; set; }

    [BinaryKey(68)]
    public long M068 { get; set; }

    [BinaryKey(69)]
    public decimal M069 { get; set; }

    [BinaryKey(70)]
    public Guid M070 { get; set; }

    [BinaryKey(71)]
    public DateTime M071 { get; set; }

    [BinaryKey(72)]
    public int M072 { get; set; }

    [BinaryKey(73)]
    public string M073 { get; set; } = string.Empty;

    [BinaryKey(74)]
    public double M074 { get; set; }

    [BinaryKey(75)]
    public bool M075 { get; set; }

    [BinaryKey(76)]
    public long M076 { get; set; }

    [BinaryKey(77)]
    public decimal M077 { get; set; }

    [BinaryKey(78)]
    public Guid M078 { get; set; }

    [BinaryKey(79)]
    public DateTime M079 { get; set; }

    [BinaryKey(80)]
    public int M080 { get; set; }

    [BinaryKey(81)]
    public string M081 { get; set; } = string.Empty;

    [BinaryKey(82)]
    public double M082 { get; set; }

    [BinaryKey(83)]
    public bool M083 { get; set; }

    [BinaryKey(84)]
    public long M084 { get; set; }

    [BinaryKey(85)]
    public decimal M085 { get; set; }

    [BinaryKey(86)]
    public Guid M086 { get; set; }

    [BinaryKey(87)]
    public DateTime M087 { get; set; }

    [BinaryKey(88)]
    public int M088 { get; set; }

    [BinaryKey(89)]
    public string M089 { get; set; } = string.Empty;

    [BinaryKey(90)]
    public double M090 { get; set; }

    [BinaryKey(91)]
    public bool M091 { get; set; }

    [BinaryKey(92)]
    public long M092 { get; set; }

    [BinaryKey(93)]
    public decimal M093 { get; set; }

    [BinaryKey(94)]
    public Guid M094 { get; set; }

    [BinaryKey(95)]
    public DateTime M095 { get; set; }

    [BinaryKey(96)]
    public int M096 { get; set; }

    [BinaryKey(97)]
    public string M097 { get; set; } = string.Empty;

    [BinaryKey(98)]
    public double M098 { get; set; }

    [BinaryKey(99)]
    public bool M099 { get; set; }

    [BinaryKey(100)]
    public long M100 { get; set; }
}

/// <summary>SCALE-04 — 200 keyed members.</summary>
[BinaryContract]
internal sealed class WideKeyed200
{
    [BinaryKey(1)]
    public string M001 { get; set; } = string.Empty;

    [BinaryKey(2)]
    public double M002 { get; set; }

    [BinaryKey(3)]
    public bool M003 { get; set; }

    [BinaryKey(4)]
    public long M004 { get; set; }

    [BinaryKey(5)]
    public decimal M005 { get; set; }

    [BinaryKey(6)]
    public Guid M006 { get; set; }

    [BinaryKey(7)]
    public DateTime M007 { get; set; }

    [BinaryKey(8)]
    public int M008 { get; set; }

    [BinaryKey(9)]
    public string M009 { get; set; } = string.Empty;

    [BinaryKey(10)]
    public double M010 { get; set; }

    [BinaryKey(11)]
    public bool M011 { get; set; }

    [BinaryKey(12)]
    public long M012 { get; set; }

    [BinaryKey(13)]
    public decimal M013 { get; set; }

    [BinaryKey(14)]
    public Guid M014 { get; set; }

    [BinaryKey(15)]
    public DateTime M015 { get; set; }

    [BinaryKey(16)]
    public int M016 { get; set; }

    [BinaryKey(17)]
    public string M017 { get; set; } = string.Empty;

    [BinaryKey(18)]
    public double M018 { get; set; }

    [BinaryKey(19)]
    public bool M019 { get; set; }

    [BinaryKey(20)]
    public long M020 { get; set; }

    [BinaryKey(21)]
    public decimal M021 { get; set; }

    [BinaryKey(22)]
    public Guid M022 { get; set; }

    [BinaryKey(23)]
    public DateTime M023 { get; set; }

    [BinaryKey(24)]
    public int M024 { get; set; }

    [BinaryKey(25)]
    public string M025 { get; set; } = string.Empty;

    [BinaryKey(26)]
    public double M026 { get; set; }

    [BinaryKey(27)]
    public bool M027 { get; set; }

    [BinaryKey(28)]
    public long M028 { get; set; }

    [BinaryKey(29)]
    public decimal M029 { get; set; }

    [BinaryKey(30)]
    public Guid M030 { get; set; }

    [BinaryKey(31)]
    public DateTime M031 { get; set; }

    [BinaryKey(32)]
    public int M032 { get; set; }

    [BinaryKey(33)]
    public string M033 { get; set; } = string.Empty;

    [BinaryKey(34)]
    public double M034 { get; set; }

    [BinaryKey(35)]
    public bool M035 { get; set; }

    [BinaryKey(36)]
    public long M036 { get; set; }

    [BinaryKey(37)]
    public decimal M037 { get; set; }

    [BinaryKey(38)]
    public Guid M038 { get; set; }

    [BinaryKey(39)]
    public DateTime M039 { get; set; }

    [BinaryKey(40)]
    public int M040 { get; set; }

    [BinaryKey(41)]
    public string M041 { get; set; } = string.Empty;

    [BinaryKey(42)]
    public double M042 { get; set; }

    [BinaryKey(43)]
    public bool M043 { get; set; }

    [BinaryKey(44)]
    public long M044 { get; set; }

    [BinaryKey(45)]
    public decimal M045 { get; set; }

    [BinaryKey(46)]
    public Guid M046 { get; set; }

    [BinaryKey(47)]
    public DateTime M047 { get; set; }

    [BinaryKey(48)]
    public int M048 { get; set; }

    [BinaryKey(49)]
    public string M049 { get; set; } = string.Empty;

    [BinaryKey(50)]
    public double M050 { get; set; }

    [BinaryKey(51)]
    public bool M051 { get; set; }

    [BinaryKey(52)]
    public long M052 { get; set; }

    [BinaryKey(53)]
    public decimal M053 { get; set; }

    [BinaryKey(54)]
    public Guid M054 { get; set; }

    [BinaryKey(55)]
    public DateTime M055 { get; set; }

    [BinaryKey(56)]
    public int M056 { get; set; }

    [BinaryKey(57)]
    public string M057 { get; set; } = string.Empty;

    [BinaryKey(58)]
    public double M058 { get; set; }

    [BinaryKey(59)]
    public bool M059 { get; set; }

    [BinaryKey(60)]
    public long M060 { get; set; }

    [BinaryKey(61)]
    public decimal M061 { get; set; }

    [BinaryKey(62)]
    public Guid M062 { get; set; }

    [BinaryKey(63)]
    public DateTime M063 { get; set; }

    [BinaryKey(64)]
    public int M064 { get; set; }

    [BinaryKey(65)]
    public string M065 { get; set; } = string.Empty;

    [BinaryKey(66)]
    public double M066 { get; set; }

    [BinaryKey(67)]
    public bool M067 { get; set; }

    [BinaryKey(68)]
    public long M068 { get; set; }

    [BinaryKey(69)]
    public decimal M069 { get; set; }

    [BinaryKey(70)]
    public Guid M070 { get; set; }

    [BinaryKey(71)]
    public DateTime M071 { get; set; }

    [BinaryKey(72)]
    public int M072 { get; set; }

    [BinaryKey(73)]
    public string M073 { get; set; } = string.Empty;

    [BinaryKey(74)]
    public double M074 { get; set; }

    [BinaryKey(75)]
    public bool M075 { get; set; }

    [BinaryKey(76)]
    public long M076 { get; set; }

    [BinaryKey(77)]
    public decimal M077 { get; set; }

    [BinaryKey(78)]
    public Guid M078 { get; set; }

    [BinaryKey(79)]
    public DateTime M079 { get; set; }

    [BinaryKey(80)]
    public int M080 { get; set; }

    [BinaryKey(81)]
    public string M081 { get; set; } = string.Empty;

    [BinaryKey(82)]
    public double M082 { get; set; }

    [BinaryKey(83)]
    public bool M083 { get; set; }

    [BinaryKey(84)]
    public long M084 { get; set; }

    [BinaryKey(85)]
    public decimal M085 { get; set; }

    [BinaryKey(86)]
    public Guid M086 { get; set; }

    [BinaryKey(87)]
    public DateTime M087 { get; set; }

    [BinaryKey(88)]
    public int M088 { get; set; }

    [BinaryKey(89)]
    public string M089 { get; set; } = string.Empty;

    [BinaryKey(90)]
    public double M090 { get; set; }

    [BinaryKey(91)]
    public bool M091 { get; set; }

    [BinaryKey(92)]
    public long M092 { get; set; }

    [BinaryKey(93)]
    public decimal M093 { get; set; }

    [BinaryKey(94)]
    public Guid M094 { get; set; }

    [BinaryKey(95)]
    public DateTime M095 { get; set; }

    [BinaryKey(96)]
    public int M096 { get; set; }

    [BinaryKey(97)]
    public string M097 { get; set; } = string.Empty;

    [BinaryKey(98)]
    public double M098 { get; set; }

    [BinaryKey(99)]
    public bool M099 { get; set; }

    [BinaryKey(100)]
    public long M100 { get; set; }

    [BinaryKey(101)]
    public decimal M101 { get; set; }

    [BinaryKey(102)]
    public Guid M102 { get; set; }

    [BinaryKey(103)]
    public DateTime M103 { get; set; }

    [BinaryKey(104)]
    public int M104 { get; set; }

    [BinaryKey(105)]
    public string M105 { get; set; } = string.Empty;

    [BinaryKey(106)]
    public double M106 { get; set; }

    [BinaryKey(107)]
    public bool M107 { get; set; }

    [BinaryKey(108)]
    public long M108 { get; set; }

    [BinaryKey(109)]
    public decimal M109 { get; set; }

    [BinaryKey(110)]
    public Guid M110 { get; set; }

    [BinaryKey(111)]
    public DateTime M111 { get; set; }

    [BinaryKey(112)]
    public int M112 { get; set; }

    [BinaryKey(113)]
    public string M113 { get; set; } = string.Empty;

    [BinaryKey(114)]
    public double M114 { get; set; }

    [BinaryKey(115)]
    public bool M115 { get; set; }

    [BinaryKey(116)]
    public long M116 { get; set; }

    [BinaryKey(117)]
    public decimal M117 { get; set; }

    [BinaryKey(118)]
    public Guid M118 { get; set; }

    [BinaryKey(119)]
    public DateTime M119 { get; set; }

    [BinaryKey(120)]
    public int M120 { get; set; }

    [BinaryKey(121)]
    public string M121 { get; set; } = string.Empty;

    [BinaryKey(122)]
    public double M122 { get; set; }

    [BinaryKey(123)]
    public bool M123 { get; set; }

    [BinaryKey(124)]
    public long M124 { get; set; }

    [BinaryKey(125)]
    public decimal M125 { get; set; }

    [BinaryKey(126)]
    public Guid M126 { get; set; }

    [BinaryKey(127)]
    public DateTime M127 { get; set; }

    [BinaryKey(128)]
    public int M128 { get; set; }

    [BinaryKey(129)]
    public string M129 { get; set; } = string.Empty;

    [BinaryKey(130)]
    public double M130 { get; set; }

    [BinaryKey(131)]
    public bool M131 { get; set; }

    [BinaryKey(132)]
    public long M132 { get; set; }

    [BinaryKey(133)]
    public decimal M133 { get; set; }

    [BinaryKey(134)]
    public Guid M134 { get; set; }

    [BinaryKey(135)]
    public DateTime M135 { get; set; }

    [BinaryKey(136)]
    public int M136 { get; set; }

    [BinaryKey(137)]
    public string M137 { get; set; } = string.Empty;

    [BinaryKey(138)]
    public double M138 { get; set; }

    [BinaryKey(139)]
    public bool M139 { get; set; }

    [BinaryKey(140)]
    public long M140 { get; set; }

    [BinaryKey(141)]
    public decimal M141 { get; set; }

    [BinaryKey(142)]
    public Guid M142 { get; set; }

    [BinaryKey(143)]
    public DateTime M143 { get; set; }

    [BinaryKey(144)]
    public int M144 { get; set; }

    [BinaryKey(145)]
    public string M145 { get; set; } = string.Empty;

    [BinaryKey(146)]
    public double M146 { get; set; }

    [BinaryKey(147)]
    public bool M147 { get; set; }

    [BinaryKey(148)]
    public long M148 { get; set; }

    [BinaryKey(149)]
    public decimal M149 { get; set; }

    [BinaryKey(150)]
    public Guid M150 { get; set; }

    [BinaryKey(151)]
    public DateTime M151 { get; set; }

    [BinaryKey(152)]
    public int M152 { get; set; }

    [BinaryKey(153)]
    public string M153 { get; set; } = string.Empty;

    [BinaryKey(154)]
    public double M154 { get; set; }

    [BinaryKey(155)]
    public bool M155 { get; set; }

    [BinaryKey(156)]
    public long M156 { get; set; }

    [BinaryKey(157)]
    public decimal M157 { get; set; }

    [BinaryKey(158)]
    public Guid M158 { get; set; }

    [BinaryKey(159)]
    public DateTime M159 { get; set; }

    [BinaryKey(160)]
    public int M160 { get; set; }

    [BinaryKey(161)]
    public string M161 { get; set; } = string.Empty;

    [BinaryKey(162)]
    public double M162 { get; set; }

    [BinaryKey(163)]
    public bool M163 { get; set; }

    [BinaryKey(164)]
    public long M164 { get; set; }

    [BinaryKey(165)]
    public decimal M165 { get; set; }

    [BinaryKey(166)]
    public Guid M166 { get; set; }

    [BinaryKey(167)]
    public DateTime M167 { get; set; }

    [BinaryKey(168)]
    public int M168 { get; set; }

    [BinaryKey(169)]
    public string M169 { get; set; } = string.Empty;

    [BinaryKey(170)]
    public double M170 { get; set; }

    [BinaryKey(171)]
    public bool M171 { get; set; }

    [BinaryKey(172)]
    public long M172 { get; set; }

    [BinaryKey(173)]
    public decimal M173 { get; set; }

    [BinaryKey(174)]
    public Guid M174 { get; set; }

    [BinaryKey(175)]
    public DateTime M175 { get; set; }

    [BinaryKey(176)]
    public int M176 { get; set; }

    [BinaryKey(177)]
    public string M177 { get; set; } = string.Empty;

    [BinaryKey(178)]
    public double M178 { get; set; }

    [BinaryKey(179)]
    public bool M179 { get; set; }

    [BinaryKey(180)]
    public long M180 { get; set; }

    [BinaryKey(181)]
    public decimal M181 { get; set; }

    [BinaryKey(182)]
    public Guid M182 { get; set; }

    [BinaryKey(183)]
    public DateTime M183 { get; set; }

    [BinaryKey(184)]
    public int M184 { get; set; }

    [BinaryKey(185)]
    public string M185 { get; set; } = string.Empty;

    [BinaryKey(186)]
    public double M186 { get; set; }

    [BinaryKey(187)]
    public bool M187 { get; set; }

    [BinaryKey(188)]
    public long M188 { get; set; }

    [BinaryKey(189)]
    public decimal M189 { get; set; }

    [BinaryKey(190)]
    public Guid M190 { get; set; }

    [BinaryKey(191)]
    public DateTime M191 { get; set; }

    [BinaryKey(192)]
    public int M192 { get; set; }

    [BinaryKey(193)]
    public string M193 { get; set; } = string.Empty;

    [BinaryKey(194)]
    public double M194 { get; set; }

    [BinaryKey(195)]
    public bool M195 { get; set; }

    [BinaryKey(196)]
    public long M196 { get; set; }

    [BinaryKey(197)]
    public decimal M197 { get; set; }

    [BinaryKey(198)]
    public Guid M198 { get; set; }

    [BinaryKey(199)]
    public DateTime M199 { get; set; }

    [BinaryKey(200)]
    public int M200 { get; set; }
}
