// SUPERSEDED — historical audit artifact, kept for provenance. Do not compile.
//
// These 19 probes were written against the pre-rework architecture and assert the DEFECTS as they
// existed then. The architecture audit (docs/Architecture-Audit.md) replaced that
// architecture, and every finding here is now pinned by a test that asserts the CORRECT behavior:
//
//   S01, S02, S07, S08, S09  → tests/ViShap.Viper.Serialization.Tests/Security/CryptoContractTests.cs
//   S03, S04, S05, S06, S10, S11, S12, C06, C07
//                            → tests/ViShap.Viper.Serialization.Tests/Security/HostileInputTests.cs
//   C01–C05, A01–A03         → tests/ViShap.Viper.Serialization.Tests/Correctness/TypeContractTests.cs
//
// The APIs these probes use (Encryptor, ICompressor, the global algorithm registries) no longer
// exist: enforcement of resource limits is no longer reachable from the public surface.

using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;
using ViShap.Viper.Compression;

// Audit evidence: each probe asserts the observed defect, NOT desired behavior.
// No destructive payloads, stack overflow, large allocations, or external access.
var plain = new BinarySerializer();
int confirmed = 0, unexpected = 0;
void Probe(string name, Func<string> test)
{
    try { Console.WriteLine($"CONFIRMED | {name} | {test()}"); confirmed++; }
    catch (Exception ex) { Console.WriteLine($"UNEXPECTED | {name} | {ex.GetType().Name}: {ex.Message}"); unexpected++; }
}
void Require(bool condition) { if (!condition) throw new Exception("Observation not reproduced"); }
BinarySerializer Limited(SerializationLimits limits) => new(BinarySerializerOptions.Configure().WithLimits(limits).Build());
byte[] Payload(Action<BinaryWriter> write)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    write(w); w.Flush(); return ms.ToArray();
}
byte[] Frame(byte[] body, int? declared = null) => Payload(w =>
{
    w.Write(0x52455342); w.Write(1);
    w.Write((byte)0); w.Write(false); // compression
    w.Write((byte)0); w.Write(false); // checksum
    w.Write((byte)0); w.Write(false); // encryption
    w.Write(false); w.Write(false); // key id, references
    w.Write(declared ?? body.Length); w.Write(declared ?? body.Length); w.Write(declared ?? body.Length);
    w.Write((byte)0); w.Write(body);
});

Probe("S01 AES-configured reader accepts plaintext", () =>
{
    // Вердикт ИИ - проверить.
    // Encryptor настроен ≠ «этот BinarySerializer обязан принимать только encrypted сообщения».

    // У нас есть два разных понятия:

    // Encryptor
    //     = механизм, которым serializer умеет шифровать/расшифровывать

    // Encryption в header
    //     = каким способом зашифровано КОНКРЕТНОЕ сообщение

    // Поэтому нормальная последовательность такая:

    // Deserialize(message)
    //     ↓
    // прочитать header
    //     ↓
    // Encryption = None?
    //     ├── да → читать plaintext
    //     └── нет → нужен соответствующий decryptor/key

    // То есть сценарий аудита:

    // var serializer = new BinarySerializer(
    //     options with { Encryptor = aesEncryptor });

    // serializer.Deserialize<int>(plaintextMessage);

    // сам по себе не доказывает security bypass.

    // Он доказывает только:

    // «Наличие Encryptor в конфигурации не означает RequireEncryption».

    // И это совершенно допустимо, если именно такая модель API задумана.
    // Когда S01 стал бы настоящей проблемой?

    // Если у нас было бы требование:

    // «Если Encryptor указан в options, serializer должен принимать только encrypted payload».

    // Тогда нужен отдельный policy:

    // RequireEncryption

    // и при:

    // header.Encryption == None
    // RequireEncryption == true

    // мы должны отвергать сообщение.

    // Но это уже новая feature/policy, а не автоматическое следствие существования Encryptor

    using var enc = new Encryptor(new Aes256Gcm(), RandomNumberGenerator.GetBytes(32));
    var s = new BinarySerializer(BinarySerializerOptions.Default with { Encryptor = enc });
    Require(s.Deserialize<int>(plain.Serialize(123)) == 123);
    return "123 accepted without authentication";
});
Probe("S02 AES header is unauthenticated", () =>
{
    using var enc = new Encryptor(new Aes256Gcm(), RandomNumberGenerator.GetBytes(32));
    var s = new BinarySerializer(BinarySerializerOptions.Default with { Encryptor = enc });
    byte[] bytes = s.Serialize(123);
    bytes[15] = 1; // PreserveReferences, no optional header strings
    Require(s.Deserialize<int>(bytes) == 123);
    return "PreserveReferences changed, GCM tag accepted";
});
Probe("S03 recursive collection bypasses depth", () =>
{
    var root = new Tree(); var current = root;
    for (int i = 0; i < 30; i++) { var child = new Tree(); current.Add(child); current = child; }
    var s = Limited(SerializationLimits.Default with { MaxDepth = 1 });
    var result = s.Deserialize<Tree>(s.Serialize(root));
    int depth = 0; for (var n = result; n!.Count != 0; n = n[0]) depth++;
    Require(depth == 30); return $"depth={depth}, MaxDepth=1, read and write accepted";
});
Probe("S04 collection objects bypass node budget", () =>
{
    var s = Limited(SerializationLimits.Default with { MaxObjectGraphNodes = 1 });
    var data = new List<List<int>> { new(), new(), new() };
    Require(s.Deserialize<List<List<int>>>(s.Serialize(data))!.Count == 3);
    return "4 lists accepted with MaxObjectGraphNodes=1";
});
Probe("S05 V0 ignores payload read limit", () =>
{
    var s = new BinarySerializer(BinarySerializerOptions.Configure().AllowV0Fallback()
        .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 1 }).Build());
    Require(s.Deserialize<int>(BitConverter.GetBytes(123)) == 123);
    return "4-byte payload accepted with MaxPayloadBytes=1";
});
Probe("S06 allocation precedes wire budget check", () =>
{
    var s = Limited(SerializationLimits.Default with { MaxWireBytes = 64 });
    var bytes = Frame([], 8 * 1024 * 1024);
    long start = GC.GetAllocatedBytesForCurrentThread();
    string error;
    try { s.Deserialize<int>(bytes); throw new Exception("Expected rejection"); }
    catch (Exception ex) when (ex.GetType().Name == "BinaryLimitException") { error = ex.GetType().Name; }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
    Require(allocated >= 8 * 1024 * 1024);
    return $"29-byte frame allocated {allocated} bytes before {error}; MaxWireBytes=64";
});
Probe("S07 key resolver caller buffer zeroed", () =>
{
    byte[] shared = RandomNumberGenerator.GetBytes(32);
    using var enc = new Encryptor(new Aes256Gcm(), _ => shared);
    enc.Encrypt([1, 2, 3]);
    Require(shared.All(b => b == 0));
    byte[] second = enc.Encrypt([4, 5, 6]);
    using var zero = new Encryptor(new Aes256Gcm(), new byte[32]);
    Require(zero.Decrypt(EncryptionAlgorithm.Aes256Gcm, null, null, second, 3).SequenceEqual(new byte[] { 4, 5, 6 }));
    return "second ciphertext decrypts with all-zero key when resolver reuses buffer";
});
Probe("S08 disposed encryptor remains usable", () =>
{
    byte[] supplied = RandomNumberGenerator.GetBytes(32);
    var enc = new Encryptor(new Aes256Gcm(), supplied); enc.Dispose();
    Require(supplied.All(b => b == 0));
    byte[] ciphertext = enc.Encrypt([7]);
    using var zero = new Encryptor(new Aes256Gcm(), new byte[32]);
    Require(zero.Decrypt(EncryptionAlgorithm.Aes256Gcm, null, null, ciphertext, 1)[0] == 7);
    return "Dispose mutated caller key; subsequent encryption used zero key";
});
Probe("S09 Deflate accepts truncated output", () =>
{
    var deflate = new Deflate(); byte[] source = new byte[1024];
    byte[] compressed = new byte[deflate.GetMaxCompressedLength(source.Length)];
    int length = deflate.Compress(source, compressed);
    var output = new byte[4];
    Require(deflate.Decompress(compressed.AsSpan(0, length), output) == 4);
    return "1024-byte expanded data accepted as 4-byte output";
});
Probe("S10 typed payload trailing bytes ignored", () =>
{
    Require(plain.Deserialize<int>(Frame(Payload(w => { w.Write(123); w.Write(456); }))) == 123);
    return "8-byte payload parsed as 4-byte int";
});
Probe("S11 writer materializes before limit", () =>
{
    int visits = 0;
    IEnumerable<int> Items() { for (int i = 0; i < 1000; i++) { visits++; yield return i; } }
    var s = Limited(SerializationLimits.Default with { MaxCollectionLength = 1 });
    try { s.Serialize<IEnumerable<int>>(Items()); throw new Exception("Expected rejection"); }
    catch (Exception ex) when (ex.GetType().Name == "BinaryLimitException") { }
    Require(visits == 1000); return $"visited={visits} before rejecting MaxCollectionLength=1";
});
Probe("S12 keyed fields outside total element budget", () =>
{
    // Вердикт ИИ - проверить.
    //     Аудит делает:

    // MaxTotalElements = 1

    // и затем отправляет:

    // 3 unknown keyed fields

    // которые успешно проходят.

    // Почему это не автоматически баг?

    // Потому что в нашей модели:

    // MaxTotalElements
    //     = количество data elements

    // MaxKeyedFields
    //     = количество keyed fields в одном keyed object

    // То есть:

    // 3 fields

    // не обязаны уменьшать:

    // _totalElements

    // Это две разные метрики.

    // Например:

    // [BinaryKey(1)] int A
    // [BinaryKey(2)] int B
    // [BinaryKey(3)] int C

    // Я бы не хотел, чтобы:

    // MaxTotalElements = 2

    // внезапно означал:

    // «у объекта не может быть третьего поля».

    // Это смешивает данные и структуру/schema metadata.

    // Но аудит всё-таки зацепил потенциальную проблему

    // Вот это уже важно.

    // Сейчас MaxKeyedFields — per object.

    // Представим:

    // Object 1 → 1,000 fields
    // Object 2 → 1,000 fields
    // Object 3 → 1,000 fields
    // ...
    // Object 100,000 → 1,000 fields

    // Каждый object отдельно удовлетворяет:

    // fields <= MaxKeyedFields

    // но суммарно attacker заставляет parser обработать:

    // 100,000,000 fields
    // А что ограничивает это сейчас?

    // Частично:

    // MaxObjectGraphNodes
    // MaxWireBytes
    // MaxDepth

    // Если эти лимиты правильно применяются ко всему graph, атака всё равно имеет предел.

    // Но отдельного cumulative field budget нет.

    // Поэтому есть два разных вопроса
    // Вопрос A

    // Должны ли unknown keyed fields расходовать MaxTotalElements?

    // Мой ответ: нет.

    // Это плохое смешение семантик.

    // То есть S12:

    // MaxTotalElements=1
    // +
    // 3 unknown fields

    // не является доказательством, что MaxTotalElements сломан.

    // Вопрос B

    // Нужен ли cumulative limit на число keyed fields во всей deserialize operation?

    // Вот это уже вполне разумный вопрос.

    // Можно иметь:

    // MaxKeyedFields
    //     = max fields per object

    // и отдельно:

    // MaxTotalKeyedFields
    //     = cumulative fields across whole operation

    var s = Limited(SerializationLimits.Default with { MaxTotalElements = 1 });
    var bytes = Frame(Payload(w =>
    {
        w.Write(true); w.Write7BitEncodedInt(3);
        for (int i = 0; i < 3; i++) { w.Write7BitEncodedInt(i); w.Write(0); }
    }));
    Require(s.Deserialize<EmptyContract>(bytes) is not null);
    return "3 unknown fields accepted, MaxTotalElements=1";
});
Probe("C01 ref primitive silently unchanged", () =>
{
    int value = 0; plain.Deserialize(plain.Serialize(123), ref value);
    Require(value == 0); return "ref int remains 0 instead of 123";
});
Probe("C02 collection references not preserved", () =>
{
    var s = new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build());
    var shared = new List<int> { 1 }; var root = new Lists { A = shared, B = shared };
    var result = s.Deserialize<Lists>(s.Serialize(root))!;
    Require(!ReferenceEquals(result.A, result.B)); return "shared List<int> becomes two instances";
});
Probe("C03 schema skipping breaks reference table", () =>
{
    var s = new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build());
    var shared = new Node { Value = 7 };
    var bytes = s.Serialize(new NewSchema { Removed = shared, Kept = shared });
    try { s.Deserialize<OldSchema>(bytes); throw new Exception("Expected rejection"); }
    catch (Exception ex) when (ex.GetType().Name == "BinaryFormatException" && ex.Message.Contains("not found"))
    { return ex.Message; }
});
Probe("C04 implicit polymorphism corrupts positional data", () =>
{
    byte[] bytes = plain.Serialize<Base>(new Derived { A = 11, Z = 22 });
    Require(plain.Deserialize<Base>(bytes)!.Z == 11);
    return "Base.Z=22 became 11 from Derived.A";
});
Probe("C05 BinaryIgnore plus BinaryKey leaks member", () =>
{
    var bytes = plain.Serialize(new Contradictory { Secret = "audit-secret" });
    Require(plain.Deserialize<Contradictory>(bytes)!.Secret == "audit-secret");
    return "member with BinaryIgnore was serialized because BinaryKey exists";
});
Probe("C06 stream offset charged as output bytes", () =>
{
    var s = Limited(SerializationLimits.Default with { MaxWireBytes = 40 });
    Require(s.Serialize(123).Length < 40);
    using var ms = new MemoryStream(); ms.Write(new byte[40]);
    try { s.Serialize(ms, 123); throw new Exception("Expected rejection"); }
    catch (Exception ex) when (ex.GetType().Name == "BinaryLimitException")
    { return "33-byte frame fits at offset 0 but rejected at offset 40"; }
});
Probe("C07 invalid Guid leaks framework exception", () =>
{
    try { plain.Deserialize<Guid>(Frame([1])); throw new Exception("Expected rejection"); }
    catch (ArgumentException ex) { return ex.GetType().Name; }
});
Console.WriteLine($"TOTAL confirmed={confirmed}; unexpected={unexpected}");
return unexpected == 0 ? 0 : 1;

public class Tree : List<Tree>;
public class Lists { public List<int>? A { get; set; } public List<int>? B { get; set; } }
public class Node { public int Value { get; set; } }
[BinaryContract] public class EmptyContract;
[BinaryContract] public class NewSchema
{
    [BinaryKey(1)] public Node? Removed { get; set; }
    [BinaryKey(2)] public Node? Kept { get; set; }
}
[BinaryContract] public class OldSchema { [BinaryKey(2)] public Node? Kept { get; set; } }
public class Base { public int Z { get; set; } }
public class Derived : Base { public int A { get; set; } }
[BinaryContract] public class Contradictory
{
    [BinaryKey(1), BinaryIgnore] public string? Secret { get; set; }
}
