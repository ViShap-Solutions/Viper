# ViShap.Viper — Архитектурный аудит и целевая архитектура

**Режим:** `skills/viper_auditor`
**Вход:** полный разбор `src/`, `System-Contract.md`, `audit/Problems.cs` (19 проб), `audit/Problems-Solutions.md`
**Цель релиза:** v1.0.0 · тегов нет, golden-wire файлов нет → изменения формата сейчас бесплатны
**Дата:** 2026-09-18

> **Статус: целевая архитектура реализована.** Разделы A–K ниже сохранены как диагноз и обоснование
> решений. Что фактически сделано, чем это подтверждено и что осталось — см. «Статус реализации»
> сразу после этого блока. Нормативное описание получившейся системы — `System-Contract.md` §2.

---

# Статус реализации

`src/` переписан по целевой архитектуре. Сборка чистая (0 предупреждений, 0 ошибок), тесты: **85/85**.

## Фазы плана §I

```text
[x] Фаза 0  Приёмочные тесты                 tests/.../Security, tests/.../Correctness
[x] Фаза 1  Владение ключами                 SecretKey · IKeyProvider · StaticKeyProvider · DelegateKeyProvider
[x] Фаза 2  Граница операции и байты          SerializationOperation · PhaseBudget · Metered*/WindowReadStream
[x] Фаза 3  Монополия на байты                ValueReader · ValueWriter · ElementCount
[x] Фаза 4  Инверсия обхода графа             GraphReader · GraphWriter · IScalar/ISequence/IMap/ICompositeFormatter
[x] Фаза 5  Контракт типа                     TypeContract · UnionMap
[x] Фаза 6  Фазы, политика, аутентификация    PhaseBudget · AAD заголовка · RequireEncryption/RequireChecksum · AlgorithmCatalog
[x] Фаза 7  Протокол идентичности             WriteReferenceTable · ReadReferenceTable (скоупы по цепочке предков)
[x] Фаза 8  Перф-восстановление               ref-struct depth-scope вместо аллокации на узел,
                                              потоковая запись последовательностей вместо 14 копий
                                              ToList(), ленивые таблицы идентичности, собственный
                                              курсор вместо опроса Position
```

**Фаза 8 — уточнение границы.** В плане §I она означала «убрать перф-долг, созданный защитными
правками» — это работа по коду, и она сделана. Снятие замеров BenchmarkDotNet к рефакторингу не
относится: `Benchmark-Plan.md` и `QA-Plan.md` — отдельные документы со своим циклом, они в этот
рефакторинг не входили и не менялись. Их нужно привести в соответствие с новой структурой отдельной
задачей (см. «Осталось»).

## Что удалено

```text
RawReader / RawWriter                    BinaryPayloadReader / BinaryPayloadWriter
DeserializationGuard                     BudgetedReadStream / BudgetedWriteStream / BoundedReadStream
ICompressor / IChecksumCalculator /      Compressor / ChecksumCalculator / Encryptor (публичные)
IEncryptor (из Core)                     AlgorithmResolver (публичный)
CompressionAlgorithmRegistry /           TypeAccessorCache / PolymorphicTypeCache / TypeAccessorPlan
ChecksumAlgorithmRegistry /              NestedFormatter / TypeFormatterRegistry
EncryptionAlgorithmRegistry (глобальные) IFormatCodec / IFormatCodecFactory / CodecRegistry
Encryptor.None (публичный синглтон)      IHeaderCodec / V1HeaderCodec · BinaryStreamExtensions
init-сеттеры BinarySerializerOptions     14 копий Cast<object?>().ToList() · класс DepthScope
```

## Проверка инвариантов (§ «Что должно стать инвариантом»)

| Инвариант | Проверка | Результат |
|---|---|---|
| INV-1 | `SerializationLimits` вне конфигурации/операции | 7 файлов, все — владельцы политики (было 17) |
| INV-2 | `grep -r "RawReader\|RawWriter" src/` | 0 совпадений (было 100 в 43 файлах) |
| INV-3 | единственный конструктор `ElementCount` — `Validate` | по построению, конструктор приватный |
| INV-4 | рекурсия и учёт только в `Engine/` | форматтеры: 0 ссылок на лимиты, бюджет и поток |
| INV-5 | аллокация после сверки с доступным | `ValueReader.RequireAvailable`, тест на аллокации |
| INV-6 | бюджет записи от старта операции | тест «offset 0 vs 1024» |
| INV-7 | зануляется только своя копия ключа | тесты владения ключом |
| INV-8 | публичные контракты без политики | оркестраторы internal, лимиты в пайплайне |
| INV-9 | payload потребляется ровно полностью | тест хвостовых байт, окна keyed-полей |
| INV-10 | нет глобального мутабельного состояния | реестры заменены снимком в опциях |

## Приёмка находок аудита

Каждая находка закреплена тестом; имена файлов — `tests/ViShap.Viper.Serialization.Tests/`.

```text
S01 → Security/CryptoContractTests    (capability vs RequireEncryption)
S02 → Security/CryptoContractTests    (перебор всех байт заголовка)
S03 → Security/HostileInputTests      (в т.ч. кадр 1.2 МБ: BinaryLimitException, процесс жив)
S04 → Security/HostileInputTests      S05 → Security/HostileInputTests
S06 → Security/HostileInputTests      (замер GC.GetAllocatedBytesForCurrentThread)
S07, S08 → Security/CryptoContractTests
S09 → Security/CryptoContractTests    S10, S11, S12 → Security/HostileInputTests
C01, C02, C03, C04, C05 → Correctness/TypeContractTests
C06, C07 → Security/HostileInputTests
A01, A02, A03 → Correctness/TypeContractTests
Круговой обход всех типов → Correctness/RoundTripCorpusTests (V0 и V1)
```

## Найдено и закрыто в ходе реализации (сверх аудита)

- **Классификация исчерпания источника.** Превышение настроенного потолка и физическая нехватка байт
  давали одно и то же исключение. Источник чтения теперь сам классифицирует отказ:
  `BinaryLimitException` для бюджета, `BinaryFormatException` для усечённого payload.
- **Усиление на расшифровании.** Заголовок мог объявить plaintext длиннее доставленного ciphertext.
  Расшифрование не расширяет данные — объявление сверяется с фактической длиной шифротекста до
  аллокации.

## Осталось

Вне рефакторинга `src/` — отдельные документы и задачи:

```text
[ ] QA-Plan.md привести в соответствие с System-Contract §2 и §22-23 (структура тестов уже новая)
[ ] Benchmark-Plan.md: снять базовые замеры на новой архитектуре
[ ] Release note: public breaking (см. §G), крипто-несовместимость (AAD), ужесточение приёма
```

---

---

# A. Executive verdict

```text
RELEASE-BLOCKED BY ARCHITECTURE
```

Вердикт не про количество найденных багов. Их можно закрыть за 2–3 недели патчами (см. `audit/Problems-Solutions.md`).
Вердикт про три свойства текущей структуры, которые патчами не закрываются:

**1. Точка применения политики безопасности является публичной точкой расширения.**
`ICompressor`, `IChecksumCalculator`, `IEncryptor` — публичные интерфейсы в Core, и именно их реализации
(`Compressor`, `Encryptor`, `ChecksumCalculator`) применяют `MaxPayloadBytes`, `MaxCompressedBytes`,
`MaxEncryptedBytes`. `BinarySerializerOptions` принимает их через `init`-сеттеры, минуя `Build()`:

```csharp
BinarySerializerOptions.Default with { Encryptor = myEncryptor }   // ровно так делает S01/S02
```

Любая внешняя реализация (или `with`-мутация) отключает все фазовые лимиты, и **это не баг, это
разрешённый публичный контракт**. Дополнительно `CompressionAlgorithmRegistry.Register(kind, factory)` и
`EncryptionAlgorithmRegistry.Register(kind, factory)` позволяют любой сборке в процессе подменить
встроенный `Aes256Gcm` глобально, для всех сериализаторов. Починить это = сломать публичный API.

**2. Безопасный примитив — не граница, а вежливость.**
`BinaryPayloadReader.RawReader` / `BinaryPayloadWriter.RawWriter` открыты форматтерам: **100 обращений в
43 файлах**. `DeserializationGuard` рядом, но необязателен. Любой новый форматтер по умолчанию небезопасен;
безопасность держится на памяти разработчика. Это прямое нарушение Rule 2 skill-а
(«no security-by-convention»). C07 — типичный представитель: `new Guid(RawReader.ReadBytes(16))`.

**3. Обход графа принадлежит не тому слою.**
Глубина и узлы графа считаются внутри `NestedFormatter` — частного случая, — а не в обходчике.
Поэтому `class Tree : List<Tree>` обходит `MaxDepth` полностью. Проверено в изолированном процессе:
кадр **~1.2 МБ** при **дефолтных лимитах** убивает процесс неперехватываемым `StackOverflowException`
(S03). Точечный фикс закроет коллекции, но не закроет *класс* дефекта: следующий форматтер
(`ReadOnlySequence`, кастомная коллекция, будущий `IAsyncEnumerable`) снова обойдёт учёт.

Пункты 1 и 2 — структурные: они означают, что после закрытия 19 находок система останется в том же
состоянии «безопасность по соглашению», и следующий раунд аудита найдёт новый набор тех же дефектов.
Ощущение «чистые классы превратились в спагетти» — точный диагноз: политика размазана по 17 файлам,
потому что у неё **нет владельца**.

Хорошая новость: ядро (модель форматов, версионность, каталог типов, двухуровневый контракт алгоритмов
`I*Algorithm` / оркестратор, таксономия исключений) — здоровое. Требуется **перепозиционирование границ**,
а не переписывание с нуля. Полиморфизм по тегам без имён типов на проводе — сильное решение, его надо
сохранить и подчеркнуть в документации (нет gadget-поверхности `BinaryFormatter`).

---

# B. Критические проблемы

Ниже — только те, у которых причина **архитектурная**. Локальные дефекты (S05, S09, S10, C05, C06, C07 …)
разобраны в `audit/Problems-Solutions.md` и здесь фигурируют как симптомы групп.

---

### AR-01 — Публичный контракт алгоритмов является точкой применения лимитов

```text
ID                  AR-01
Severity            Critical
Локация             Core/Compression/ICompressor.cs, Core/Checksum/IChecksumCalculator.cs,
                    Core/Crypto/IEncryptor.cs, Serialization/Compression/Compressor.cs,
                    Serialization/Crypto/Encryptor.cs, Configuration/BinarySerializerOptions.cs,
                    Configuration/AlgorithmResolver.cs
Наблюдаемое         Внешняя реализация ICompressor/IEncryptor или `options with { Encryptor = ... }`
                    полностью отключает MaxPayloadBytes/MaxCompressedBytes/MaxEncryptedBytes.
                    Проба S01 конструирует опции именно так, минуя Build().
Почему важно        Единственный барьер против decompression-bomb и crypto-expansion снимается
                    легальным использованием публичного API. Лимиты становятся рекомендацией.
Root cause          Смешаны две разные роли в одном публичном типе: «алгоритм» (чистая механика) и
                    «оркестратор фазы» (применение политики). Публичной сделана вторая.
Владелец правила    FormatPipeline (кодек) — он знает границы фаз; алгоритм их знать не должен.
Рекомендация        I*Algorithm остаются публичными. ICompressor/IChecksumCalculator/IEncryptor и их
                    реализации становятся internal. Лимиты фаз применяет PhaseBudget внутри пайплайна.
                    В опциях хранится алгоритм + ключевой провайдер, не оркестратор.
Blast radius        public breaking (Core + Serialization), wire-neutral.
```

### AR-02 — `RawReader`/`RawWriter`: сквозной обход безопасного примитива

```text
ID                  AR-02
Severity            Critical
Локация             Codec/BinaryPayloadReader.cs:18, Codec/BinaryPayloadWriter.cs:22
                    + 43 файла форматтеров (100 обращений)
Наблюдаемое         Форматтер читает счётчики и байты напрямую: RawReader.ReadInt32(),
                    RawReader.ReadBytes(16). Валидация — опциональный второй вызов.
Почему важно        Любой forgetful/новый форматтер небезопасен по умолчанию. C07 (ArgumentException
                    из Guid), S03/S04 (нет учёта), потенциально любая будущая аллокация.
Root cause          У безопасного API нет монополии на байты. «Безопасный» и «сырой» пути равнодоступны.
Владелец правила    ValueReader/ValueWriter — единственный доступ к потоку внутри payload-движка.
Рекомендация        Удалить RawReader/RawWriter. Дать типизированные checked-примитивы, включая
                    ReadExact(Span<byte>) и ReadCount(CountKind) → ElementCount.
Blast radius        internal only (форматтеры переписываются механически), wire-neutral.
```

### AR-03 — Обход графа реализован в форматтере, а не в движке

```text
ID                  AR-03
Severity            Critical
Локация             Codec/BinaryPayloadWriter.cs:60-100 (WriteNestedTracked),
                    Codec/BinaryPayloadReader.cs:191-220 (ReadNestedTracked),
                    Formatters/** (ни один контейнерный форматтер не входит в depth-scope)
Наблюдаемое         MaxDepth/MaxObjectGraphNodes применяются только к member-encoded объектам.
                    Проверено: 200 000 уровней `Tree : List<Tree>` в кадре ~1.2 МБ при дефолтных
                    лимитах → StackOverflowException, процесс убит, catch невозможен.
Почему важно        Удалённый неперехватываемый отказ сервиса на недоверенном входе.
Root cause          Обход графа (рекурсия, узлы, идентичность) — ответственность движка, а реализована
                    как побочный эффект одного из форматтеров.
Владелец правила    PayloadEngine (структурный обходчик).
Рекомендация        Инверсия управления: форматтер описывает форму (scalar/sequence/map/object) и
                    предоставляет builder; циклом, глубиной, узлами и идентичностью управляет движок.
Blast radius        internal only, wire-neutral. Меняет observable поведение лимитов (по контракту).
```

### AR-04 — Нет объекта операции: `Limits` и `Budget` растащены по слоям

```text
ID                  AR-04
Severity            High
Локация             17 файлов ссылаются на SerializationLimits/SerializationBudget;
                    Budget создаётся внутри BinaryPayloadReader/Writer (Codec/BinaryPayloadReader.cs:34,
                    Codec/BinaryPayloadWriter.cs:36); 31 обращение вида reader.Budget.Limits.X
Наблюдаемое         Каждый слой достаёт политику сам. Нет места, где видно, что учитывает одна операция.
                    Симптомы: S05 (V0 read забыли обернуть payload-бюджетом при том, что write обёрнут),
                    C06 (бюджет записи считает абсолютную позицию потока), S06 (аллокация до проверки),
                    дублирующая валидация limits.Validate() в ~10 конструкторах и хелперах.
Почему важно        Нельзя доказать полноту: «все фазы одной операции учтены» не проверяемо структурно.
Root cause          Отсутствует граница операции. Конфигурация используется как глобальная переменная,
                    бюджет — как локальная деталь движка.
Владелец правила    SerializationOperation — внутренний per-call объект (Limits + Budget + PhaseBudget).
Рекомендация        Операцию создаёт BinarySerializer, передаёт в пайплайн; ниже никто не конструирует
                    ни Budget, ни лимиты и не вызывает Validate().
Blast radius        internal only, wire-neutral.
```

### AR-05 — Владение памятью и ключами не выражено в типах

```text
ID                  AR-05
Severity            High
Локация             Crypto/Encryptor.cs:66-124 (ZeroMemory на чужом буфере),
                    Crypto/Encryptor.cs:215-225 (Dispose обнуляет массив вызывающего),
                    Crypto/Encryptor.cs:41 (Encryptor.None — процессный синглтон, тоже IDisposable)
Наблюдаемое         S07: ключ, возвращённый резолвером, обнуляется после первого использования —
                    вторая операция шифрует нулевым ключом (проба расшифровывает new byte[32]).
                    S08: Dispose уничтожает ключ вызывающего; disposed-объект продолжает работать.
Почему важно        Тихая деградация до известного ключа = полная потеря конфиденциальности.
Root cause          byte[] как валюта между фазами: владение описано комментарием, а не типом.
Владелец правила    SecretKey (владеющая копия, IDisposable) + IKeyProvider.
Рекомендация        Резолвер возвращает материал → движок делает владеющую копию → зануляет копию.
                    Dispose зануляет только собственные копии. Encryptor.None неуничтожим.
Blast radius        public breaking (сигнатуры ключевого API), wire-neutral.
```

### AR-06 — Глобальные мутабельные реестры как якорь доверия

```text
ID                  AR-06
Severity            High
Локация             Compression/CompressionAlgorithmRegistry.cs:17-27,
                    Crypto/EncryptionAlgorithmRegistry.cs:16-27,
                    Formatters/TypeFormatterRegistry.cs:104-108 (ConcurrentBag Custom + Cache)
Наблюдаемое         Register(kind, factory) публичен и перезаписывает встроенные алгоритмы для всего
                    процесса. Любая сборка может подменить Aes256Gcm у всех сериализаторов.
                    Кастомные форматтеры лежат в ConcurrentBag → порядок разрешения недетерминирован,
                    а Cache уже может содержать результат, посчитанный до регистрации.
Почему важно        Якорь доверия (какой код реально шифрует) изменяем извне и вне конфигурации.
                    Недетерминированное разрешение форматтеров = невоспроизводимый wire.
Root cause          Реестр механизмов сделан процессным состоянием вместо части конфигурации.
Владелец правила    Конфигурация (BinarySerializerOptions) + immutable snapshot в операции.
Рекомендация        Убрать перезапись встроенных. Кастомные регистрации — через builder, снимок в
                    опциях; разрешение детерминировано порядком регистрации.
Blast radius        public breaking, wire-neutral.
```

### AR-07 — Протокол идентичности ссылок не определён как протокол

```text
ID                  AR-07
Severity            High
Локация             Codec/BinaryPayloadWriter.cs:60-100, Codec/BinaryPayloadReader.cs:191-220,
                    Codec/BinaryPayloadReader.cs:270-340 (keyed-поля)
Наблюдаемое         C02: разделяемый List<int> распадается на два экземпляра.
                    C03: пропуск неизвестного keyed-поля ломает таблицу ссылок (BinaryFormatException
                    «not found»), т.е. PreserveReferences и schema-evolution несовместимы, и сбой
                    зависит от данных. A02: у boxed-структур пишется мёртвый reference-кадр, из-за
                    которого ref-перегрузка читает мусор.
Почему важно        Две заявленные фичи V1 взаимно несовместимы, и это выясняется в проде.
Root cause          Нет описанного протокола: кто регистрирует id, когда (до/после детей), какова
                    область видимости id.
Владелец правила    ReferenceScope внутри PayloadEngine.
Рекомендация        Стек скоупов: id виден только по цепочке предков; регистрация до чтения детей
                    (builder-протокол); value-типы не участвуют.
Blast radius        wire-format breaking под PreserveReferences (до v1.0.0 — бесплатно).
```

### AR-08 — Асимметрия плана типа между записью и чтением

```text
ID                  AR-08
Severity            High
Локация             Codec/BinaryPayloadWriter.cs:104-130 (план от value.GetType()),
                    Codec/BinaryPayloadReader.cs:236-250 (план от declaredType),
                    Cache/TypeAccessorCache.cs:85-120
Наблюдаемое         C04: Serialize<Base>(new Derived{A=11,Z=22}) → читается Z==11, молча.
                    A03: Serialize<object>(x) пишет члены runtime-типа, читается пустой object.
                    A01: populate-in-place для List<int> даёт Count=0, Capacity=3.
                    C05: [BinaryKey] + [BinaryIgnore] → член попадает на провод вопреки Ignore.
Почему важно        Четыре пути тихой порчи данных без единого исключения.
Root cause          План строится из двух разных источников на двух сторонах; контракт типа не
                    валидируется как единое целое в одном месте.
Владелец правила    TypeContract (единый снимок: члены, ключи, union-карта, режим) — строится один раз.
Рекомендация        Запись использует declared-план + union-дискриминатор; несоответствие runtime без
                    union → BinaryTypeException; populate-in-place разрешён только для object-формы.
Blast radius        public behavior breaking (раньше «работало» молча), wire-neutral.
```

### AR-09 — Двухфазность кадра не реализована: аллокация опережает проверку

```text
ID                  AR-09
Severity            High
Локация             Codec/V1FormatCodec.cs:107 (ReadExactly по header.OnDiskLength),
                    Security/DeserializationGuard.cs:233 (new byte[length] до чтения),
                    Security/DeserializationGuard.cs:172-190 (ReadValidatedBytes для строк/блобов)
Наблюдаемое         S06: 29-байтовый кадр, объявляющий 8 МБ, аллоцирует 8 МБ и только потом падает.
                    Коэффициент усиления ~290 000×; при дефолтах один кадр = 64 МБ аллокации.
Почему важно        Дешёвый remote-amplification на любой публичной точке входа.
Root cause          Объявленный размер не сверяется с физически доступным до аллокации.
Владелец правила    FrameValidator (фаза 1 кадра) + ValueReader (доступные байты окна).
Рекомендация        Каждый поток чтения знает Remaining; любой declared-размер сверяется с Remaining
                    до аллокации; заголовок валидируется до создания payload-объекта.
Blast radius        internal only, wire-neutral.
```

### AR-10 — Три потоковые обёртки с пересекающимися ролями

```text
ID                  AR-10
Severity            Medium
Локация             Security/BudgetedReadStream.cs, Security/BudgetedWriteStream.cs,
                    Security/BoundedReadStream.cs
Наблюдаемое         BudgetedWriteStream считает абсолютные позиции (C06) и знает про keyed-патчинг
                    (Position/Seek/SetLength — три разные ветки бюджета). BudgetedReadStream знает
                    Remaining, но никому его не сообщает (AR-09). BoundedReadStream — единственный
                    честный «оконный» поток, и он же единственный не-seekable.
Почему важно        Дублирование арифметики лимитов и три разных ответа на вопрос «сколько осталось».
Root cause          Обёртки написаны по мере появления находок, а не спроектированы как два механизма.
Владелец правила    Слой физических байтов (2 механизма: счётчик операции и окно).
Рекомендация        ByteMeter (операционно-относительный счётчик, read+write) + ByteWindow (окно
                    с Remaining). Обе реализуют IRemainingBytes.
Blast radius        internal only, wire-neutral.
```

### AR-11 — Канонизация кадра не завершена

```text
ID                  AR-11
Severity            Medium
Локация             Codec/V1FormatCodec.cs:160-170 (нет проверки полного потребления payload),
                    Compression/Deflate.cs:26-45 (нет проверки избыточного выхода)
Наблюдаемое         S10: [123][456] читается как 123. S09: данные, реально распаковывающиеся в 1024
                    байта, принимаются как валидные 4 байта.
Почему важно        Format confusion: два читателя расходятся в интерпретации одних и тех же байт.
Root cause          Правило «ровно столько, сколько объявлено» применено к keyed-полям и не применено
                    к корню и к распаковке.
Владелец правила    FormatPipeline (корень) + PhaseBudget (распаковка).
Рекомендация        Полное потребление payload на корне V1; exact-output контракт для распаковки.
Blast radius        wire-strictness breaking (ранее принимаемый мусор станет ошибкой), поведенчески — да.
```

### AR-12 — Перф-долг, созданный защитными правками

```text
ID                  AR-12
Severity            Medium
Локация             Security/SerializationBudget.cs:47-70 (класс DepthScope — аллокация на каждый
                    вложенный объект), Formatters/** (14 × Cast<object?>().ToList()),
                    Security/BudgetedWriteStream.cs:100-115 (обращение к _inner.Position на каждую
                    запись), Codec/BinaryPayloadWriter.cs:13-19 (два словаря аллоцируются всегда,
                    даже при PreserveReferences=false), Compression/Deflate.cs:29 (source.ToArray())
Наблюдаемое         Защита добавила аллокацию на узел, полную материализацию на контейнер и лишние
                    копии на фазу.
Почему важно        v1.0 позиционируется как быстрый бинарный сериализатор; это регресс по дизайну.
Root cause          Механизмы защиты добавлялись как объекты, а не как структуры/операции.
Владелец правила    PayloadEngine (scope как readonly ref struct), ValueWriter (потоковая запись).
Рекомендация        ref struct depth-scope; ленивое создание таблиц идентичности; счётчик байт
                    внутри обёртки вместо опроса позиции; потоковая запись последовательностей.
Blast radius        internal only, wire-neutral.
```

---

# C. Диагноз архитектуры

## C.1 Текущий граф зависимостей (упрощённо)

```text
                        BinarySerializerOptions ──────────────┐
                           (public, init-сеттеры)             │ Limits
                                   │                          │
                                   ▼                          ▼
   BinarySerializer ──► BinaryFormatRouter ──► V0/V1FormatCodec ──► Compressor  (public, Limits)
                                                    │                Encryptor   (public, Limits)
                                                    │                Checksum    (public)
                                                    │
                                                    ├──► BinaryFormatHeaderV1   (Limits)
                                                    ├──► Budgeted/BoundedStream (Limits)
                                                    │
                                                    └──► BinaryPayloadReader/Writer ── создаёт Budget
                                                                │      │
                                                                │      └── RawReader / RawWriter ──┐
                                                                ▼                                  │
                                                          ITypeFormatter × 60 ─────────────────────┤
                                                                │                                  │
                                                                ├── DeserializationGuard (Limits)  │
                                                                └── прямой доступ к потоку ────────┘
```

Наблюдение: `SerializationLimits` — это **сквозная глобальная переменная**, присутствующая на всех семи
уровнях. `SerializationBudget` создаётся на шестом уровне тем же классом, который его потребляет.
Форматтеры имеют два равнодоступных пути к байтам.

## C.2 Текущий поток безопасности

```text
физические байты      MaxWireBytes      → BudgetedRead/WriteStream (write: абсолютная позиция — C06)
                      MaxPayloadBytes   → write: есть; V0 read: НЕТ (S05); V1 read: косвенно
фазы                  MaxCompressed/    → Compressor/Encryptor (публично заменяемы — AR-01)
                      MaxEncrypted
объявленные размеры   OnDiskLength и др → валидируются ПОСЛЕ аллокации (S06)
семантические счётчики MaxArray/…       → DeserializationGuard, вызов опционален (AR-02)
обход графа           MaxDepth/Nodes    → только NestedFormatter (S03/S04)
идентичность          PreserveReferences→ только NestedFormatter (C02/C03)
контракт типа         план членов       → запись: runtime-тип; чтение: declared-тип (C04/A01/A03)
канонизация           полнота чтения    → только keyed-поля (S10)
```

Из девяти строк таблицы **шесть** имеют либо необязательное, либо частичное, либо заменяемое извне
применение. Это и есть ответ на вопрос skill-а: нет, сейчас система безопасна не архитектурой, а
дисциплиной.

## C.3 Ответственности Reader/Writer/Formatter сегодня

| Класс | Реально делает | Должен делать |
|---|---|---|
| `BinaryPayloadReader` | доступ к байтам, обход графа, идентичность, keyed-разбор, план типа, трассировка, создание Budget, ref/existing-перегрузки, сырой поток наружу | безопасные примитивы чтения |
| `BinaryPayloadWriter` | то же + 8 методов валидации + 7-bit кодирование + keyed back-patching | безопасные примитивы записи |
| `ITypeFormatter` (×60) | кодек типа **+** счётчики **+** аллокации **+** конструирование **+** доступ к сырому потоку | только кодек типа |
| `DeserializationGuard` | валидация + чтение + материализация массивов + строки + skip | ничего (его функции расходятся по ValueReader и движку) |

`BinaryPayloadReader` — god object: 7 несвязанных ответственностей. `DeserializationGuard` — переходная
абстракция: половина методов должна стать примитивами ValueReader, половина — частью движка.

## C.4 Горячие точки связанности

```text
SerializationLimits          → 17 файлов (конфигурация утекла всюду)
reader.Budget.Limits.X       → 31 обращение (форматтер знает конкретную политику)
RawReader / RawWriter        → 100 обращений в 43 файлах (обход границы)
Cast<object?>().ToList()     → 14 файлов (дублированная материализация)
limits.Validate()            → ~10 вызовов (дублированная валидация конфигурации)
```

## C.5 Проблемы публичных точек расширения

| Точка | Проблема |
|---|---|
| `ICompressor` / `IChecksumCalculator` / `IEncryptor` | публичны и являются точкой применения лимитов (AR-01) |
| `BinarySerializerOptions.{Compressor,Checksum,Encryptor}` | `init`-сеттеры обходят `Build()` и всю его валидацию |
| `*AlgorithmRegistry.Register(kind, …)` | глобальная перезапись встроенной криптографии (AR-06) |
| `IEncryptionAlgorithm` | нет AAD → аутентификация заголовка невозможна без ломки контракта (S02) |
| `IEncryptor.Encrypt(byte[])` | `byte[]`-валюта: лишние копии, неопределённое владение (AR-05) |
| `ITypeFormatter` | `internal`, т.е. заявленная в CLAUDE.md расширяемость форматтерами отсутствует |
| `AlgorithmResolver` | публичный статический класс, тиражирующий конструирование политики |

Вывод: публично то, что должно быть внутренним (оркестраторы политики), и внутренне то, что имело бы
смысл сделать публичным позже (форматтеры). Ровно инверсия.

## C.6 Целевой граф зависимостей

```text
┌─────────────────────────────────────────────────────────────────────┐
│ Public API      BinarySerializer · StreamExtensions · атрибуты      │
│                 SerializationLimits · исключения · I*Algorithm      │
└───────────────────────────────┬─────────────────────────────────────┘
                                │ создаёт ровно один раз на вызов
┌───────────────────────────────▼─────────────────────────────────────┐
│ SerializationOperation (internal)                                    │
│   Limits (immutable snapshot) · Budget · PhaseBudget · ключи         │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────────┐
│ FormatPipeline  (V0/V1 codec)                                        │
│   кадрирование · порядок фаз · заголовок · фазовые лимиты            │
│   ByteMeter / ByteWindow (физические байты)                          │
└───────────────────────────────┬─────────────────────────────────────┘
                                │ передаёт ValueReader/ValueWriter
┌───────────────────────────────▼─────────────────────────────────────┐
│ PayloadEngine   обход графа · глубина · узлы · ReferenceScope        │
│                 TypeContract · keyed-разбор · канонизация            │
└───────────────────────────────┬─────────────────────────────────────┘
                                │ вызывает по форме
┌───────────────────────────────▼─────────────────────────────────────┐
│ Formatters      только кодирование типа. Нет лимитов, нет потока,    │
│                 нет циклов по элементам, нет аллокаций по счётчику   │
└─────────────────────────────────────────────────────────────────────┘

Алгоритмы (I*Algorithm) — листья, вызываются только пайплайном, о лимитах не знают.
Направление зависимостей строго вниз. Ни один нижний слой не знает SerializationLimits.
```

## C.7 Сравнение архитектурных альтернатив

Для каждого сквозного механизма рассматривались минимум три варианта. Ниже — решения в формате
Decision / Reason / Alternative / Why rejected / Blast radius.

### Решение 1 — где живёт состояние операции

```text
Decision     Узкий SerializationOperation (Limits + Budget + PhaseBudget + Keys), internal,
             создаётся публичным API, инжектируется на границе пайплайна и движка.
             В сигнатуры форматтеров НЕ попадает.
Reason       Даёт единственное место, где видно потребление одной операции, и делает
             проверяемым утверждение «все фазы учтены». Однопоточен по построению.
Alternative  (a) Оставить как есть: каждый слой достаёт Limits из конфигурации.
             (b) God-context: единый SerializationContext с опциями, механизмами и потоками,
                 передаваемый в том числе форматтерам.
             (c) AsyncLocal/статический амбиентный контекст.
Why rejected (a) — источник AR-04: полноту доказать нельзя, симптомы S05/C06/S06.
             (b) — нарушает Rule 4: форматтер получает доступ к политике и потокам, т.е.
                 возвращает ровно ту связанность, которую убираем.
             (c) — нарушает Rule 3: невидимое состояние, ломается на параллелизме и
                 вложенных вызовах, недоказуемое время жизни.
Blast radius internal only, wire-neutral.
```

### Решение 2 — как защитить чтение байтов

```text
Decision     Монополия ValueReader/ValueWriter + типизированный ElementCount.
Reason       Единственный способ выполнить Rule 2: не «форматтер обязан вызвать проверку»,
             а «непроверенного примитива не существует, а длину цикла нельзя выразить int-ом».
Alternative  (a) Оставить DeserializationGuard как рекомендованный путь и задокументировать.
             (b) Анализатор Roslyn, запрещающий RawReader в форматтерах.
             (c) Аудит на code review.
Why rejected (a) — security-by-convention; 100 существующих обращений доказывают, что так
                 не работает.
             (b) — полезно как дополнительный гейт, но анализатор отключается и не покрывает
                 «прочитал счётчик и выделил массив»; как единственный барьер недостаточен.
             (c) — не механизм.
Blast radius internal only (60 форматтеров, механическая замена), wire-neutral.
```

### Решение 3 — кто ведёт обход графа

```text
Decision     Инверсия управления: движок владеет циклом, глубиной, узлами и идентичностью;
             форматтер предоставляет форму и builder.
Reason       Единственный вариант, в котором «новый форматтер забыл про depth/nodes»
             невыразимо. Побочно устраняет S11 и 14 копий материализации.
Alternative  (a) BudgetScope-метка на ITypeFormatter (как в audit/Problems-Solutions.md §1.1):
                 движок сам открывает scope, но цикл остаётся у форматтера.
             (b) Декоратор над каждым форматтером, открывающий scope.
             (c) Проверка глубины по стеку вызовов/счётчику в ValueReader.
Why rejected (a) — минимальный по диффу и закрывает S03/S04, но оставляет у форматтера
                 цикл, аллокацию и регистрацию идентичности, т.е. C02/C03/S11 остаются
                 «по договорённости». Годится как промежуточный шаг фазы 4, не как цель.
             (b) — декоратор не видит элементы и не может зарегистрировать builder до чтения
                 детей, т.е. протокол идентичности всё равно не закрывается.
             (c) — глубина рекурсии CLR не равна семантической глубине графа; даёт ложные
                 срабатывания и не считает узлы.
Blast radius internal only, wire-neutral (кроме идентичности — см. решение 6).
```

### Решение 4 — где применяются фазовые лимиты

```text
Decision     PhaseBudget внутри FormatPipeline; оркестраторы становятся internal;
             публичны только I*Algorithm.
Reason       Барьер должен быть структурно неустраним: алгоритм вызывается внутри барьера,
             а не вместо него.
Alternative  (a) Оставить публичные ICompressor/IEncryptor и документировать обязанность
                 внешней реализации соблюдать лимиты.
             (b) Обернуть пользовательский ICompressor внутренним декоратором-ограничителем.
             (c) Валидировать только результат работы пользовательского оркестратора.
Why rejected (a) — AR-01: политика в точке расширения; документация не является барьером.
             (b) — декоратор не видит промежуточных аллокаций внутри чужой реализации:
                 бомба разворачивается до того, как декоратор увидит результат.
             (c) — то же самое: пик памяти уже достигнут.
Blast radius public breaking, wire-neutral.
```

### Решение 5 — потоковые обёртки

```text
Decision     Два механизма: ByteMeter (операционно-относительный счётчик, чтение и запись)
             и ByteWindow (окно фиксированной длины с Remaining). Оба дают Remaining.
Reason       Физические байты — единственное, что можно защитить на уровне потока; смысла
             данных там нет. Два механизма закрывают ровно две разные задачи.
Alternative  (a) Сохранить три текущих класса, починив C06 точечно.
             (b) Отказаться от обёрток и считать байты в пайплайне вручную.
             (c) Один универсальный класс с флагами режима.
Why rejected (a) — AR-10: дублирование арифметики и три разных ответа про «сколько осталось».
             (b) — теряется защита на уровне ввода-вывода: любой прямой Read мимо счётчика.
             (c) — boolean-параметры, скрывающие семантику (прямо запрещено §13 skill-а).
Blast radius internal only, wire-neutral.
```

### Решение 6 — область видимости идентичности ссылок

```text
Decision     Стек скоупов: id виден только по цепочке предков; регистрация до чтения детей.
Reason       Единственный вариант, при котором пропуск неизвестного keyed-поля не может
             породить висячую ссылку, а циклы к предкам продолжают работать.
Alternative  (a) Глобальная таблица id (как сейчас) + внятное сообщение об ошибке.
             (b) Независимый скоуп на каждое keyed-поле.
             (c) Запретить сочетание PreserveReferences и [BinaryContract].
Why rejected (a) — сбой остаётся зависящим от данных: проявляется в проде на конкретном графе.
             (b) — ломает циклы к предкам: A.Field1 = B, B.Parent = A даёт бесконечную
                 рекурсию на записи.
             (c) — удаляет работающую функциональность и противоречит заявленным возможностям V1.
Blast radius wire-format breaking под PreserveReferences (до v1.0.0 — бесплатно).
```

### Решение 7 — аутентификация метаданных заголовка

```text
Decision     AAD = канонический снимок заголовка без OnDiskLength; AAD не хранится на проводе.
Reason       Заголовок становится неподделываемым при AEAD, при этом байты кадра не меняются
             и обратная совместимость формата (не крипто) сохраняется.
Alternative  (a) Включить в AAD весь заголовок целиком, включая OnDiskLength.
             (b) Вынести MAC заголовка в отдельное поле кадра.
             (c) Подписывать заголовок отдельным ключом.
Why rejected (a) — циклическая зависимость: OnDiskLength известен только после шифрования.
                 Он самопроверяем: неверное значение ломает тег или обрывает чтение.
             (b) — новое поле = новая версия кадра и вторая крипто-примитивная зависимость.
             (c) — второй ключ без реальной модели угроз; усложняет конфигурацию.
Blast radius public additive (default-методы интерфейса), крипто-совместимость breaking.
```

---

# D. Целевая архитектура: кто чем владеет

| Компонент | Владеет | Явно НЕ владеет |
|---|---|---|
| **Core contracts** | атрибуты, enum-ы алгоритмов, `I*Algorithm`, иерархия исключений | лимитами, бюджетом, политикой, оркестрацией |
| **Options/configuration** | неизменяемый снимок: алгоритмы, лимиты, версия записи, политики (`RequireEncryption`), провайдер ключей, кастомные регистрации | исполнением, состоянием операции |
| **SerializationOperation** | `Limits` (снимок) + `Budget` + `PhaseBudget` + разрешённые ключи; время жизни = один публичный вызов | форматом, типами, потоками |
| **Resource policy (`SerializationLimits`)** | значения и их валидация — **один раз**, при построении опций | подсчётом |
| **`SerializationBudget`** | накопленные элементы, узлы, keyed-поля, глубина | лимитами как конфигурацией |
| **`PhaseBudget`** | payload / compressed / encrypted / wire | семантическими счётчиками |
| **Security boundary** | `ByteMeter`, `ByteWindow`, `SecretKey`, `FrameValidator` | смыслом данных |
| **Router** | выбор версии по магии/политике фолбэка | семантикой кодека |
| **FormatPipeline (codec)** | кадрирование, порядок фаз, вызов заголовка, применение фазовых лимитов, канонизация корня | обходом графа, типами |
| **Header** | разбор/запись метаданных, инварианты заголовка, построение AAD | аллокацией payload, ресурсами |
| **PayloadEngine** | обход, глубина, узлы, идентичность, keyed-разбор, план типа, полнота потребления | байтовым вводом-выводом, лимитами фаз |
| **`ValueReader`/`ValueWriter`** | все обращения к байтам; checked-примитивы; `ElementCount` | смыслом типа, обходом |
| **Formatter** | отображение типа ↔ последовательность примитивов/элементов | лимитами, бюджетом, потоком, циклом по элементам |
| **Algorithm** | чистая механика сжатия/хеша/шифра над span-ами | размерами политики, кадрированием |
| **Provider/registry** | разрешение механизма по виду/имени из снимка конфигурации | политикой payload, глобальным состоянием |

---

# E. Модель лимитов и бюджета

| Лимит / состояние | Область | Владелец применения | Проверяется до | Кумулятивный | Потребитель |
|---|---|---|---|---|---|
| `MaxWireBytes` | операция (относительно старта) | `ByteMeter` | каждой физической операции ввода-вывода | да | пайплайн |
| `MaxPayloadBytes` | фаза | `PhaseBudget` | создания payload-буфера | нет | пайплайн |
| `MaxCompressedBytes` | фаза | `PhaseBudget` | вызова алгоритма | нет | пайплайн |
| `MaxEncryptedBytes` | фаза | `PhaseBudget` | вызова алгоритма | нет | пайплайн |
| объявленные длины кадра | кадр | `FrameValidator` | любой аллокации (сверка с `Remaining`) | нет | пайплайн |
| `MaxDepth` | операция | `PayloadEngine` | входа в структуру | стек | движок |
| `MaxObjectGraphNodes` | операция | `PayloadEngine` | материализации узла | да | движок |
| `MaxTotalElements` | операция | `ValueReader.ReadCount` | цикла по элементам | да | движок |
| `MaxArrayLength` | значение | `ValueReader.ReadCount(Array)` | аллокации/цикла | нет | движок |
| `MaxCollectionLength` | значение | `ValueReader.ReadCount(Collection)` | цикла | нет | движок |
| `MaxDictionaryEntries` | значение | `ValueReader.ReadCount(Dictionary)` | цикла | нет | движок |
| `MaxStringBytes` | значение | `ValueReader.ReadString` | аллокации | нет | примитив |
| `MaxByteBlobBytes` | значение | `ValueReader.ReadBlob` | аллокации | нет | примитив |
| `MaxKeyedFields` | объект | `PayloadEngine` (keyed) | цикла по полям | нет | движок |
| `MaxTotalKeyedFields` *(новый)* | операция | `SerializationBudget` | цикла по полям | да | движок |

**Как новый форматтер защищается автоматически.** Три механических барьера, ни один не требует памяти
разработчика:

1. У форматтера **нет** доступа к потоку — только `ValueReader`/`ValueWriter`. Забыть проверку нельзя,
   потому что непроверенного примитива не существует.
2. Счётчик нельзя «просто прочитать»: `ReadCount(CountKind)` возвращает `ElementCount` — тип, который
   единственный принимается движком в качестве длины цикла. `int` туда не подходит по компиляции.
3. Форматтер **не управляет циклом**. Для последовательностей/словарей/объектов он реализует
   builder-контракт, а цикл, глубину, узлы и идентичность исполняет движок. «Форматтер забыл войти в
   depth-scope» невозможно, потому что depth-scope не его.

Скалярный форматтер (Guid, DateTime) не может обойти защиту, так как читает только фиксированные
примитивы, у которых нет управляемой злоумышленником длины.

---

# F. Контракт исключений

| Условие | Исключение | Граница трансляции | InnerException |
|---|---|---|---|
| Некорректная конфигурация лимитов/опций | `BinaryConfigurationException` | построение опций (один раз) | — |
| Алгоритм без AEAD при `RequireEncryption` | `BinaryConfigurationException` | построение опций | — |
| Магия/версия не распознаны | `BinaryFormatException` / `BinaryFormatNotSupportedException` | Router | — |
| Обрыв заголовка/payload | `BinaryFormatException` | Header / `ValueReader` | `EndOfStreamException` |
| Некорректный 7-bit varint, маркер, отрицательная длина | `BinaryFormatException` | `ValueReader` | — |
| Объявленный размер > физически доступного | `BinaryFormatException` | `FrameValidator` / `ValueReader` | — |
| Объявленный размер > настроенного максимума | `BinaryLimitException` | `PhaseBudget` / `ValueReader` | — |
| Превышение depth / nodes / elements / keyed-полей | `BinaryLimitException` | `PayloadEngine` | — |
| Хвостовые байты после корня; избыточный выход распаковки | `BinaryFormatException` | `FormatPipeline` | — |
| Битый сжатый поток | `BinaryFormatException` | `PhaseBudget` | `InvalidDataException` |
| Несовпадение контрольной суммы | `BinaryIntegrityException` | `FormatPipeline` | — |
| Провал AEAD-тега / неверный ключ на этапе аутентификации | `BinaryIntegrityException` | `FormatPipeline` | `CryptographicException` |
| Незашифрованный вход при `RequireEncryption` | `BinaryIntegrityException` | `FormatPipeline` | — |
| Ключ недоступен / несовпадение `keyId` | `BinaryEncryptionKeyException` | `IKeyProvider` | — |
| Прочий операционный сбой шифрования | `BinaryEncryptionException` | `FormatPipeline` | по ситуации |
| Неизвестная версия или алгоритм | `BinaryFormatNotSupportedException` | Router / Header | — |
| Неверный контракт типа, union, runtime≠declared, цикл без `PreserveReferences`, populate-in-place неприменим | `BinaryTypeException` | `TypeContract` / `PayloadEngine` | — |
| Ввод-вывод нижележащего потока | `BinaryStreamException` | `ByteMeter` / `ByteWindow` | `IOException` |
| Использование после `Dispose` | `ObjectDisposedException` | публичный тип | — |
| `null` в обязательном публичном аргументе | `ArgumentNullException` | публичный API | — |
| Требуется seekable-поток | `NotSupportedException` | Router / Inspector / `GraphWriter` (keyed-запись) | — |

Изменения против текущего состояния: три ранее «протекавших» случая (`ArgumentException` из `Guid`,
`ArgumentOutOfRangeException` из `Int128/UInt128`, `EndOfStreamException` из keyed-длины) закрываются
автоматически, потому что фиксированные чтения станут примитивом `ValueReader`.

---

# G. Аудит публичного API и точек расширения

| Изменение | Класс изменения |
|---|---|
| `ICompressor` / `IChecksumCalculator` / `IEncryptor` → `internal` | **public breaking** |
| `Compressor` / `ChecksumCalculator` / `Encryptor` → `internal` | **public breaking** |
| `AlgorithmResolver` → `internal` | **public breaking** |
| `BinarySerializerOptions.{Compressor,Checksum,Encryptor}` → `{CompressionAlgorithm, ChecksumAlgorithm, EncryptionAlgorithm, KeyProvider}` | **public breaking** |
| `init`-сеттеры опций → закрыты; единственный путь — `Configure()…Build()` | **public breaking** |
| `*AlgorithmRegistry.Register(kind, factory)` (перезапись встроенных) → удалить | **public breaking** |
| `*AlgorithmRegistry.RegisterCustom` → переносится в builder (снимок в опциях) | **public breaking** |
| `IEncryptionAlgorithm`: AAD-перегрузки + `AuthenticatesAssociatedData` (default impl) | **public additive** |
| `IKeyProvider` + `SecretKey` | **public additive** |
| `SerializationLimits.MaxTotalKeyedFields` | **public additive** |
| `RequireEncryption` / `RequireChecksum` | **public additive** |
| `ITypeFormatter` остаётся `internal` в v1.0 | internal only |
| `ValueReader`/`ValueWriter`, `PayloadEngine`, `SerializationOperation`, `ByteMeter`, `ByteWindow` | internal only |
| V1-кадр: байты не меняются (AAD не хранится) | wire-format additive |
| Ранее выпущенные **зашифрованные** payload перестают аутентифицироваться | wire-format breaking (крипто) |
| Кадрирование ссылок при `PreserveReferences` | wire-format breaking |
| Строгость: хвостовые байты, exact-output распаковки | wire-strictness breaking |

Ответы на обязательные вопросы skill-а §6 для целевого состояния:

1. Внешний реализатор `I*Algorithm` не видит ни одного internal-типа — только span-ы и enum.
2. В публичных контрактах не остаётся политики сериализатора: размеры проверяет пайплайн.
3. Новый механизм безопасности не заставляет менять существующие реализации алгоритмов (AAD добавлен
   как default-метод, отказ от AAD — осознанная деградация, видимая через `AuthenticatesAssociatedData`).
4. V2-кодек не трогает контракты алгоритмов и форматтеров (см. §H, сценарий A).
5. Классификация ошибок алгоритмом: остаётся `BinaryFormatException` для битого входа, всё остальное
   классифицирует пайплайн; алгоритму не нужно знать про лимиты.
6. Владение выражено типами: `SecretKey` владеет копией, span-ы не владеют ничем, буферы фаз
   принадлежат пайплайну.
7. Потокобезопасность: `BinarySerializer` и опции — потокобезопасны на чтение; `SerializationOperation`
   строго однопоточна и живёт внутри вызова; глобального мутабельного состояния не остаётся.

---

# H. Ключевые изменения: BEFORE / AFTER / WHY

### H.1 Граница операции

```csharp
// BEFORE — каждый слой достаёт политику сам, бюджет рождается в движке
public BinaryPayloadReader(BinaryReader reader, bool preserveReferences = false,
    SerializationLimits? limits = null, bool enableTrace = false, bool keyedContracts = false)
{
    var actualLimits = limits ?? SerializationLimits.Default;
    actualLimits.Validate();                       // 10-й раз за операцию
    Budget = new SerializationBudget(actualLimits);
}
```

```csharp
// AFTER — операция создаётся один раз на публичный вызов и передаётся вниз
internal sealed class SerializationOperation
{
    public SerializationLimits Limits { get; }     // уже провалидированный снимок
    public SerializationBudget Budget { get; }     // семантический учёт
    public PhaseBudget Phases { get; }             // payload/compressed/encrypted/wire
    public IKeyProvider Keys { get; }
}

// BinarySerializer.Deserialize<T>(Stream source)
using var operation = _options.BeginOperation();   // единственное место создания
return _router.Resolve(source, operation).Read<T>(source, operation);
```

**WHY.** Появляется место, где видно всё потребление одной операции. Исчезает вопрос «а этот слой
обернули?» (S05), «от чего считается бюджет?» (C06), «сколько раз валидируются лимиты?». Ни один слой
ниже не конструирует политику — значит, не может её потерять.

### H.2 Монополия на байты

```csharp
// BEFORE — два равнодоступных пути, один из них небезопасен
internal BinaryReader RawReader => _reader;
// ...
public object Read(BinaryPayloadReader reader, Type declaredType) =>
    new Guid(reader.RawReader.ReadBytes(16));                       // ArgumentException наружу (C07)

int count = DeserializationGuard.ValidateCount(
    reader, reader.RawReader.ReadInt32(),                           // сырое чтение рядом с проверкой
    reader.Budget.Limits.MaxCollectionLength, "Collection count");
```

```csharp
// AFTER — сырого пути не существует
internal sealed class ValueReader                 // единственный держатель потока
{
    public void ReadExact(Span<byte> destination, string what);     // BinaryFormatException при обрыве
    public ElementCount ReadCount(CountKind kind);                  // лимит + бюджет внутри
    public string ReadString();
    public byte[] ReadBlob();
}

// форматтер:
public Guid Read(ValueReader reader)
{
    Span<byte> buffer = stackalloc byte[16];
    reader.ReadExact(buffer, "Guid");
    return new Guid(buffer);
}
```

**WHY.** Безопасность перестаёт быть выбором. `ElementCount` — не `int`: движок не примет непроверенное
число как длину цикла, поэтому «прочитал счётчик и сразу выделил» становится невыразимым.

### H.3 Инверсия обхода графа

```csharp
// BEFORE — форматтер сам ведёт цикл, сам (не) считает глубину и узлы
public object Read(BinaryPayloadReader reader, Type declaredType)
{
    var instance = ActivatorCache.CreateInstance(declaredType);
    var add = MethodInvokerCache.GetOneArgInvoker(declaredType, "Add", elementType);
    int count = DeserializationGuard.ValidateCount(/* … */);
    for (int i = 0; i < count; i++)
        add(instance, reader.ReadElement(elementType));              // ни depth, ни nodes (S03/S04)
    return instance;
}
```

```csharp
// AFTER — форматтер описывает форму и builder; циклом владеет движок
internal interface ISequenceFormatter : ITypeFormatter
{
    Type ElementType(Type declaredType);
    object CreateBuilder(Type declaredType, int capacityHint);       // регистрируется как узел ДО детей
    void Add(object builder, object? element);
    object Complete(object builder);
}

// PayloadEngine (единственная реализация цикла на весь проект):
using var depth = operation.Budget.EnterDepth();                     // ref struct, без аллокации
operation.Budget.ConsumeObjectGraphNodes(1);
var count   = reader.ReadCount(CountKind.Collection);                // лимит + кумулятивный бюджет
var builder = formatter.CreateBuilder(declaredType, count.CapacityHint);
references.Register(id, builder);                                    // идентичность до детей
for (int i = 0; i < count; i++)
    formatter.Add(builder, ReadValue(elementType));                  // рекурсия — у движка
return formatter.Complete(builder);
```

**WHY.** S03, S04, S11, C02 и половина C03 перестают быть вопросами форматтера. Глубина, узлы, лимит
длины, бюджет элементов, ленивость записи и регистрация идентичности реализованы **один раз**.
Новый форматтер не может их обойти, потому что не управляет обходом. Это же удаляет 14 копий
`Cast<object?>().ToList()` и аллокацию `DepthScope` на каждый узел.

### H.4 Фазы: политика уходит из алгоритма

```csharp
// BEFORE — публичный оркестратор и есть барьер; заменяется снаружи (AR-01)
public sealed class Compressor(ICompressionAlgorithm algorithm, SerializationLimits? limits = null)
    : ICompressor
{
    public byte[] Compress(byte[] rawPayload)
    {
        if (rawPayload.LongLength > _limits.MaxPayloadBytes)
            throw new BinaryLimitException(
                $"Payload length {rawPayload.LongLength} exceeds the configured maximum of {_limits.MaxPayloadBytes}.");
        // …
    }
}
```

```csharp
// AFTER — алгоритм чист, барьер внутри пайплайна и незаменим снаружи
internal readonly struct PhaseBudget(SerializationLimits limits)
{
    public void CheckPayload(long length)    => Check(length, limits.MaxPayloadBytes,   "payload");
    public void CheckCompressed(long length) => Check(length, limits.MaxCompressedBytes, "compressed");
    public void CheckEncrypted(long length)  => Check(length, limits.MaxEncryptedBytes,  "encrypted");
}

// V1FormatPipeline.Write:
operation.Phases.CheckPayload(payload.Length);
var compressed = _compression.Compress(payload, operation.Phases);   // алгоритм лимитов не знает
operation.Phases.CheckCompressed(compressed.Length);
```

**WHY.** Пользовательский алгоритм больше не может отключить защиту: он вызывается **внутри**
барьера, а не вместо него. Публичная поверхность сужается до чистой механики, что и требуется
внешнему реализатору.

### H.5 Владение ключом

```csharp
// BEFORE — зануляется чужая память, disposed-объект работает
byte[] key = ResolveKey(DefaultKeyId);     // массив вызывающего/резолвера
try { /* … */ }
finally { if (isTransient) CryptographicOperations.ZeroMemory(key); }   // S07

public void Dispose()
{
    if (_fixedKey is not null)
        CryptographicOperations.ZeroMemory(_fixedKey);                  // S08: ключ вызывающего
    _disposed = true;                                                   // но проверки нет
}
```

```csharp
// AFTER — владение выражено типом
public sealed class SecretKey : IDisposable            // всегда владеющая копия
{
    public static SecretKey CopyFrom(ReadOnlySpan<byte> material);
    public ReadOnlySpan<byte> Span { get; }             // не покидает вызов
    public void Dispose();                              // зануляет ТОЛЬКО свою копию
}

public interface IKeyProvider
{
    SecretKey Resolve(string? keyId);                   // контракт: возвращает владеющую копию
}

// использование в пайплайне:
using var key = operation.Keys.Resolve(header.KeyId);
_encryption.Decrypt(ciphertext, key.Span, associatedData, destination);
```

**WHY.** S07 и S08 перестают быть возможными: у сериализатора нет ссылки на чужую память, а «занулить
в finally» применимо только к собственной копии. `Encryptor.None` исчезает вместе с проблемой
уничтожаемого синглтона.

### H.6 Двухфазная проверка кадра

```csharp
// BEFORE — аллокация по объявленному размеру, проверка позже (S06)
byte[] onDiskPayload = DeserializationGuard.ReadExactly(
    reader.BaseStream, header.OnDiskLength, "On-disk payload");   // new byte[8 МБ] из 29 байт
```

```csharp
// AFTER — объявленное сверяется с настроенным и с физически доступным до аллокации
operation.Phases.CheckEncrypted(header.OnDiskLength);             // BinaryLimitException
frame.RequireAvailable(header.OnDiskLength, "On-disk payload");   // BinaryFormatException
byte[] onDiskPayload = frame.ReadExact(header.OnDiskLength);      // аллокация только теперь
```

**WHY.** Убирает усиление 290 000× на входе, а вместе с ним и все аналогичные усиления для строк и
блобов внутри payload — правило живёт в одном примитиве, а не в каждом форматтере.

### H.7 Симметрия контракта типа

```csharp
// BEFORE — план от runtime-типа на записи, от declared-типа на чтении (C04/A03)
var plan = TypeAccessorCache.GetOrBuild(value.GetType());
foreach (var accessor in plan.Members)
    WriteValue(accessor.Getter(value), accessor.MemberType);
```

```csharp
// AFTER — один контракт, обе стороны читают его одинаково
var contract = TypeContract.For(declaredType);          // члены + ключи + union + режим
if (!contract.TryResolveRuntime(value.GetType(), out var runtime))
    throw new BinaryTypeException(
        $"Declared type '{declaredType}' received a value of runtime type '{value.GetType()}', " +
        $"but '{declaredType.Name}' has no [BinaryUnion] map — the derived layout cannot be read back.");

writer.WriteDiscriminator(runtime.Tag);                 // отсутствует, если union не объявлен
foreach (var member in runtime.Members)
    WriteValue(member.Get(value), member.MemberType);
```

**WHY.** C04, A03, A01 и C05 — проявления одного дефекта: контракт типа нигде не материализован
целиком. Единый `TypeContract` делает проверку обязательной и однократной, а не рассыпанной по
`TypeAccessorCache` + writer + reader.

---

# I. План миграции

Фазы независимо ревьюятся и независимо откатываются. Явно указано, что удаляется.

### Фаза 0 — Фиксация поведения (подготовка)

- **Цель:** получить сеть безопасности до структурных правок.
- **Область:** `tests/` — перенести 19 проб из `audit/Problems.cs` в xUnit как *characterization*-тесты
  (фиксируют текущее поведение, помечены `Skip` там, где поведение должно измениться), плюс golden-wire
  файлы для V0, V1, keyed, полиморфизма, `PreserveReferences`.
- **Зависимости:** нет.
- **Wire:** нет.
- **Откат:** тривиальный.
- **Критерий:** каждый пункт §5 `System-Contract.md` покрыт хотя бы одним тестом.

### Фаза 1 — Владение ключами и материалом (AR-05)

- **Цель:** закрыть S07/S08 структурно.
- **Область:** `Core/Crypto/*`, `Serialization/Crypto/*`, `Configuration/*`.
- **Удаляется:** `Encryptor.None` как публичный синглтон; ветка `isTransient`; занудение чужих массивов.
- **Публичный контракт:** `IKeyProvider`, `SecretKey` (additive); ключевые параметры builder-а (breaking).
- **Wire:** нет.
- **Риск отката:** низкий.
- **Критерий:** массив вызывающего неизменен после операции и после `Dispose`; использование после
  `Dispose` → `ObjectDisposedException`; повторное шифрование даёт разные корректные ciphertext.

### Фаза 2 — Граница операции и физические байты (AR-04, AR-09, AR-10, C06)

- **Цель:** один владелец политики и предсказуемый учёт байтов.
- **Область:** новый `Operation/`; `Security/` сокращается до `ByteMeter` + `ByteWindow`;
  `V0/V1FormatCodec`, `BinaryFormatRouter`, `BinarySerializer`.
- **Удаляется:** `BudgetedReadStream`, `BudgetedWriteStream`, `BoundedReadStream` в текущем виде;
  ~10 повторных `limits.Validate()`; конструирование `SerializationBudget` внутри Reader/Writer.
- **Wire:** нет.
- **Риск отката:** средний (затрагивает все входные точки).
- **Критерий:** S05 и S06 закрыты; C06 закрыт; `SerializationLimits` не упоминается ниже пайплайна.

### Фаза 3 — Монополия на байты (AR-02, C07)

- **Цель:** удалить сырой путь.
- **Область:** `ValueReader`/`ValueWriter`; все 60 форматтеров — механическая замена вызовов.
- **Удаляется:** `RawReader`, `RawWriter`, `DeserializationGuard` (методы расходятся между
  `ValueReader` и движком), `BinaryStreamExtensions` (уходит в заголовок).
- **Wire:** нет.
- **Риск отката:** средний по объёму, низкий по семантике (замена механическая).
- **Критерий:** `grep -r "RawReader\|RawWriter" src/` пуст; ни один форматтер не ссылается на
  `SerializationLimits`.

### Фаза 4 — Инверсия обхода графа (AR-03; S03, S04, S11)

- **Цель:** структурная защита обхода.
- **Область:** `PayloadEngine`; контейнерные форматтеры переводятся на builder-контракт.
- **Удаляется:** `WriteNestedTracked`/`ReadNestedTracked` в текущем виде; 14 × `Cast<object?>().ToList()`;
  класс `DepthScope` (становится `ref struct`).
- **Wire:** нет.
- **Риск отката:** высокий (ядро), поэтому идёт после фазы 0.
- **Критерий:** рекурсивная коллекция на `MaxDepth+1` падает `BinaryLimitException` на чтении и записи;
  кадр 1.2 МБ из пробы больше не роняет процесс; бесконечный `IEnumerable` отвергается без зависания.

### Фаза 5 — Контракт типа (AR-08; C04, C05, A01, A03, C01, A02)

- **Цель:** симметрия и отказ вместо тихой порчи.
- **Область:** `TypeContract` (объединяет `TypeAccessorCache` + `PolymorphicTypeCache`), движок,
  `ref`/`existingInstance` перегрузки.
- **Удаляется:** дублирующая логика построения плана на записи; ручной разбор кадра в
  `Deserialize(existingInstance)`.
- **Публичный контракт:** поведение меняется (раньше молча «работало») — release note обязателен.
- **Wire:** нет.
- **Критерий:** C04/A03 → `BinaryTypeException`; A01 → `BinaryTypeException`; C01/A02 → корректное
  значение при обеих настройках `PreserveReferences`.

### Фаза 6 — Фазы, политика и аутентификация (AR-01, AR-06; S01, S02, S09, S10, S12-B)

- **Цель:** закрыть публичную дыру в применении политики и привязать заголовок к AEAD.
- **Область:** `Core/*` контракты алгоритмов, `PhaseBudget`, заголовок (AAD), опции/builder, реестры.
- **Удаляется:** `ICompressor`/`IChecksumCalculator`/`IEncryptor` из публичного API; `AlgorithmResolver`
  из публичного API; `Register(kind, factory)` для встроенных алгоритмов.
- **Wire:** ранее выпущенные зашифрованные payload перестают аутентифицироваться; строгость (хвостовые
  байты, exact-output) меняет приём ранее принимавшегося мусора.
- **Критерий:** любой изменённый байт заголовка → `BinaryIntegrityException`; `RequireEncryption`
  отвергает plaintext и V0; Deflate отвергает избыточный выход; хвостовые байты V1 отвергаются.

### Фаза 7 — Протокол идентичности (AR-07; C02, C03)

- **Цель:** сделать `PreserveReferences` и эволюцию схемы совместимыми.
- **Область:** `ReferenceScope` в движке; keyed-разбор; builder-контракт (регистрация до детей).
- **Wire:** breaking под `PreserveReferences`.
- **Критерий:** разделяемые коллекции сохраняют идентичность; чтение старой схемой payload новой схемы
  со ссылкой внутри удалённого поля проходит успешно; цикл через immutable-контейнер даёт
  детерминированное `BinaryFormatException`.

### Фаза 8 — Перф-восстановление и зачистка (AR-12)

- **Цель:** вернуть стоимость защиты к константной на узел.
- **Область:** `ValueWriter` (потоковая запись последовательностей), ленивые таблицы идентичности,
  счётчик байт вместо опроса `Position`, бенчмарки из `Benchmark-Plan.md`.
- **Критерий:** нет аллокаций на узел вне самого объекта; нет полной материализации контейнера на
  записи; регресс к текущим бенчмаркам не хуже, чем на величину, зафиксированную в release note.

**Порядок обязателен только частично:** 0 → (1 ∥ 2) → 3 → 4 → 5 → 6 → 7 → 8. Фазы 1 и 2 независимы;
фаза 6 требует 2; фаза 7 требует 4.

---

# J. Release checklist

### Архитектура

```text
[ ] Ни один класс ниже FormatPipeline не ссылается на SerializationLimits
[ ] SerializationBudget создаётся ровно в одном месте (SerializationOperation)
[ ] grep "RawReader|RawWriter" по src/ пуст
[ ] Ни один форматтер не содержит цикла по элементам, управляемого данными с провода
[ ] Ни один форматтер не аллоцирует по счётчику с провода
[ ] Глубина, узлы графа и идентичность реализованы ровно по одному разу
[ ] Публичный API не содержит типов, применяющих лимиты
[ ] Глобальные мутабельные реестры устранены; конфигурация — снимок
```

### Безопасность

```text
[ ] Для каждой аллокации по недоверенному размеру указана точка проверки ДО аллокации
[ ] MaxDepth действует для рекурсивных коллекций (проба 1.2 МБ не роняет процесс)
[ ] MaxObjectGraphNodes учитывает контейнеры
[ ] MaxPayloadBytes действует на чтении в V0 и V1
[ ] Байтовые бюджеты относительны старту операции
[ ] Каждый байт заголовка V1 аутентифицирован (AAD) при AEAD-алгоритме
[ ] RequireEncryption/RequireChecksum отвергают downgrade
[ ] Ключевой материал: сериализатор не владеет чужой памятью и не зануляет её
[ ] Использование после Dispose невозможно
[ ] Распаковка отвергает и недостачу, и избыток относительно объявленного размера
[ ] Корневой payload V1 потребляется ровно полностью
[ ] Кумулятивный бюджет keyed-полей действует
```

### Корректность

```text
[ ] runtime ≠ declared без [BinaryUnion] → BinaryTypeException на записи
[ ] [BinaryKey] + [BinaryIgnore] → BinaryTypeException при построении контракта
[ ] populate-in-place отвергается для не-object форм
[ ] ref-перегрузка восстанавливает значение при обеих настройках PreserveReferences
[ ] PreserveReferences сохраняет идентичность коллекций и массивов
[ ] Пропуск неизвестных keyed-полей совместим с PreserveReferences
```

### Контракт и документация

```text
[ ] System-Contract.md обновлён: §2 (границы), §5/§6 (лимиты и бюджет), §7 (потоки), §15, §16, §17, §21
[ ] §21.3 не содержит пунктов, ставших требованиями релиза
[ ] CLAUDE.md описывает builder-контракт форматтера, а не «не забудь вызвать»
[ ] Release note перечисляет: breaking публичного API, breaking крипто-совместимости,
    ужесточение приёма ранее валидных payload
[ ] QA-Plan.md покрывает каждый пункт этого чек-листа тестом
```

---

# K. Отложенные вопросы (не входят в v1.0.0)

Эти пункты осознанно вне релиза. Они не должны молча превратиться в требования.

```text
D1  Публичный контракт форматтеров (ITypeFormatter наружу). Замораживать протокол обхода
    до появления двух-трёх реальных внешних кейсов преждевременно.
D2  Асинхронный API (ValueTask-based Serialize/DeserializeAsync). Требует пересмотра
    примитивов ValueReader; в v1.0 не заявляется вовсе.
D3  Потоковая (не буферизованная) обработка payload. Сейчас payload материализуется целиком;
    это ограничивает MaxPayloadBytes сверху размером кучи. Требует V2-кадра с чанками.
D4  V2-кодек как таковой. Архитектура его допускает (см. ниже), но v1.0 выпускается с V0+V1.
D5  Constant-time сравнение контрольной суммы. Для CRC32 не нужно; станет нужным, если
    будет разрешён MAC-подобный кастомный алгоритм — тогда и вводить признак на контракте.
D6  Генераторы исходного кода вместо expression-tree аксессоров (AOT/trimming-дружелюбность).
D7  Пул буферов фаз с явным временем жизни (сейчас ArrayPool используется точечно).
```

---

# Заключение

## Что требует перепроектирования

1. **Граница операции** — её просто нет (AR-04).
2. **Доступ к байтам** — монополия вместо двух путей (AR-02).
3. **Обход графа** — из форматтера в движок (AR-03).
4. **Применение фазовой политики** — из публичных оркестраторов в пайплайн (AR-01).
5. **Владение ключами и буферами** — выразить типами (AR-05).
6. **Протокол идентичности ссылок** — описать и реализовать как протокол (AR-07).
7. **Контракт типа** — один симметричный источник истины (AR-08).
8. **Реестры механизмов** — из процессного состояния в конфигурацию (AR-06).

## Что остаётся

- Двухуровневая модель алгоритмов (`I*Algorithm` = механика, оркестратор = политика) — идея верна,
  ошибочно только то, что публичным сделан второй уровень.
- Самоописывающийся кадр: магия + версия + роутер. Расширяемость версий не требует переделки.
- Полиморфизм по байтовым тегам без имён типов на проводе — отсутствует gadget-поверхность.
  Это сильная сторона, её надо явно зафиксировать в контракте.
- Кэши рефлексии на expression-деревьях (`ActivatorCache`, `MethodInvokerCache`, …) — ответственность
  чистая, менять незачем.
- Таксономия исключений — полная и осмысленная; требуется только устранить протечки.
- `SerializationLimits` как неизменяемая запись с валидацией — правильная форма, ошибочна только
  область распространения.

## Что подлежит удалению

```text
RawReader / RawWriter                       — точка обхода границы
DeserializationGuard                        — переходная абстракция; распадается на
                                              ValueReader (примитивы) и PayloadEngine (обход)
BudgetedReadStream / BudgetedWriteStream /
BoundedReadStream в текущем виде            — три роли на два механизма (ByteMeter/ByteWindow)
Encryptor.None как публичный синглтон       — уничтожаемое глобальное состояние
ICompressor/IChecksumCalculator/IEncryptor
в публичном API                             — политика в точке расширения
AlgorithmResolver в публичном API           — тиражирование конструирования политики
*AlgorithmRegistry.Register(kind, factory)  — глобальная подмена якоря доверия
14 × Cast<object?>().ToList()               — заменяется одним циклом движка
класс DepthScope                            — становится ref struct
~10 повторных limits.Validate()             — валидация один раз при построении опций
init-сеттеры BinarySerializerOptions        — обход Build()
BinaryStreamExtensions                      — уходит в заголовок как деталь
```

## Что должно стать инвариантом

```text
INV-1  Ни один тип ниже FormatPipeline не знает SerializationLimits.
INV-2  Доступ к байтам payload существует только через ValueReader/ValueWriter.
INV-3  Длина цикла по данным с провода выражается только типом ElementCount,
       который невозможно получить без проверки лимита и списания бюджета.
INV-4  Рекурсией, глубиной, узлами графа и идентичностью управляет исключительно PayloadEngine.
INV-5  Любая аллокация по недоверенному размеру предваряется сверкой с настроенным максимумом
       И с физически доступным остатком.
INV-6  Байтовые бюджеты относительны стартовой позиции операции.
INV-7  Криптографический материал всегда принадлежит владеющему типу; зануляется только своя копия.
INV-8  Публичный контракт алгоритма не содержит политики сериализатора.
INV-9  Всё, что читается, потребляется ровно полностью: корень, keyed-поле, фаза распаковки.
INV-10 Конфигурация неизменяема и валидируется один раз; глобального мутабельного состояния нет.
```

## Рекомендуемая целевая архитектура

```text
   Public API            BinarySerializer · StreamExtensions · атрибуты · исключения
                         SerializationLimits · I*Algorithm · IKeyProvider · SecretKey
        │ (единственная точка создания операции)
        ▼
   SerializationOperation           Limits снимок · Budget · PhaseBudget · Keys
        │
        ▼
   FormatPipeline (V0 | V1 | …)     кадр · порядок фаз · заголовок+AAD · фазовые лимиты
        │  ByteMeter (операционный счётчик) · ByteWindow (окно с Remaining)
        ▼
   PayloadEngine                    обход · глубина · узлы · ReferenceScope · TypeContract
        │  ValueReader / ValueWriter — монополия на байты, checked-примитивы, ElementCount
        ▼
   Formatters                       чистые кодеки типов: scalar | sequence | map | object
        │
        ▼
   Algorithms                       чистая механика над span-ами

   Зависимости строго вниз. Наверх — только через возвращаемые значения и исключения.
```

**Проверка расширяемости (обязательный мысленный эксперимент).**

- *Сценарий A, V2-кодек.* Меняется только `FormatPipeline` + новый `IHeaderCodec`. Форматтеры,
  движок, алгоритмы, лимиты, бюджет — без изменений; V2 переиспользует тот же безопасный конвейер,
  ничего не копируя. Сейчас: V2 обязан заново продублировать обёртки потоков, проверки длин и
  порядок фаз.
- *Сценарий B, другой заголовок.* Заголовок — отдельный контракт, сообщающий пайплайну длины фаз и
  AAD. Привязки к V1-раскладке в движке нет.
- *Сценарий C, новый алгоритм.* Реализуется против `I*Algorithm` в Core; ни одного internal-типа,
  ни одного упоминания лимитов.
- *Сценарий D, новый форматтер.* Защищён тремя механическими барьерами (§E): нет доступа к потоку,
  нет непроверенного счётчика, нет собственного цикла. Ответ на вопрос «что помешает забыть?» —
  структура, а не память разработчика.

## Release gate

```text
[ ] Фазы 0–7 плана миграции завершены и отревьюены
[ ] Все инварианты INV-1…INV-10 проверяемы автоматически (тест или анализатор/grep-гейт в CI)
[ ] Чек-лист §J закрыт с указанием доказательства (тест/файл) для каждого пункта
[ ] Проба audit/Problems.cs переписана под целевое поведение: 0 CONFIRMED, 0 UNEXPECTED
[ ] Кадр-проба глубины (1.2 МБ) в CI: BinaryLimitException, процесс жив
[ ] System-Contract.md не содержит отложенных пунктов, ставших требованиями
[ ] Бенчмарки зафиксированы; стоимость защиты на узел константна и задокументирована
[ ] Публичная поверхность заморожена и отражена в release note (API + крипто + строгость)
```
