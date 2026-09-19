namespace ViShap.Viper.Serialization.Tests.Fixtures;

// One enum per integral underlying type, so the rule that an enum travels as its underlying
// primitive can be pinned for each width and signedness rather than for int alone.

public enum ByteEnum : byte { Value = 0xAB }

public enum SByteEnum : sbyte { Value = -2 }

public enum ShortEnum : short { Value = -2 }

public enum UShortEnum : ushort { Value = 0x1234 }

public enum IntEnum { Value = 0x11223344 }

public enum UIntEnum : uint { Value = 0x89ABCDEF }

public enum LongEnum : long { Value = 0x1122334455667788 }

public enum ULongEnum : ulong { Value = 0x8899AABBCCDDEEFF }
