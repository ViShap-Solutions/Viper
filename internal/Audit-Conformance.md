# Аудит соответствия v1.0.0 — R9c и закрытие R9e

**Класс: исторический.** Запись независимого аудита соответствия перед первым выпуском. Систему описывают `System-Contract.md` и документы, которые `README.md` относит к нормативным и действующим; этот отчёт — свидетельство того, что было проверено, и перечень находок для R9d и R9e.

Аудитор: skill `viper_conformance_auditor`, отдельная сессия. Отчёт ничего не исправляет; `src/`, `tests/`, `benchmarks/` и проверяемые документы на ветке не менялись.

---

# 1. Вердикт

**rc можно ставить** (R9e, коммит `e8af35b`, 2026-09-29).

- Все семнадцать находок R9c закрыты в коде и документах на слое, который они называли; три вопроса к владельцу решены и записаны (`Owner-Review.md` логи 65–68), подробности — §12.
- Пробный прогон CD прошёл: сборка Release без предупреждений в пакетах, 2 014 тестов зелёные в Debug и Release, три `.nupkg` и два `.snupkg` версии `1.0.0-rc.1`, у каждого пакета свой непустой README; харнесс — `--verify` 351 пара без отказов, `--smoke` 1 158 бенчмарков без отказов.
- Новых находок веса «блокирует rc» или «исправить до выпуска» нет; две новые находки — вес «зафиксировано» (CONF-18, CONF-19).

Вердикт R9c, заменённый этим: **rc ставить нельзя.**

- Три находки требуют решения владельца: объём INV-9 («у каждого поля одна кодировка») при подтверждённых пробой вторых записях `BigInteger`, `BitArray`, `CultureInfo`, `Version` (CONF-01); INV-4 в сформулированном виде не выполняется для цикла по keyed-полям и по записям сервисов заголовка (CONF-02); запись строки с одиночным суррогатом молча меняет значение (CONF-06).
- Ещё три находки веса «блокирует rc» ясны по стороне и слою: инварианты INV-8…INV-11, INV-14, INV-15, INV-18 держатся поведенческими, а не структурными тестами (CONF-03); D9.13(2) для `List<T>` реализовано иначе (CONF-04); часть публичной поверхности не записана в контракте (CONF-05).
- Сборка, набор (1 978 тестов в Debug и Release), харнесс (`--verify`, `--smoke`), упаковка трёх пакетов и 55 примеров документации — в порядке. Проводной формат, фикстуры и реестр `Retired.md` в целом соответствуют решениям; найденные расхождения в документах — вес «исправить до выпуска».

# 2. Объём и доказательства

| Что | Значение |
|---|---|
| Проверенный коммит | `16d2704552f1547426a92d8111aad5eef1960ed9` (merge PR #18, `docs/v1-consumer-docs`) |
| Ветка | `audit/v1-conformance`; `release/v1.0.0` и `origin/release/v1.0.0` указывают на тот же коммит; дерево чистое |
| Дата | 2026-09-29 |
| Предусловие | R9a закрыт (`16b2680`). Строка R9b в Progress — «gate holds — awaiting commit», но ветка слита (`16d2704`); аудит проведён по git-истории, расхождение — CONF-10 |

Команды и результаты:

```text
dotnet build Viper.sln --configuration Release
    Ошибок: 0, предупреждений: 6 — все в тестовом проекте (CS0414 Fixtures/Basic.cs:93, CS8631 Format/V0CorpusTests.cs:135,
    CS8604 Hostile/PropertyTests.cs:171, xUnit2013 Limits/ArrayShapeTests.cs:46 и :70, xUnit2028 RoundTrip/CorpusCollections.cs:377)
dotnet build src/ViShap.Viper.Core/…csproj и src/ViShap.Viper.Serialization/…csproj -c Release --no-incremental
    Предупреждений: 0, ошибок: 0 (ни одного предупреждения trimming/AOT)
dotnet test …Serialization.Tests.csproj --configuration Debug     пройдено 1978, не пройдено 0
dotnet test …Serialization.Tests.csproj --configuration Release   пройдено 1978, не пройдено 0 (Api/AotAnalysisTests в их числе)
dotnet run --project benchmarks/… -c Release -- --verify          "351 pairs, 0 failed." Corpus: 27 datasets
dotnet run --project benchmarks/… -c Release -- --smoke           "Smoke: 1158 benchmarks, 0 failed." (08:17)
dotnet pack × 3 -c Release --no-build -p:Version=1.0.0-rc.1 --output <scratchpad>/artifacts   (предварительно; пробный прогон CD — в R9e)
    ViShap.Viper.Core.1.0.0-rc.1.nupkg + .snupkg, ViShap.Viper.Serialization.1.0.0-rc.1.nupkg + .snupkg, ViShap.Viper.1.0.0-rc.1.nupkg;
    в каждом nuspec <version>1.0.0-rc.1</version> и собственный README: CORE-README.md (6 891 байт),
    SERIALIZATION-README.md (6 851), METAPACK-README.md (3 982)
Примеры docs/ и README: 55 блоков csharp, каждый собран отдельно в консольном проекте net10.0 (ImplicitUsings enable)
    51 собираются и выполняются с кодом 0; 4 — только объявления типов, собираются как библиотека (CONF-15)
Реестр Retired.md: git grep -F по 223 строкам «Searched as» вне исторических документов и Baselines/ (§6)
Пробы (scratchpad/probe, публичный API): вторые записи значений, одиночный суррогат, заголовок (§5, CONF-01, CONF-06)
Выгрузка публичной поверхности отражением обеих сборок: 340 строк (CONF-05)
```

Вывод пробы:

```text
BigInteger 1 canonical   = 0101
BigInteger 1 as 02 01 00: ACCEPTED -> 1
BigInteger 0 as empty blob 00: ACCEPTED -> 0
BigInteger 0 canonical   = 0100
BitArray 9 bits, padding bits set 0A 02 8D FF: ACCEPTED -> 101100011
BitArray 9 bits canonical 0A 02 8D 01: ACCEPTED -> 101100011
CultureInfo 'EN-us': ACCEPTED -> en-US
Version ' 1.2': ACCEPTED -> 1.2
Version '01.2': ACCEPTED -> 1.2
decimal with invalid flags: BinaryFormatException: Decimal value is malformed.
Char lone surrogate round trip: False
magic + version 0: BinaryFormatNotSupportedException: No pipeline registered for format version 0.
unknown skippable service 9 (kind 0x12): ACCEPTED -> 1
payload mode 02: BinaryFormatNotSupportedException: Payload mode 2 sets a bit this build does not know; …
Nullable flag 02: BinaryFormatException: Boolean value 2 is not a valid encoding; only 0 and 1 are admitted.
```

# 3. Решения

`Decisions.md` §1–§9 с поправками журнала `Owner-Review.md` (логи 53–64).

| Решение | Как решено? | Доказательство | Находка |
|---|---|---|---|
| 1.1 Всё в v1.0.0, после — только аддитивно | да | Progress R0–R9b; ничего отложенного не сделано наполовину (см. 9.2) | — |
| 1.2 Провод и API без оглядки на совместимость | н/п | — | — |
| 1.3 V1 окончателен, отдельного механизма extensions нет | да | расширение — только номер сервиса: `Metadata/BinaryFormatHeaderV1.cs:261-282` | — |
| 1.4 Reed–Solomon снят | да | нет в коде | — |
| 2.1 Раскладка заголовка, `onDiskLength` всегда | да | `BinaryFormatHeaderV1.cs:158-201`; `Format/WireFormatTests.cs:21-38` (минимальный кадр `42 53 45 52 01 00 00 01 01`) | — |
| 2.2 Режим payload, резервные биты → NotSupported | да | `BinaryFormatHeaderV1.cs:219-222`; HDR-29 (`Format/HeaderTests.cs:365`); `WireFormatTests.cs:67-74` | — |
| 2.3.1–2.3.7 Записи сервисов, порядок, критичность, тело ровно | да | `BinaryFormatHeaderV1.cs:231-287`; HDR-21…HDR-27 | — |
| 2.3.8 Заголовок ≤ 4 096 байт | да | `BinaryFormatHeaderV1.cs:47, 253-257`, `TryMeasure`; HDR-28; SRC-08 | — |
| 2.4 Тела: `id · [name]`, пустое имя, `id = None` | да | `BinaryFormatHeaderV1.cs:399-427`; HeaderTests:393 | — |
| 2.4.1 Правило коэффициента | да | `BinaryFormatHeaderV1.cs:315-316, 491-499`; HDR-20 | — |
| 2.4.2 Длина открытого текста не объявляется; порядок проверок | да | `Pipeline/V1FormatPipeline.cs:385-409`; `CheckPlaintext` `BinaryFormatHeaderV1.cs:327`; CMP-18 | — |
| 2.4.3 Длина хэша = `HashSizeInBytes` | да | `Algorithms/ChecksumService.cs:21-29`; CHK-04 | — |
| 2.4.4–2.4.5 `KeyId` в теле шифрования со свёрткой null | да | `WireFormatTests.cs:175-190` (`01 03 61 62`, `01 01`, `01 00`) | — |
| 2.5 Полный пример Brotli + AES-GCM | да | `WireFormatTests.cs:41-65`, байты дословно | — |
| 3.1 AAD = точные байты заголовка | да | запись `V1FormatPipeline.cs:140-149`; чтение `:250`, `:393`; ENC-05, ENC-06 | — (тест поведенческий: CONF-03) |
| 3.2 Точная `GetCiphertextLength`, сервис сверяет | да | `Algorithms/EncryptionService.cs:25-34, 69-72`; ENC-25 | — |
| 3.3 Шифрование прямо в назначение | да | `Pipeline/SealedBody.cs`, `EncodedFrame.cs`; контракт §13 | — |
| 3.4 Алгоритм без точной длины не подключается | да | форма `IEncryptionAlgorithm` | — |
| 4.1.1 Только минимальный varint | да | `Io/WireReader.cs:371-397`; HST-40 | — |
| 4.1.2 Служебные числа — varint, данные фиксированные | да | `WireReader`/`WireWriter`; WF-05, WF-07 | — |
| 4.1.3 Keyed-поле `varint key · int32 длина` | да | `Engine/Contracts/MemberWriter.cs`; `WireFormatTests.cs:236-259` | — |
| 4.2 Null в первом числе; лог 60 для прочих типов | да | `Engine/Codecs/StructuralCodec.cs:29-132`; `WireFormatTests.cs:194-299, 387-420` | — |
| 4.2.2 После кадра ссылки без `+1` | да | `WireFormatTests.cs:120-128, 262-273` | — |
| 4.2.3 `byte[]` — последовательность | да | `WireFormatTests.cs:131-136` | — |
| 4.2.4 `ImmutableArray<T>`: `0` = default | да | `Engine/Codecs/ArrayCodecs.cs`; `Format/CompositeWireTests.cs:82-119` | — |
| 4.2.5 Битовые маски и пропуск null-полей отвергнуты | да | не реализованы | — |
| 4.3 Кадр ссылки одним varint, id явные | да | `StructuralCodec.cs:198-238`; WF-10…WF-12; KEY-17 | — |
| 4.4 Ключи строго по возрастанию | да | `Engine/Codecs/ObjectCodec.cs:261-274`; KEY-23 | — |
| 5.1–5.3 V0 — голый payload, без ссылок | да | `Pipeline/V0FormatPipeline.cs`; V0-01; CYC-тесты | — |
| 5.4 Keyed-запись V0 в non-seekable | да | V0-25 (`Format/V0FormatTests.cs:125`) | — |
| 5.5 V0 = payload V1 байт в байт | да | V0-19, V0-21, KEY-18 | — (тест поведенческий: CONF-03) |
| 5.6 Граница V0 при чтении | да | V0-27; API-22; `Exceptions/ExceptionMappingTests.cs:348` | — |
| 6.1–6.3 Свой буфер, одна копия, атомарность | да | `Io/PayloadBuffer.cs`, `Pipeline/EncodedFrame.cs`; STR-29, STR-30 | — |
| 7.1–7.3 CD | да | `.github/workflows/cd.yml:40-99` | CONF-12 (текст сообщения) |
| 8.1–8.4 Шов в форме метода, сверка вызовов | да | `Engine/Contracts/TypeContract.cs`, `MemberWriter.cs`, `MemberReader.cs`; LIM-49; CTR-31 | — |
| 8.5 `ReflectedContract<T>`, `MemberAccessor<T,TMember>`, `Getter`/`Setter` по `ref` | да | `Engine/Contracts/ReflectedContract.cs:14-92, 139-156`; CTR-32 | — |
| 8.6 Порядок работы движка | да | `ObjectCodec.cs:36-81, 189-307` | — |
| 8.7 Генератор — иллюстрация | н/п | — | — |
| 8.8 Отложено до генератора | да, отложено | шов `internal`: `Api/PublicSurfaceTests.EngineTypes_AreNotPublic` | — |
| 9.1 Версия 1 | да | `BinaryFormatHeaderV1.cs:40` | — |
| 9.2 D-2, D-4, D-10 — после релиза | отложено | `git grep -i fingerprint\|zstd\|lz4\|ContractRegistry\|IIncrementalGenerator` в `src/ tests/ benchmarks/`: одно совпадение — строка "lz4x" в тесте помощника | — |
| 9.3 `StreamExtensions`, `FromHeader`/`FromStream` удалены; `WithKeys`; ключи в двух местах → Build | да | поверхность (§5); `BinarySerializerOptionsBuilder.cs:439`; OPT-22 | — |
| 9.4 `Populate` | да | `ObjectCodec.cs:87-142`; API-24 | — |
| 9.5 Асинхронность на границе кадра | да | LIM-50; API-25 | — |
| 9.6 Асинхронное чтение V0 → NotSupported; обязательный текст | да | API-26; XML: `BinarySerializer.cs:368, 448, 762, 852`, `BinarySerializerOptions.cs:107`, `BinarySerializerOptionsBuilder.cs:283`; `docs/formats.md:202` | — |
| 9.7 `PooledPayload` — класс | да | `PooledPayload.cs`; API-23 | — |
| 9.8 «Сколько прочитано» | да | поверхность; API-22 | — |
| 9.9 Нет чтения `byte[]`; null → Format | да | `Api/SerializerApiTests.cs:116`; `ExceptionMappingTests.cs:309` | — |
| 9.10 Итоговая поверхность `BinarySerializer` | да | `Api/PublicSurfaceTests.cs:235-291` | CONF-05 (прочие типы) |
| 9.11 Интерфейсы алгоритмов, проверки сервисов | да | выгрузка поверхности; EXT-06; ENC-25, CHK-09, CMP-17 | — |
| 9.12 Встроенные; ChaCha20 без поддержки → NotSupported при Build и чтении; HKDF (лог 58) | да | `ChaCha20Poly1305Encryption.cs:124-130`, вызовы `BinarySerializerOptionsBuilder.cs:452`, `AlgorithmCatalog.cs:79`; `KeyProviders.cs:145-202`; ENC-26, ENC-27, CHK-10 | — |
| 9.13 (1) Формы + кодеки движка | да | `Formatters/Shapes.cs`; LIM-49 | — |
| 9.13 (2) `T[]`, `List<T>`, `ImmutableArray<T>` через span | **нет для `List<T>`** | `Formatters/Sequences/SequenceShapes.cs:50-56`: struct-энумератор; `CollectionsMarshal` в `src/` не встречается | **CONF-04** |
| 9.13 (3) Массив точной длины, если подкреплён байтами | да | `Engine/Codecs/Elements.cs:76-111`; TYP-03 | — |
| 9.14 Аллокации: стек предков, таблицы из пула, ёмкость, пуловые async, `AesGcm` на операцию | да | `GraphState`, `ReferenceScope.cs:27,102`, `ElementCount.CapacityFor`, 14 мест `PoolingAsyncValueTaskMethodBuilder`, `Aes256GcmEncryption.cs:76,103` | — |
| 9.14 Цели по путям | отложено к замеру | контракт §24 помечает фазовые цели открытыми (ALLOC-02, ALLOC-03) | — |
| 9.15 INV-1…INV-18 | частично | §4 | CONF-02, CONF-03 |
| 9.16 W11 корень прочитан ровно; W12 фикстуры один раз | да | `V1FormatPipeline.cs:429-431`; §7 | — |
| 9.17 Этапы | да | Progress | CONF-10 |
| 9.18 Суффиксы семейств | да | выгрузка поверхности; `PublicSurface_SharesNoSimpleNameWithTheLibrariesItBuildsOn` | — |
| 9.19 `Cache/` удалена; корпус оракула | да | CN-20; оракул выведен в R6 | — |
| 9.20 Генератор после релиза, AOT-аннотации в v1.0 | да | EXT-07 (`Api/AotAnalysisTests`), `tests/ViShap.Viper.AotConsumer` | — |
| 9.21 `DeserializeAsyncEnumerable` | да | API-27 | — |
| 9.22 Ветки и теги; beta не ставится (лог 64) | да | `ci.yml:3-14`; `git tag -l` пуст | CONF-11 (Workflow §3.3) |
| 9.23 Решения R0 | да | DATA-09 = 5 000 (`ShapeDatasets.cs:41`); оракул выведен в R6 | — |
| 9.24 `CLAUDE.md` меняется поэтапно | да, с отставанием | `CLAUDE.md:95-96` | CONF-10 |
| 9.25 Адаптеры Track B после R6; `BASE-02` | BASE-02 есть; адаптеров нет | `Reporting/BaselineComparison.cs`; `Adapters/` — только `ViperAdapter.cs` | CONF-16 |
| 9.26 `RunKind.Measurement` | да | `Environment/RunTarget.cs:93` | — |
| 9.27 Харнесс живёт с кодом | да | `--verify`, `--smoke` (§2) | — |
| 9.28 Реестр, сверка, классы | частично | §6, §8 | CONF-08, CONF-09 |
| 9.29 Skill аудитора | да | `.claude/skills/viper_conformance_auditor`; `Development-Workflow.md:68` | — |
| 9.30 `CORE-README.md` в пакете Core | да | `ViShap.Viper.Core.csproj`: `PackageReadmeFile` = `CORE-README.md`; пакет (§2) | — |
| 9.31 R9a…R9e | да | история веток | CONF-10 |
| 9.32 Диагностика (логи 61, 62, 63) | да | поверхность `ViShap.Viper.Diagnostics`; `Diagnostics/DumpTrace.cs:15-16` (256 символов, 64 байта); `Diagnostics/DumperTests` | — |

# 4. Инварианты

| INV | Держится конструкцией (как) | Раздел контракта | Структурный тест | Находка |
|---|---|---|---|---|
| INV-1 | `OperationState` — struct, создаётся в `BinarySerializer`, инспекторе и дампере; все члены ниже принимают `ref` | §2.2 | LIM-51 (`OperationState_IsCreatedOnlyAtThePublicEdge`, `…TravelsByReference`) — исходник и отражение | — |
| INV-2 | `WireReader`/`WireWriter` — `ref struct`; `MemberWriter`/`MemberReader` с приватными конструкторами и только `Member`/`Field`/`Value` | §2.3 | LIM-44, LIM-47, LIM-48, LIM-49 | CONF-17 (формулировка) |
| INV-3 | `ElementCount.IsBackedBy`/`CapacityFor`, `WireReader.RequireAvailable` | §6, §17 | TYP-03, SRC-10, LIM-42 — счётчик аллокаций | — |
| INV-4 | `ElementCount` — приватный конструктор, фабрики `Validate`/`ValidateShape` | §6, §17 | LIM-40 | **CONF-02** — цикл по keyed-полям и по записям заголовка идёт по сырому `int`; сырые числа доступны скалярному форматтеру |
| INV-5 | формы не получают счётчика и примитивов; контракт — только поверхности членов | §2.4, §14.1 | LIM-49 | — |
| INV-6 | примитивы в Core, не ссылающейся на сборку лимитов | §2.5, §12, §13 | CMP-12 (ссылки сборок, параметры) | — |
| INV-7 | каталог — снимок опций, статических изменяемых полей нет | §4.1 | CAT-06 — проверяет только статику `AlgorithmCatalog` | CONF-03 |
| INV-8 | `UnionMap` пишет байт тега | §15 | PM-14 — поведенческий (в payload нет имён) | **CONF-03** |
| INV-9 | см. `WireReader`, заголовок, `ObjectCodec` | §11, §22 | WF/HDR, KEY-23, HST-40 — поведенческие | **CONF-01**, **CONF-03** |
| INV-10 | фильтрованные `catch` на границах | §8, §9 | EXC-14…EXC-20, REF-19 — поведенческие; EXC-21/22 (исходник) не названы в §25 | **CONF-03** |
| INV-11 | `SecretKey` копирует; сервис освобождает только своё | §13.2 | ENC-12, ENC-14 — поведенческие | **CONF-03** |
| INV-12 | один построитель плана; шов `ContractOf<T>` | §14.1 | CONF-01…CONF-07 | — |
| INV-13 | `SerializationLimits` — неизменяемая запись; `Validate()` только на границах конфигурации | §5 | CFG-09 (исходник), OPT-08 | — |
| INV-14 | AAD — копия `PayloadBuffer` заголовка при записи, срез источника при чтении (`V1FormatPipeline.cs:140-149, 250`) | §13.1 | ENC-05, ENC-06 — поведенческие | **CONF-03** |
| INV-15 | кадр целиком в пуловых буферах до копирования | §2.6 | STR-30 — поведенческий | **CONF-03** |
| INV-16 | в `Engine/` и `Formatters/` нет асинхронных методов | §2.4, §3.5 | LIM-50 | — |
| INV-17 | бокс только в `WriteBoxed`/`ReadBoxed` | §2.4, §15 | TYP-02 — считающий двойник (счётчик аллокаций) | — |
| INV-18 | оба пайплайна пишут через `Graph.WriteRoot` без ссылок | §10.2, §22.7 | V0-19, V0-21, KEY-18 — поведенческие | **CONF-03** |

Все восемнадцать записаны в контракте (§25 и названные разделы).

# 5. Согласованность

**Контракт ↔ `src/`.** Контракт прочитан полностью (§1–§25) и сверен с `Io/`, `Security/`, `Engine/`, `Pipeline/`, `Metadata/`, `Algorithms/`, `Crypto/`. Расхождения: CONF-01 (INV-9 и §24 «every wire field has one encoding only»), CONF-02 (INV-4, §2.3 «read a length and allocate it is not expressible» для скалярного форматтера), CONF-05 (члены вне §3), CONF-06 (запись строки), CONF-13 (§2 «dependencies point strictly downwards»), CONF-17 (§2.3). Остальное совпадает, включая порядок проверок §5.10, правила §11, §13, §16, §22.

**Правило ↔ чекпойнт ↔ тест.** Автоматически: 707 отмеченных чекпойнтов; все 96 названных тестовых классов существуют, кроме названных только в зачёркнутых (retired) строках и в строках BASE (CONF-08). Разделы контракта без единой ссылки из QA-плана: §1, §4 (подразделы цитируются), §8.6, §21.3, §21.4. Правило §8.6/§13 «`CryptographicException` при записи — `BinaryEncryptionException`» не имеет ни чекпойнта, ни теста (CONF-07). OPT-21 ссылается на §5 за публичный `Validate()`, которого §5 не называет (CONF-05).

**Бенчмарк-чекпойнты ↔ наборы.** Каждый набор `…Benchmarks`, названный в `Benchmark-Plan.md`, существует как класс; `--verify` и `--smoke` проходят. Открытые чекпойнты не являются критерием выпуска (§28).

**Поверхность ↔ §3 ↔ XML ↔ `docs/`.** Типы совпадают с §3 (`PublicSurfaceTests`); члены сверены только у `BinarySerializer` и построителя (CONF-05). XML-документация: ни одной ссылки на `internal/`, ни одной истории. В `docs/`, README и описаниях пакетов нет прилагательных о производительности; ссылок на `internal/` нет. Обязательный текст D9.6 есть в XML и в `docs/formats.md`. Таблицы лимитов и исключений `docs/options-and-limits.md`, `docs/exceptions.md` совпадают с §5 и §8.

# 6. Ничего не осталось

Поиск: `git grep -n -F` по каждой строке «Searched as» во всём репозитории, кроме `internal/rework/`, `internal/audit/`, `Architecture-Audit.md`, `Audit-*.md`, `benchmarks/**/Baselines/`, `BenchmarkDotNet.Artifacts/`.

| Строка реестра | Совпадения вне исторических | Допустимо? | Находка |
|---|---|---|---|
| R0 `RunKind.Partial`, «Partial по умолчанию», описание Baseline, DATA-09 | нет | — | — |
| R1 `ValueReader`, `ValueWriter` | QA §30 (1155, 1342, 1345, 1375); MICRO-01 | да (по строке) | — |
| R1 `PayloadWindow`, `OpenWindow` | нет | — | — |
| R1 `RemainingBytes`, `CanSeek` | QA §30 (1155, 1174); `CanSeek` — свойство потока | да | — |
| R1 keyed V0 в non-seekable, `MeteredReadStream` как механизм §7 | нет | — | — |
| R1 `new CompositeReader(`/`new CompositeWriter(` | `CompositeCodec.cs:72, 130` — внутри `Decode`/`Encode` | да | — |
| R2 `MeteredReadStream` | QA §30 (1155, 1172) | да | — |
| R2 `MeteredWriteStream`, `WindowReadStream`, `SkipRemaining` | нет | — | — |
| R2 `IRemainingBytes` | QA §30 | да | — |
| R2 `HashSet<object>` предков | нет | — | — |
| R2 `new …ReferenceTable()` | `ReferenceScope.cs:27, 102` — внутри `Rent` | да | — |
| R2 `IFormatPipeline.Write(Stream…)` | нет | — | — |
| R2 `byte[]`-результаты сервисов | `EnvelopeTests.cs:205` — свой построитель теста | да | — |
| R2 `BuildAssociatedData` через `BinaryWriter` | `Fixtures/Wire.cs:177`, `Mutate.cs:50` | да | — |
| R2 `MemoryStream` на пути payload | STR-29 зелёный | да | — |
| R2 контракт §7/§5.10 формулировки; QA §21 `Streams/`; MICRO-09; MICRO-07; WL-03/ALLOC-05 | нет | — | — |
| R3 `StreamExtensions` | QA 265, 269-279, 1362, 1439 (зачёркнуто/§30); PROF-07 с пометкой; **QA:173 BASE-02 «replaced by Api/StreamExtensionsTests»** | BASE-02 — нет | CONF-08 |
| R3 `FromHeader`, `FromStream` | QA зачёркнуто; `AllocationTests` — локальный помощник; имена бенчмарков `DeserializeFromStream…` | да | — |
| R3 `Deserialize<T>(byte[])` | адаптер бенчмарка; QA зачёркнуто | да | — |
| R3 `existingInstance`, `ref T existing`, `ExistingInstanceTests` | QA зачёркнуто 196-200; **QA:172 BASE-01, :175 BASE-04 → `Api/ExistingInstanceTests`** | BASE — нет | CONF-08 |
| R3 null `byte[]` → ArgumentNull | нет | — | — |
| R3 «needs a seekable stream» | контракт §3.1:322 (V0), `BinaryFormatInspector.cs:137` (`Peek`) | да (две оставшиеся) | — |
| R3 `BinaryHeaderPeek`, `SerializeTo(`, `ReadAhead(` | `SerializeTo(` — метод харнесса `Dataset` | да | — |
| R3 «neither required nor rejected» | QA V0-06 про seekable-поток | да | — |
| R3 Populate-корень обратная ссылка | `ObjectCodec.cs:100` — в `BinaryFormatException` | да | — |
| R3 §3.2/§4.3 контракта, «an async API», SX-*, бенчмарк PROF/FAIR/ALLOC | только зачёркнутые пункты и итог QA:1466 | да | — |
| R4 `SerializationOperation`, `WithPreserveReferences` | QA §30 Q1; имена тестов `…_WithPreserveReferences_…` (не член) | да | — |
| R4 `.Operation.*` | нет | — | — |
| R4 `GraphReader`, `GraphWriter` | QA §30 | да | — |
| R4 `ITypeFormatter` и др. | **нет** — строка реестра всё ещё «reported» | — | CONF-08 (колонка устарела) |
| R4 `Resolve(typeof(`, `CachedTypeCount`, «returning null» | `TypeContractCache.CachedTypeCount` (другой член); ENC-08 (другой смысл) | да | — |
| R4 кэши `Cache/` | QA CN-06…CN-13 с пометками, §30; MICRO-15 с пометкой; CN-20 и его тест `CacheTests.cs:163`; **`Exceptions/SourceInvariantTests.cs:99` перечисляет `"Cache/"` среди папок ниже пайплайна** | последнее — нет | CONF-08 |
| R4 `MemberBinding`, `PrimitiveFormatter<` и др. | `MemoryLikeFormatter` — QA §30 | да | — |
| R4 `ReadFlag`/`WriteFlag`, `IFormatPipeline.Read(…object?)`, `ImmutableArray` как композит, `Enum.ToObject`, помощники `Operation()`, MICRO-05/06 | нет | — | — |
| R4 CN-06, CN-13, CN-19 | только с пометками | да | — |
| R5 `new Crc32(` и др. | `new Aes256Gcm(` — QA §30:1201 | да | — |
| R5 `GetMaxCompressedLength`, `SupportsIncrementalDecompression` | BCL `BrotliEncoder`, PERF-04/08, тест; QA §30 NX-01 | да | — |
| R5 `GetMaxCiphertextLength`, `PayloadBufferWriter`, «capped», `SealedBody`, ключ без проверки, «only known after encryption» | «could not fit…» — сообщение писателя сжатия | да | — |
| R5 CMP-15 | с пометкой | да | — |
| R6 фиксированный заголовок (`CompressedLength` …) | QA зачёркнуто HDR-11/13/14; PERF (подстрока); имена тестов о сжатой длине; `OnDiskLengthOffset` — член помощника | да | — |
| R6 HDR-13/14, AAD-образ, `self-verifying`, WF-29, `declared plaintext`, `RequireStored`, маркер ссылки, REF-13/14, WF-13, `int32`-счётчики, HDR-11, `probe window` | только пометки; «ciphertext bytes» в XEP-06 — другой смысл | да | — |
| R6 «null flag» | `Codec.cs:16`, `ScalarCodecs.cs:3,66`, `WireTrace.cs:35`, `FormatterRegistry.cs:21`, PERF-09 — там, где флаг остался | да | — |
| R6 «must be non-negative» | `ElementCount`, `WireReader`, `WireWriter`, `PhaseBudget`, `SerializationBudget` — значения вызывающего | да | — |
| R6 оракул | QA §0 и помощники с пометкой | да | — |
| R6 фикстуры, `BinaryHeaderInfo`, MICRO-08, `DumpHeader(byte[])` | нет | — | — |
| R9a карта §21.3, «non-buffered», §24, §5/§13 формулировки, `..\..\README.md`, фильтры `CLAUDE.md`, `§22.8` | QA BASE-06/07 — записи | да | — |
| R9b «currently empty pending», оценочные слова | `High-performance` — QA §30 NX-10 | да | — |

**Сверх реестра.** Поиск словаря прежней системы (`DeserializationGuard`, `RawReader`, `BinaryPayloadReader`, `NestedFormatter`, `ByteMeter`, `ICompressor`, `IEncryptor`, `AlgorithmResolver`, `AlgorithmRegistry`, `BudgetedReadStream`, `EnableTrace`, `EncryptionGuarantee`, `IFormatter<`, `Convert.ChangeType`, «fixed header», `object? Read`) в `src/`, `docs/` и README — ноль совпадений. Найдено вне реестра: имя ветки `rework/r9-release-gate` и «после R6 v1.0.0-beta.N» в `Development-Workflow.md:181, 184` (CONF-11).

# 7. Wire

- Байтовые правила плана §6 и диаграмм `Decisions.md` §2–§4 закреплены дословно тестами над продовым кодировщиком: минимальный кадр, заголовок Brotli + AES-GCM, `KeyId` (три формы), строки `00`/`01`/`06 68 65 6C 6C 6F`, `List<int>` с ссылками и без (`04 …`, `01 03 …`), keyed-класс и struct, `int?`, union, кадр ссылки `01`/`02`/`03`/`04`, `Uri "a"` = `02 61`, `BitArray` = `0A 02 8D 01`, `int[2,3]` = `03 02 03 …`, `Tuple` = `01 …`, `ImmutableArray<T>` в шести формах — `Format/WireFormatTests.cs`, `Format/CompositeWireTests.cs:82-119`, `Contracts/ConformanceTests.cs`. Два примера — запись контрольной суммы `03 05 01 9A 3B C1 07` и custom-имя `05 09 FF 01 04 6C 7A 34 78 E8 07` — сверены дословно только с тестовым кодировщиком (CONF-14).
- Фикстуры `Fixtures/Wire/*.bin` (13 файлов): `git log --follow` каждой — за время переделки ровно одно изменение, `31074d0` («Final wire format … fixtures re-frozen», R6); после него — ни одного. `CLAUDE.md` записывает единственное исключение и правило «never regenerated».
- Оракул удалён: `git ls-files | grep -i oracle` пусто.
- `Fixtures/Wire.cs` и `Mutate.cs` не ссылаются на `WireReader`, `WireWriter`, `BinaryFormatHeaderV1`, `BinarySerializer` и пространства `Io`/`Metadata`/`Engine`/`Pipeline`; кодируют через собственный `BinaryWriter`.

# 8. Классы документов

| Файл | Класс в шапке | `CLAUDE.md` / `internal/README.md` | Находка |
|---|---|---|---|
| `System-Contract.md` | normative | normative | — |
| `QA-Plan.md`, `Benchmark-Plan.md`, `Benchmark-Graceful-Stop.md` | plan | plan | — |
| `performance/README.md`, `PERF-01…PERF-09` (10 файлов) | **нет** | plan | CONF-09 |
| `README.md` | operational | operational | — |
| `Development-Workflow.md` | «действующий» | operational | — (русский эквивалент, так же в шапках исторических) |
| `Architecture-Audit.md`, `Audit-Closure.md`, `Audit-Future.md`, `Audit-Refactor.md` | исторический, «правила больше не действуют» | historical | — |
| `audit/Problems-Solutions.md`, `audit/Problems-Solutions-Review.md` | historical, «rules no longer apply» | historical | — |
| `audit/Problems.cs` | «SUPERSEDED — historical audit artifact … Do not compile», без формулы класса | historical | CONF-09 |
| `rework/*.md` (9 файлов) | historical, «rules stop applying when v1.0.0 is released» | historical | — |

Живые документы не ссылаются на исторические как на источник правила; `CLAUDE.md` и `Development-Workflow.md` ссылаются на `rework/` как на запись переделки до выпуска, что допускают их собственные шапки.

# 9. Готовность к выпуску

- Набор зелёный: Debug 1 978/1 978, Release 1 978/1 978.
- Харнесс собирается; `--verify` — 351 пара, 0 провалов; `--smoke` — 1 158 бенчмарков, 0 провалов.
- `dotnet pack` трёх пакетов проходит, каждый несёт собственный непустой README и версию из `-p:Version` (предварительно; пробный прогон CD — в R9e).
- Сборки Core и Serialization при чистой пересборке: 0 предупреждений, включая trimming/AOT; `Api/AotAnalysisTests` зелёный.
- Примеры: 51 из 55 собираются и выполняются, 4 объявляют только типы и собираются как библиотека.

# 10. Принципы

**Зависимости строго вниз.** `Io/`, `Engine/`, `Security/` не ссылаются на пайплайн и API. `Formatters/FormatterRegistry.cs` (пространство `ViShap.Viper.Formatters`) строит кодеки движка — `ObjectCodec<>`, `SequenceCodec<,,,>`, `MapCodec<,,,,>`, `ScalarCodec<>`, `NullableCodec<>`, `ImmutableArrayCodec<>`, `RejectedCodec<>` (строки 115-254): ссылка из слоя форматтеров вверх, в движок (CONF-13). `SerializationLimits` по имени ниже пайплайна не встречается (LIM-43); `Io/` и `Engine/` читают снимок через `state.Limits` — так задумано в `Architecture-Audit.md` §C.6 (снимок в операции). Тест LIM-43 перечисляет несуществующую папку `Cache/` (CONF-08).

**Одна операция владеет лимитами; payload их не поднимает.** `OperationState.Limits` — `readonly`; `SerializationLimits` — запись с `init`; `Validate()` вызывается только на границах конфигурации (CFG-09). Ни одно значение с провода не пишется ни в лимиты, ни в бюджеты, ни в `PhaseBudget` — только расходует их.

**Три барьера.**
- *Монополия на байты:* декодирование — только через `WireReader`/`WireWriter` (LIM-44, LIM-48); фазы пайплайна и дампер держат байты как непрозрачные спаны (CONF-17 — формулировка).
- *Проверенные счётчики:* каждый счётчик элементов — `ElementCount`; но цикл по keyed-полям (`ObjectCodec.cs:252-262`) и по записям сервисов (`BinaryFormatHeaderV1.cs:231-234`) идёт по сырому `int`, а скалярному форматтеру доступны `ReadInt32`, `Read7BitEncodedInt`, `ReadFolded` — «прочитать длину и выделить» для скаляра выразимо, и тест этого не запрещает (CONF-02).
- *Обход у движка:* формы не получают счётчика и примитивов, контракт — только `MemberWriter`/`MemberReader`, движок сверяет вызовы (LIM-47, LIM-49, CTR-31).

# 11. Находки

### CONF-01 — вторые записи значений при INV-9 «у каждого поля одна кодировка»

- **Вес:** блокирует rc
- **Сторона:** нужно решение владельца
- **Слой:** скалярные форматтеры (`Formatters/Scalars/PrimitiveFormatters.cs`, `SystemFormatters.cs`) либо контракт §24/§25
- **Доказательство:** проба §2: `BigInteger` 1 читается и из `01 01`, и из `02 01 00`; 0 — из `01 00` и из пустого блоба `00`; `BitArray` из 9 бит читается одинаково из `0A 02 8D 01` и `0A 02 8D FF`; `CultureInfo` "EN-us" → `en-US`; `Version` " 1.2" и "01.2" → `1.2`. `BigIntegerFormatter.Read` = `new(reader.ReadBlob(…))` (`PrimitiveFormatters.cs:258`); `BitArrayFormatter.Read` не проверяет биты заполнения (`SystemFormatters.cs:107-119`).
- **Наблюдается:** у этих полей больше одной записи одного значения.
- **Ожидается:** `Decisions.md` 9.15 INV-9 «у каждого поля ровно одна кодировка: …»; контракт §24 «Every wire field has one encoding only, so no field can be rewritten into a second spelling of itself (INV-9)». Перечень после двоеточия эти типы не называет — отсюда вопрос объёма.
- **Варианты, видимые из материалов:** (а) сделать чтение каноническим — блоб `BigInteger` равен минимальной записи `TryWriteBytes`, биты заполнения `BitArray` нулевые, строка `Version`/`CultureInfo` равна тому, что пишет писатель; провод для корректного писателя не меняется; (б) сузить формулировку INV-9 в §24/§25 до перечисленных правил и записать, что значения, разбираемые из текста или блоба, принимают снисходительность разборщика.

### CONF-02 — INV-4 в сформулированном виде не держится

- **Вес:** блокирует rc
- **Сторона:** нужно решение владельца
- **Слой:** `Io/ElementCount.cs` и движок, либо контракт §2.3, §6, §17, §25
- **Доказательство:** `Engine/Codecs/ObjectCodec.cs:252-262` — `int fieldCount = reader.ReadFolded(…)`, проверка `MaxKeyedFields` и бюджета на месте, затем `for (int i = 0; i < fieldCount; i++)`; `Metadata/BinaryFormatHeaderV1.cs:231-234` и `:95` — цикл по `services`, сырому `int` с провода. `WireReader` отдаёт скалярному форматтеру `ReadInt32`, `Read7BitEncodedInt`, `ReadFolded`, `ReadBitCount` (`Io/WireReader.cs:188, 315, 331, 371`); LIM-40 проверяет лишь фабрики `ElementCount` и файлы, вызывающие `ElementCount.Validate`; ни один тест не запрещает скаляру цикл или выделение по такому числу.
- **Наблюдается:** граница цикла по данным с провода существует не только как `ElementCount`; для скалярного форматтера «прочитать длину и выделить» выразимо.
- **Ожидается:** `Decisions.md` 9.15 INV-4 «цикл по данным с провода — только по проверенному ElementCount»; контракт §2.3 «so "read a length and allocate it" is not expressible», §25 INV-4.
- **Варианты:** (а) завести проверенный тип (или `CountKind`) для числа keyed-полей и записей сервисов и закрыть сырые числа от скалярных форматтеров, со структурным тестом; (б) сузить INV-4 до циклов по элементам и записать в контракте собственные ограничители числа keyed-полей (`MaxKeyedFields` + бюджет) и записей (граница 4 096 байт), а §2.3 — в пределах форм и композитов.

### CONF-03 — инварианты без структурного теста

- **Вес:** блокирует rc
- **Сторона:** тесты и контракт §25 (колонка «Held by»)
- **Слой:** `tests/…/Limits/StructuralBarrierTests.cs` (или соседний структурный набор), `System-Contract.md` §25
- **Доказательство:** §25 называет для INV-8 — PM-14 (`Contracts/UnionDeclarationTests.cs:127`, поиск имён в байтах одного payload); INV-9 — WF/HDR, KEY-23, HST-40 (враждебные байты); INV-10 — EXC-14…EXC-20, REF-19; INV-11 — ENC-12, ENC-14; INV-14 — ENC-05, ENC-06; INV-15 — STR-30 (`Metering/AtomicWriteTests.cs:100-163`); INV-18 — V0-19, V0-21, KEY-18. Все — поведенческие. INV-7: CAT-06 (`Algorithms/AlgorithmCatalogTests.cs:185-209`) проверяет только статику `AlgorithmCatalog`, не сборку.
- **Наблюдается:** семь инвариантов закреплены поведением, один — структурно, но частично.
- **Ожидается:** `Decisions.md` 9.15 «Каждый держится структурным тестом»; контракт §25 «each pinned by a structural test that inspects the code rather than a behaviour».

### CONF-04 — D9.13(2): `List<T>` пишется не через span

- **Вес:** блокирует rc
- **Сторона:** код
- **Слой:** `Formatters/Sequences/SequenceShapes.cs` (`ListShape<T>`)
- **Доказательство:** `SequenceShapes.cs:50-56` — `List<T>.Enumerator`; `CollectionsMarshal` в `src/` отсутствует; отступление нигде не записано.
- **Наблюдается:** `List<T>` перечисляется struct-энумератором.
- **Ожидается:** `Decisions.md` 9.13 (2): «`T[]`, `List<T>`, `ImmutableArray<T>` — через span (`CollectionsMarshal.AsSpan`)»; `Rework-Plan.md` §10.1.

### CONF-05 — публичные члены вне контракта

- **Вес:** блокирует rc
- **Сторона:** контракт (§3, §4, §5, §19) и `Api/PublicSurfaceTests`
- **Слой:** `System-Contract.md` §3 и §19; сверка членов всех публичных типов в `PublicSurfaceTests`
- **Доказательство:** выгрузка поверхности (§2): `BinaryFormatInspector.Peek(ReadOnlySpan<byte>|ReadOnlySequence<byte>|Stream, SerializationLimits)` — с `BinaryConfigurationException` на недопустимых лимитах и `BinaryLimitException` на заголовке сверх лимитов (`Metadata/BinaryFormatInspector.cs:44-50, 79-89, 130-175`); `SerializationLimits.Validate()`; свойства `BinarySerializerOptions` (`WriteVersion`, `Compression`, `Checksum`, `Encryption`, `KeyId`, `Keys`, `Limits`, …) и семантика записи (`Equals`, `ToString`); позиционный конструктор и `Deconstruct` `BinaryHeaderInfo`; `SecretKey.CopyFrom`, `Span`, `Length`; конструкторы `StaticKeyProvider`, `DelegateKeyProvider`. `grep "Peek\|Validate()"` в контракте: `Peek` упомянут без перегрузки с лимитами (§4.3, §19), `Validate()` — нигде. `PublicSurfaceTests` сравнивает члены только `BinarySerializer` и построителя (`:285-299`). QA OPT-21 ссылается на §5 за `Validate()`.
- **Наблюдается:** наблюдаемые потребителем члены и исключения не записаны в контракте и не сверяются с ним.
- **Ожидается:** контракт §1 «the public surface (§3) … specified here, so a test — or a second implementation — can be written from this document»; §3 «This is the whole public surface».

### CONF-06 — строка с одиночным суррогатом молча меняется при записи

- **Вес:** блокирует rc
- **Сторона:** нужно решение владельца
- **Слой:** `Io/WireWriter.cs:124-136, 270-276` либо контракт §22.1 и `docs/supported-types.md`
- **Доказательство:** проба §2: `Deserialize<string>(Serialize("\uD800")) == "\uD800"` → `False`; `WireWriter.WriteString` использует `Encoding.UTF8`, который заменяет суррогат на U+FFFD без исключения.
- **Наблюдается:** запись проходит, значение после кругового переноса другое.
- **Ожидается:** контракт об этом молчит: §22.1 требует строгого UTF-8 только при чтении; §1 «A successful round trip…» и принцип «ни одного молчаливого искажения» (`Decisions.md` 9.28 — «без молчаливых рудиментов»).
- **Варианты:** (а) строгий кодировщик при записи и отказ (тип исключения — решение владельца); (б) записать замену в §22.1 и в документации.

### CONF-07 — правило без чекпойнта: `CryptographicException` при записи

- **Вес:** исправить до выпуска
- **Сторона:** QA-план и тесты
- **Слой:** `QA-Plan.md` (группа ENC), `Algorithms/EncryptionTests`
- **Доказательство:** контракт §8.6, §13: «A `CryptographicException` is `BinaryEncryptionException` on the way out»; код — `Algorithms/EncryptionService.cs:58-60`. `grep BinaryEncryptionException tests` — только `PublicSurfaceTests.cs:74` и `ExceptionHierarchyTests.cs:26`; §8.6 не цитируется ни одним чекпойнтом.
- **Наблюдается:** правило не проверяется.
- **Ожидается:** каждое правило контракта имеет чекпойнт и тест (`Rework-Plan.md` R9a; skill §3.3).

### CONF-08 — остатки удалённого в живых документах и тестах; колонка реестра неверна

- **Вес:** исправить до выпуска
- **Сторона:** `QA-Plan.md`, тест, `rework/Retired.md` (колонка R9 sweep)
- **Слой:** QA §6 (BASE), `Exceptions/SourceInvariantTests.cs`, `Retired.md`
- **Доказательство:** `QA-Plan.md:172` BASE-01 и `:175` BASE-04 — `[x]`, называют `Api/ExistingInstanceTests`; `:173` BASE-02 — «replaced by Api/StreamExtensionsTests»; `:178`, `:180` — `Streams/`; ни у одного нет пометки retired. `Exceptions/SourceInvariantTests.cs:99` перечисляет `"Cache/"`. `Retired.md` строки R3 (`ExistingInstanceTests`, `StreamExtensionsTests`) и R4 (`Cache/`) записаны «clean»; строка R4 `ITypeFormatter` записана «reported», хотя совпадений больше нет.
- **Наблюдается:** отмеченные чекпойнты называют удалённые классы; тест называет несуществующую папку; реестр не отражает результат поиска.
- **Ожидается:** `Rework-Plan.md` §0.12, R9a: чистый поиск по каждой строке, допустимы только исторические документы и пометки `retired in Rn`.

### CONF-09 — файлы `internal/` без класса в шапке

- **Вес:** исправить до выпуска
- **Сторона:** `internal/performance/*.md`, `internal/audit/Problems.cs`
- **Слой:** шапки файлов
- **Доказательство:** в первых строках `performance/README.md` и `PERF-01…PERF-09` нет строки класса; `audit/Problems.cs` начинается с «SUPERSEDED — historical audit artifact», без формулы класса и без «its rules no longer apply».
- **Ожидается:** `internal/README.md` «Every file under `internal/` belongs to one class, and says which at its head»; `Rework-Plan.md` R9a.

### CONF-10 — состояние переделки описано устаревшим

- **Вес:** исправить до выпуска
- **Сторона:** `rework/Rework-Plan.md` (Progress), `CLAUDE.md`
- **Слой:** строка R9b таблицы Progress; абзац «Current state» `CLAUDE.md`
- **Доказательство:** `Rework-Plan.md:94` — R9b «gate holds — awaiting commit», без merge-коммита, хотя `16d2704` сливает `docs/v1-consumer-docs`; `CLAUDE.md:95-96` — «the architecture rework is complete but for its last reconciliation, the consumer documentation and the release audit», хотя R9a и R9b слиты.
- **Ожидается:** `Decisions.md` 9.24 — `CLAUDE.md` описывает систему как она есть; Progress — закрытые этапы с merge-коммитом.

### CONF-11 — `Development-Workflow.md` §3.3 противоречит решениям

- **Вес:** исправить до выпуска
- **Сторона:** `internal/Development-Workflow.md` (менять только по просьбе владельца)
- **Слой:** §3.3
- **Доказательство:** `Development-Workflow.md:181` «rework/r0-baseline … rework/r9-release-gate»; `:184` «после R6 v1.0.0-beta.N — формат окончательный».
- **Ожидается:** `Owner-Review.md` лог 64 — beta после R6 сознательно не ставится, первый pre-release — `v1.0.0-rc.1`; лог 50 и `Decisions.md` 9.31 — R9 = R9a…R9e на ветках `rework/r9a-reconcile`, `docs/`, `audit/`, `bugfix/`.

### CONF-12 — текст ошибки CD говорит «from any branch»

- **Вес:** исправить до выпуска
- **Сторона:** `.github/workflows/cd.yml`
- **Слой:** шаг «Extract and validate release version»
- **Доказательство:** `cd.yml:51` «pre-release, from any branch»; проверка `:84-89` требует ветку `release/*`.
- **Ожидается:** `Decisions.md` 7.2 (уточнено 2026-09-26), `Owner-Review.md` лог 41: pre-release только на `release/*`.

### CONF-13 — `FormatterRegistry` в слое форматтеров строит кодеки движка

- **Вес:** исправить до выпуска
- **Сторона:** код (размещение) либо контракт §2
- **Слой:** `Formatters/FormatterRegistry.cs` → `Engine/`, или §2 и `CLAUDE.md` называют реестр частью движка
- **Доказательство:** `FormatterRegistry.cs:13` `namespace ViShap.Viper.Formatters`; `:115-254` — `typeof(ObjectCodec<>)`, `SequenceCodec<,,,>`, `MapCodec<,,,,>`, `ScalarCodec<>`, `NullableCodec<>`, `ImmutableArrayCodec<>`, `RejectedCodec<>`, `new UnsupportedCodec<…>`.
- **Наблюдается:** зависимость из слоя Formatters вверх, в Engine.
- **Ожидается:** контракт §2 «Dependencies point strictly downwards»; схема слоёв §2 и `CLAUDE.md` (Engine над Formatters).

### CONF-14 — два примера записей сервиса сверены только с тестовым кодировщиком

- **Вес:** зафиксировано
- **Сторона:** тесты
- **Слой:** `Format/HeaderTests` или `Format/WireFormatTests`
- **Доказательство:** `Fixtures/UtilityTests.cs:287-292` сверяет `03 05 01 9A 3B C1 07` и `05 09 FF 01 04 6C 7A 34 78 E8 07` с `Wire.ChecksumRecord`/`Wire.CompressionRecord`; продовый вывод проверен через тестовый декодер (`HeaderTests.cs:193-204`, `CompressionTests.cs:246`), не байт в байт.
- **Ожидается:** skill §3.5 — ожидаемые байты теста совпадают с байтами §6 дословно.

### CONF-15 — четыре блока примеров только объявляют типы

- **Вес:** зафиксировано
- **Сторона:** `CLAUDE.md` (формулировка) либо `docs/diagnostics.md`, `CORE-README.md`
- **Доказательство:** `docs/diagnostics.md` блок 1 и `CORE-README.md` блоки 1–3 дают CS5001 в консольном проекте и собираются как библиотека; остальные 51 выполняются с кодом 0.
- **Ожидается:** `CLAUDE.md`: «Every `csharp` block … compiles and runs».

### CONF-16 — адаптеры Track B не написаны

- **Вес:** зафиксировано
- **Сторона:** бенчмарки
- **Доказательство:** `benchmarks/…/Adapters/` — `ISerializerAdapter.cs`, `ViperAdapter.cs`; ветки `benchmark/track-b-adapters` нет.
- **Ожидается:** `Decisions.md` 9.25 — адаптеры после R6. Критерием выпуска не является (`Benchmark-Plan.md` §28).

### CONF-17 — формулировка монополии на байты шире кода

- **Вес:** зафиксировано
- **Сторона:** контракт §2.3, §25 INV-2
- **Доказательство:** контракт §2.3 «`WireReader` and `WireWriter` are the only types that touch payload bytes»; при этом фазы (`ChecksumService`, `CompressionService`, `EncryptionService`, `EncodedFrame`, `SealedBody`) и `BinaryDump.ToHex` держат байты payload как непрозрачные спаны.
- **Ожидается:** формулировка о монополии на *разбор* байтов, как в `Architecture-Audit.md` §C.6.

# 12. Закрытие

R9e, ветка `audit/v1-conformance-closure`, коммит `e8af35bfbed2e75705270b17b713140f625b3142` (merge PR #21 `bugfix/v1-strict-utf8-keys` поверх merge PR #20 `bugfix/v1-audit-all`), 2026-09-29, дерево чистое. Закрытие проверено по коду и документам (`git diff 867ba6b e8af35b`, 43 файла), не по отчёту R9d.

## 12.1 Прогоны

```text
dotnet --version                                                     10.0.400
dotnet restore Viper.sln                                             ok
dotnet build Viper.sln --configuration Release --no-restore
    0 ошибок, 7 предупреждений — все в тестовом проекте (CS0414 Fixtures/Basic.cs:93, CS8631 Format/V0CorpusTests.cs:135,
    CS8604 Hostile/PropertyTests.cs:171, CS8602 Hostile/CanonicalScalarTests.cs:55 — новое, CONF-19; остальные — как в R9c);
    в Core и Serialization — ни одного, в том числе trimming/AOT
dotnet test …Serialization.Tests.csproj --configuration Release --no-build --no-restore
    Passed! Failed: 0, Passed: 2014, Skipped: 0, Total: 2014 (Api/AotAnalysisTests, Api/MemberSurfaceTests в их числе)
dotnet test …Serialization.Tests.csproj --configuration Debug
    Passed! Failed: 0, Passed: 2014, Skipped: 0, Total: 2014
dotnet pack src/ViShap.Viper.Core/…            --configuration Release --no-build --no-restore -p:Version=1.0.0-rc.1 --output <scratchpad>/artifacts
dotnet pack src/ViShap.Viper.Serialization/…   (то же)
dotnet pack src/ViShap.Viper/…                 (то же)
    ViShap.Viper.Core.1.0.0-rc.1.nupkg           <version>1.0.0-rc.1</version>, README CORE-README.md (6 891 байт)
    ViShap.Viper.Core.1.0.0-rc.1.snupkg
    ViShap.Viper.Serialization.1.0.0-rc.1.nupkg  <version>1.0.0-rc.1</version>, README SERIALIZATION-README.md (6 851 байт)
    ViShap.Viper.Serialization.1.0.0-rc.1.snupkg
    ViShap.Viper.1.0.0-rc.1.nupkg                <version>1.0.0-rc.1</version>, README METAPACK-README.md (3 982 байт)
    — ровно три .nupkg и два .snupkg, имена ожидаемые; артефакты — вне репозитория
dotnet run --project benchmarks/… -c Release -- --verify          "351 pairs, 0 failed." Corpus: 27 datasets
dotnet run --project benchmarks/… -c Release -- --smoke           "Smoke: 1158 benchmarks, 0 failed."
```

Фикстуры `Fixtures/Wire/*.bin` после R9c не менялись (`git log 867ba6b..HEAD -- …/Fixtures/Wire/` пуст).

## 12.2 Находки R9c

| ID | Итог | Доказательство |
|---|---|---|
| CONF-01 | закрыта — решение владельца: вариант (а), чтение канонично (`Owner-Review.md` лог 65) | `BigIntegerFormatter.Read` отвергает пустой и не кратчайший блоб (`PrimitiveFormatters.cs:258-269`); `VersionFormatter.Read` требует `version.ToString() == text`, `CultureInfoFormatter.Read` — `culture.Name == name`, `BitArrayFormatter.Read` — нулевые биты заполнения (`SystemFormatters.cs`); контракт §22.4 (строки `BigInteger`, `Version`, `CultureInfo`, `BitArray`) и §25 INV-9 дополнены; `docs/supported-types.md`; QA HST-42…HST-45 → `Hostile/CanonicalScalarTests` |
| CONF-02 | закрыта — решение владельца: гибрид (лог 66) | `CountKind.KeyedFields` в `Io/ElementCount.cs`, фабрика проверяет `MaxKeyedFields` и списывает бюджет keyed-полей; `ObjectCodec.cs:252` — `reader.ReadCount(CountKind.KeyedFields, …)`; записи сервисов ограничены заголовком 4 096 байт — записано в контракте §2.3 (строки 108-116), §5.9, §25 INV-4; `CLAUDE.md` барьер 2; LIM-52 → `Limits/InvariantStructureTests` (`KeyedObject_TakesItsFieldCountAsAValidatedCount`, `Formatters_ReadNoRawCount`) |
| CONF-03 | закрыта | LIM-52…LIM-59 в `Limits/InvariantStructureTests` — отражение и поиск по исходникам текущих типов: INV-7 (нет статического поля с алгоритмом или фабрикой, по обеим сборкам), INV-8 (нет разрешения типа по имени, `WireWriter` не берёт `Type`), INV-9 (строгий UTF-8 в `WireReader`/`WireWriter`, нет `Encoding.UTF8` ниже пайплайна), INV-10 (бросаются только таксономия и стандартные исключения §8.10), INV-11 (`SecretKey` только через `CopyFrom`, обнуление только у владельцев), INV-14/15 (AAD — байты заголовка, в приёмник пишет только готовый кадр), INV-18 (обе версии через `Graph.WriteRoot`); колонка «Held by» §25 и итог «Architecture» QA обновлены |
| CONF-04 | закрыта | `ISequenceShape.TryGetSpan` (`Formatters/Shapes.cs`), `ListShape<T>.TryGetSpan` через `CollectionsMarshal.AsSpan` (`SequenceShapes.cs:61`), `SequenceCodec.WriteBody` пишет из span (`SequenceCodec.cs:24`); `CLAUDE.md` «Adding a formatter» обновлён |
| CONF-05 | закрыта | контракт §3.6 «Member surface» (строка 462) — каждый публичный член каждого публичного типа, включая `Peek(…, SerializationLimits)`, `SerializationLimits.Validate()` с описанием семантики, свойства опций, `BinaryHeaderInfo`, `SecretKey`, конструкторы провайдеров; API-29 → `Api/MemberSurfaceTests` читает список из самого контракта и сверяет в обе стороны (`EveryPublicMember_IsListedInTheContract`, `EveryListedMember_Exists`, `TheList_IsFoundAndNotEmpty`) |
| CONF-06 | закрыта — решение владельца: строгая запись, `BinaryFormatException` (лог 67) | `WireWriter.EncodedLength` на строгом `UTF8Encoding(throwOnInvalidBytes: true)` для всех трёх путей записи строк (`Io/WireWriter.cs`); контракт §22.1 (строка 2340), `docs/supported-types.md`; WF-38 → `Hostile/CanonicalScalarTests`. Дополнительно по поручению владельца (лог 69): строгий UTF-8 для key id в `HkdfKeyProvider.Resolve` (`BinaryEncryptionKeyException`) и для строк заголовка (`BinaryConfigurationException`), контракт §8.1, §13.2, `docs/algorithms-and-keys.md`, ENC-31 → `Algorithms/HkdfKeyProviderTests` |
| CONF-07 | закрыта | ENC-30 → `EncryptionTests.Serialize_ACipherRaisingCryptographicException_ThrowsEncryptionWithTheCauseInside`: `BinaryEncryptionException`, `CryptographicException` внутри, приёмник пуст |
| CONF-08 | закрыта | QA BASE-01/02/04/07/09 помечены `retired in R3`/`retired in R2` с заменой; `SourceInvariantTests.cs:99` без `Cache/`; строки `Retired.md` R3 и R4 (`ITypeFormatter` — «clean») исправлены. Повторный поиск строк этих строк реестра по `src/`, `tests/`, `benchmarks/`, `docs/`, `.claude/`, `CLAUDE.md` и живым документам `internal/`: попадания только в QA-плане с пометками retired; в `tests/ViShap.Viper.AotConsumer/bin/` — устаревший выход сборки от 02:09 (игнорируется `.gitignore:33`, в репозиторий не входит), текущий `ViShap.Viper.Serialization.xml` называет только `ViShap.Viper.Engine.FormatterRegistry` |
| CONF-09 | закрыта | строка `**Class: plan.**` в третьей строке `performance/README.md` и `PERF-01…PERF-09`; `audit/Problems.cs` начинается с «Class: historical … whose rules no longer apply» |
| CONF-10 | закрыта | `Rework-Plan.md` Progress: R9b `closed` / `16d2704`, R9c `closed` / `867ba6b`; абзац «Current state» `CLAUDE.md` описывает R9a–R9c как сделанные и R9e как следующий шаг. Строка R9d после слияния снова отстала — CONF-18 |
| CONF-11 | закрыта — по поручению владельца (лог 68) | `Development-Workflow.md` §3.3: этапы до `rework/r8-generator-ground`, R9 — пять подэтапов с их ветками, «после R6 формат окончательный, но beta не ставится», первый pre-release — `v1.0.0-rc.N` после R9e |
| CONF-12 | закрыта | `cd.yml:51` — «pre-release, from a release/* branch» |
| CONF-13 | закрыта — решение владельца: перенос (лог 68) | `Engine/FormatterRegistry.cs`, `namespace ViShap.Viper.Engine`; в `Formatters/` нет ни одной ссылки на кодеки движка и на реестр (поиск `ObjectCodec|SequenceCodec|MapCodec|ScalarCodec|NullableCodec|ImmutableArrayCodec|RejectedCodec|UnsupportedCodec|FormatterRegistry` пуст); `CLAUDE.md`, `PublicSurfaceTests`, строка `Retired.md` R9d |
| CONF-14 | закрыта | HDR-33 → `HeaderTests.Serialize_ChecksumAndCustomCompression_WritesTheDocumentedRecords`: продовый вывод сверен байт в байт с `03 05 01` + CRC-32 и `05 09 FF 01 04 6C 7A 34 78 E8 07` из §22 |
| CONF-15 | закрыта | `CLAUDE.md` формулирует правило так, как устроены примеры: блок с верхнеуровневыми операторами компилируется и выполняется, блок только с объявлениями — компилируется как библиотека |
| CONF-16 | остаётся зафиксированной — не критерий выпуска | адаптеры Track B не написаны; `Benchmark-Plan.md` §28 |
| CONF-17 | закрыта | контракт §2.3 (строки 100-102) и `CLAUDE.md` барьер 1: «the only types that parse or produce payload bytes», фазы, готовый кадр и hex-вид дампа держат байты как непрозрачные спаны |

## 12.3 Новые находки

### CONF-18 — строка R9d в Progress после слияния не обновлена

- **Вес:** зафиксировано
- **Сторона:** `rework/Rework-Plan.md` (Progress)
- **Слой:** строка R9d таблицы Progress
- **Доказательство:** `Rework-Plan.md:96` — R9d «gate holds — awaiting commit», колонка merge-коммита пуста, хотя `9d8e8ae` сливает `bugfix/v1-audit-all`, а `e8af35b` — `bugfix/v1-strict-utf8-keys` (вторая ветка в строке не названа).
- **Наблюдается:** та же отстающая строка, что в CONF-10, у следующего подэтапа.
- **Ожидается:** Progress — закрытые этапы с merge-коммитом. Вес ниже, чем у CONF-10: у отчёта R9e нет права менять план, а строку R9d/R9e по образцу R9a–R9c заполняет следующий шаг; ни потребитель, ни выпуск от неё не зависят. Заполнить вместе со строкой R9e при слиянии этого отчёта или в первой ветке после rc.

### CONF-19 — новое предупреждение компилятора в тестовом проекте

- **Вес:** зафиксировано
- **Сторона:** тесты
- **Слой:** `Hostile/CanonicalScalarTests.cs:55`
- **Доказательство:** `warning CS8602: Dereference of a possibly null reference` в сборке Release — `Serializer.Deserialize<BitArray>(canonical).Length`.
- **Наблюдается:** к шести предупреждениям тестового проекта, записанным в R9c, добавилось седьмое; пакеты предупреждений не имеют.
- **Ожидается:** §3.7 требует чистоты только от двух пакетов; записано как наблюдение.

## 12.4 Повторный поиск по затронутым файлам и принципы

- `Retired.md` (§3.4) по 43 файлам R9d: новая строка R9d (`Formatters/FormatterRegistry`) — чисто; прежние строки — попаданий вне допустимых нет (см. CONF-08).
- `SerializationLimits` ниже `Pipeline/` по-прежнему не встречается (LIM-43 зелёный, список папок без `Cache/`). Перенос реестра в `Engine/` убрал единственную ссылку вверх из `Formatters/`.
- `Encoding.UTF8` в `src/` остался один раз — `Diagnostics/BinaryDump.cs:207`, декодирование собственного JSON дампа, не байтов payload; ниже пайплайна его нет (LIM-55).
- Строгие кодировщики добавлены в `WireWriter`, `BinaryFormatHeaderV1` и `HkdfKeyProvider`; каждое новое исключение — из таксономии (LIM-56 зелёный) и записано в контракте (§8.1, §13.2, §22.1).

## 12.5 Итог

Все находки веса «блокирует rc» и «исправить до выпуска» закрыты; открыты только CONF-16, CONF-18, CONF-19 веса «зафиксировано». Решений владельца не требуется. **rc можно ставить.**
