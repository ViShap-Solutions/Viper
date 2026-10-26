# ViShap.Viper — аудит соответствия `v1.0.0-rc.2` (P8)

**Класс: исторический.** Запись независимого аудита соответствия `rc.2`, выполненного по брифу
`internal/rc2/Audit-Brief.md`. Систему описывают `System-Contract.md` и документы, которые он называет;
этот отчёт фиксирует, что было доказано и что найдено на проверенном коммите, и никакого правила не задаёт.

---

## 1. Вердикт

**rc.2 не может быть помечен тегом.**

- Одна находка веса «блокирует rc» требует решения владельца по существу: предложения
  `SupportedTypes` действуют на весь процесс, а `Union<T>` и `Collection<TCollection, T>` принимают
  произвольные плечи и делегаты, поэтому контекст, который лишь *создан*, меняет байты и чтение любого
  сериализатора процесса (RC2-01). Контракт §14.5/§18, INV-20, обоснование D3 и `docs/writing-contracts.md`
  утверждают обратное; проба это опровергает.
- Вторая — решение S5-a записано как `PrivateAssets="all"`, а построено `PrivateAssets="none"`, и только
  так выполняется «one install gives everything» той же строки (RC2-03).
- Одна находка веса «блокирует rc» ясна по стороне и слою: поведение исключения из собственного кода
  поставленного контракта и `null` из `Create` контрактом не описано, и `NullReferenceException`
  выходит наружу (RC2-02).
- Сборка без предупреждений, оба набора зелёные в Debug и Release (2 265 + 46), `--verify`, `--smoke`,
  `AotAnalysisTests`, CI `native-aot` и локальная публикация native AOT — зелёные. Зелёный набор вердикта
  не определяет.

## 2. Объём и доказательства

| | |
|---|---|
| Проверенный коммит | `97fba7373649a0bcf38a6bfc84d1e8c6d9506276` (`release/v1.0.0` HEAD, merge PR #32 `docs/rc2-docs`), совпадает с `origin/release/v1.0.0` |
| Ветка | `audit/rc2-conformance`, дерево чистое до и после всех прогонов |
| Дата | 2026-10-26 |
| Дельта | `git diff c728c99..HEAD`: 26 коммитов, 210 файлов, +25 991 / −1 070; `src/` — 56 файлов (P1–P4 `eac6c0e`, P4b `89036bf`, P4d `6b298ee`, P7 `f50928d`) |
| Бриф | одобрен 2026-10-26, `RC2-Plan.md` §3.9, строка «P8 — the auditor brief» |
| Пробы | в scratchpad сессии; ничего в `src/`, `tests/`, `benchmarks/` не менялось |

Команды §4.9 брифа и их результаты:

| Команда | Результат |
|---|---|
| `dotnet build Viper.sln --configuration Release` | 0 предупреждений, 0 ошибок (также Debug: 0 / 0) |
| `dotnet test …Serialization.Tests… --configuration Debug` | пройдено 2 265, не пройдено 0 |
| `dotnet test …Serialization.Tests… --configuration Release` | пройдено 2 265, не пройдено 0 |
| `dotnet test …Generator.Tests… --configuration Debug` | пройдено 46, не пройдено 0 |
| `dotnet test …Generator.Tests… --configuration Release` | пройдено 46, не пройдено 0 |
| `dotnet run --project benchmarks/… -c Release -- --verify` | `405 pairs, 0 failed`; `Market: 304 pairs, 140 probes, 0 schema failures, 0 failed`; exit 0 |
| `dotnet run --project benchmarks/… -c Release -- --smoke` | `Smoke: 2477 benchmarks, 0 failed.`, exit 0 |

Дополнительно (§4.7 брифа): CI-прогон на проверенном коммите — `Viper CI`, run `40313175409`, push
`release/v1.0.0`, `success`; задания `Native AOT` — success, `Build and test` — success
(`https://github.com/ViShap-Solutions/Viper/actions/runs/40313175409`). Локально
`dotnet publish tests/ViShap.Viper.AotConsumer -c Release -r win-x64`: 0 предупреждений `IL####`;
вывод нативного исполняемого файла и `dotnet run` того же проекта — по 48 строк, идентичны, последняя
`all checks passed`. (Первая попытка в Git Bash упала на `vswhere.exe` вне `PATH` — свойство среды;
повтор с каталогом установщика Visual Studio в `PATH` прошёл.)

Пробы, на которые ссылаются находки:

```text
probe union-baseline   plain Base(A): 4253455201000006010107000000   plain MyColl: 1,2
probe union-hostile    (new HostileContext() создан и ни в какие опции не передан)
                       plain Base(A): 4253455201000006010907000000   ← тег 01 стал 09
                       plain MyColl: 101,102                          ← делегат add контекста
                       plain Serialize<Plain>: BinaryTypeException: Runtime type 'Plain' is not allowed
                                               for declared type 'Plain'
probe create-null      System.NullReferenceException: Object reference not set to an instance of an object.
probe contract-throws  System.InvalidOperationException: from Write; stream length after: 0
                       System.InvalidOperationException: from ReadPositional
probe offer-no-contract (рефлексия выключена, Add(SupportedTypes.Object<Box>()) без контракта)
                       BinaryConfigurationException: 'Box' is not offered by any BinarySerializerContext
                       this process has created, and the reflection path is switched off …
probe boxing           reflected/generated write 272/272 B/op, read 72/72 B/op (struct-граф с enum и int?)
probe captured         [BinaryContract] с захваченным параметром первичного конструктора:
                       reflected = generated = 425345520100000A02010400000003000000
probe consumer         пакеты 1.0.0-rc.2 из scratchpad, только ViShap.Viper, PublishAot=true:
                       ViperReflectionEnabled=[false]; switch found=True enabled=False;
                       генератор работает (Ctx.Default.Order), тип вне контекста — BinaryConfigurationException
probe doc-output       9 блоков docs/ и GENERATOR-README с комментарием вывода: все 9 печатают ровно комментарий
```

## 3. Решения

| Решение | Как решено? | Доказательство | Находка |
|---|---|---|---|
| N1a / N1b — B + M1 | да | `TypeContract.cs`: `ReadPositional`, `ReadKeyed`; `MemberWriter.Member(value)` / `Member(key, value)`; `MemberReader.Member<T>()`; §3.6 строки 694–696, 772–774 | — |
| S1 — a; базовый тип — внутренний интерфейс | да | `TypeContract<T> : ITypeContract` (`ITypeContract` — `internal`); публичны ровно пять типов; `PublicSurfaceTests` | — |
| S2 — c; контекст; покрытие типов | да | `BinarySerializerContext` abstract, `protected Add<T>`, `Snapshot()` в `Build()`; `[BinaryContext]` в Core; NAOT-06 | — |
| S3 — b | да | `Planner.cs:681–697` — `[UnsafeAccessor]` для недоступных аксессоров | — |
| S4 — A, затем «S4 revisited» — B через фасад имён | да | `SupportedTypes`, `BinaryTypeInfo<T>`, `ReflectionPath.IsEnabled`; 26 + 26 входов | — |
| S5 — a | **нет, как записано** | решение: «`PrivateAssets="all"` … One install gives everything»; `src/ViShap.Viper/ViShap.Viper.csproj` — `PrivateAssets="none"`; nuspec `include="All"` | **RC2-03** |
| Invalid description | да | `TypeContract` ctor и `MemberDescription` ctor — `BinaryConfigurationException`; CTX-06 | — |
| P1–P4 на одной ветке | да | `feature/generator`, merge `4604960` | — |
| F1, T1, A1 — отложены | да, ничего не построено наполовину | поиск `fingerprint`, `WithTrace`, `TraceSink`, `Zstandard`, `Zstd`, `Lz4`, `GcmSiv` в `src/`, `docs/`, README, контракте — пусто; §3.6 `CompressionAlgorithm` = None/Deflate/Brotli/Custom; есть IDEA-01, 02, 03, 11 | — |
| B1 — a | да | ни одного прилагательного о производительности в `docs/`, README, описаниях пакетов, XML | — |
| Engine fast path; P4b item 1 — A; items 2, 3 — PERF-11 | да | `ObjectCodec.ContractChoice` (одна публикация пары); `PERF-11-member-call-dispatch.md` | — |
| B2 — без заседания по PERF-02…09 | да | файлы остаются Open | — |
| Workflow §3.3 не изменяется | да | `Development-Workflow.md` в дельте не менялся | — |
| P4c — D1 | да | `SupportedTypes`/`SupportedType`, `protected Add(SupportedType)` | — |
| P4c — D2 | да (as built) | одна таблица `BuiltInCodecs.Scalars` | — |
| P4c — D3 | **реализовано, но обоснование решения не выполняется** | D3: «an offer of a different value is not expressible»; `SupportedTypes.Union<T>` и `Collection<,>` принимают произвольное содержимое, проба `union-hostile` | **RC2-01** |
| P4c — D4 | да (as built), с тем же следствием | `Union<T>(params (int, Type)[])`; плечи не сверяются с `[BinaryUnion]` | RC2-01 |
| P4c — D5 | да, с тем же следствием | `Collection<TCollection,T>(create, add)` | RC2-01 |
| P4c — D6 | да | `ReflectionPath.cs`; `buildTransitive/ViShap.Viper.Serialization.targets`; проба consumer | — |
| P4c — D7 | да | `BinaryTypeInfo<T>.Require`; `TypeInfo<T>`, `TryGetTypeInfo<T>`; NAOT-09, NAOT-10 | — |
| P4c — D8 | да | 22 + 4 перегрузки с `BinaryTypeInfo<T>` (`BinarySerializer.TypeInfo.cs`, `BinaryFormatDumper.cs`); §3.6 | — |
| P4c — D9 | да | VPR021, `CompilerVisibleProperty`; GEN-02 | — |
| P4c — D10 | да | в `src/` нет `UnconditionalSuppressMessage`, `SuppressMessage` с `IL`, `#pragma warning disable IL`; атрибуты — ровно 22 + 4 входа и `FormatterRegistry`, `ReflectedContract`, `TypeContractCache.Get` | — |
| P4c — D11 | да, кроме доказательства INV-20 для рукописного предложения | NAOT-01…14; NAOT-02 проверяет только плечи, равные атрибутам | RC2-01 |
| P4c — D12 | да | `ci.yml` job `native-aot`; run `40313175409` | — |
| P4d — A + C (R-07, PERF-12) | да | предложения выполняются в конструкторе контекста (`Add(SupportedType)` → `Offer()`); `PERF-12` | — |
| P8 — аудитор A; бриф | да | `.claude/skills/viper_conformance_auditor`, `rc2/Audit-Brief.md` | — |
| После rc.2 — B | да | в плане нет пункта и ветки потребителя | — |

## 4. Инварианты

| INV | По построению (как) | Раздел | Структурный тест | Находка |
|---|---|---|---|---|
| INV-1 | `OperationState` — struct, создаётся в каждом публичном методе и передаётся `ref`; шов контракта не видит его | §2.2 | LIM-51 | — |
| INV-2 | `WireReader`/`WireWriter` — `ref struct`; `MemberWriter`/`MemberReader` — публичные `ref struct` с приватными конструкторами и единственным публичным методом `Member` | §2.3, §14.3 | LIM-44, LIM-47, LIM-49 (`StructuralBarrierTests.TypeContract_IsHandedOnlyTheMemberSurfaces` и проверка `["Member"]`) | — |
| INV-3 | неизменно с rc.1; дельта в кодеках — по две строки | §6, §17 | TYP-03, SRC-10, LIM-42 | — |
| INV-4 | `ElementCount` — единственная фабрика; генератор не выпускает цикл (GEN-09 по регулярным выражениям) | §2.3 | LIM-40, LIM-52 | — |
| INV-5 | шейпы и кодеки `internal`; контракт получает только `MemberWriter`/`MemberReader`; `SupportedType` непрозрачен | §2.4, §14.1, §14.5 | LIM-49, NAOT-14 | — |
| INV-6 | неизменно | §2.5 | CMP-12 | — |
| INV-7 | буквально держится: нет статического поля с алгоритмом (LIM-53). §14.5 ссылается на INV-7, утверждая, что предложение — не реестр, меняющий поведение; это не так | §4.1, §14.5, §18 | CAT-06, LIM-53 | RC2-01 |
| INV-8 | union-карта несёт теги и `Type`, на провод идут только теги | §15 | PM-14, LIM-54 | — |
| INV-9 | неизменно; дельта не трогала `Io/` и форматтеры, кроме `SequenceShapes` (без провода) | §11, §22 | WF/HDR, KEY-23, HST-40…45, WF-38, LIM-55 | — |
| INV-10 | для шва не держится: `null` из `Create` даёт `NullReferenceException` | §8, §9 | EXC-14…20, REF-19, LIM-56 | RC2-02 |
| INV-11 | неизменно | §13.2 | ENC-12, ENC-14, LIM-57 | — |
| INV-12 | генерированный план равен отражённому: `ByteIdentityTests`, CONF-01…07 трижды (отражение, рукописные, сгенерированные) | §14.1, §14.4 | CONF-01…07, GEN-04, GEN-11, CTX-08 | — |
| INV-13 | `MemberWriter.Begin` берёт `MaxKeyedFields` из `state.Limits`; ничто из шва не меняет лимиты | §5 | CFG-09, OPT-08 | — |
| INV-14 | неизменно | §13.1 | ENC-05, ENC-06, LIM-58 | — |
| INV-15 | исключение контракта поднимается до записи в назначение (проба: `stream length after: 0`) | §2.6 | STR-30, LIM-58, CTX-07 | — |
| INV-16 | в `Engine/`, `Formatters/` нет `async`; единственный `async` в `Io/PayloadBuffer.WriteToAsync` — край кадра, не изменён | §2.4, §3.5 | LIM-50 | — |
| INV-17 | сгенерированный код вызывает обобщённые `Member<T>`; бокс — только `WriteBoxed`/`ReadBoxed` полиморфного слота. Тест на путь через контекст отсутствует | §2.4, §15 | TYP-02 (только отражённый путь) | RC2-06 |
| INV-18 | неизменно | §10.2, §22.7 | V0-19, V0-21, KEY-18, LIM-59 | — |
| INV-19 | `FormatterCache<T>` (`Codec.cs:46`), `ObjectCodec.Reflected`/`RuntimeContract` (`ObjectCodec.cs:69, 83`) за `ReflectionPath.IsEnabled`; `TypeContractCache.GetUnion` сам не охраняется, но достижим только из конструктора `ObjectCodec()` без карты, который строит лишь `FormatterRegistry` (`FormatterRegistry.cs:65`) | §3, §14.5 | NAOT-12, NAOT-13, CI `native-aot` | — |
| INV-20 | **не держится по построению**: `Union<T>` и `Collection<,>` принимают содержимое, которого путь рефлексии не строит; первое предложение действует на весь процесс | §14.5 | NAOT-01, NAOT-02, NAOT-06 — проверяют только равное содержимое | **RC2-01** |

## 5. Согласованность

**Контракт ↔ `src/`.** Прочитаны §2–§3.6, §4.1, §14, §18, §21.3, §23–§25 против кода дельты.
Совпадает: поверхность §3.1/§3.6 (`MemberSurfaceTests` зелёный), отказ `Build()` для
`RequireGeneratedContracts` без `WithContracts` (`BinarySerializerOptionsBuilder.cs:520`), проверка
контекста `BinaryTypeInfo<T>.Require` до назначения, снимок контекста при первой сборке, отказ
дубликатов и позднего `Add`, защита копии и `default` у `MemberWriter`/`MemberReader`, проверка каждого
вызова контракта против описания (`MemberWriter.Next`, `MemberReader.Member`, ключевой цикл
`ObjectMembers.ReadKeyed`). Расхождения: RC2-01, RC2-02, RC2-04, RC2-05, RC2-12.

**Правило ↔ чекпойнт ↔ тест** (§15.2, §15.3, §33, §34 и изменённые). Каждый чекпойнт называет
существующий тест; все зелёные. Тест утверждает меньше заявленного: GEN-09 («no tag» — сгенерированный
контекст выпускает теги union-карты, RC2-05), TYP-02 (нет пути через контекст, RC2-06), NAOT-02 (только
плечи, равные атрибутам, RC2-01). DOC-01…04 проверяют компиляцию и выполнение, но не напечатанное;
отдельная проба показала, что все 9 блоков с комментарием вывода печатают его.

**Бенчмарки.** Имена сьютов, адаптеров и проб Track B и graceful stop из `Benchmark-Plan.md` и
`Benchmark-Graceful-Stop.md` существуют в `benchmarks/`; ключи `--track`, `--report`, `--verify`,
`--market`, `--smoke` есть в `Program.cs`. P6 не правил `src/`: `git log c728c99..HEAD -- src` даёт
только `eac6c0e`, `89036bf`, `6b298ee`, `f50928d`; коммиты P6 — `b4608e6`, `53c5fb0`, `45e9d0a`.
Висячие ссылки на §22.8 — RC2-07.

**Поверхность ↔ §3 ↔ XML ↔ `docs/`.** `PublicSurfaceTests`, `MemberSurfaceTests` зелёные. XML
документация `src/` не ссылается на `internal/`, `§`, INV, стадии и историю (поиск пуст). Каждый новый
публичный тип назван на странице `docs/`, кроме `SupportedType` (RC2-13). `docs/writing-contracts.md:437`
утверждает, что ничто добавленное контекстом не меняет другую конфигурацию, — RC2-01.

## 6. Ничего не осталось

**`RC2-Plan.md` §7** — каждое значение «Searched as» по `src/`, `tests/`, `benchmarks/`, `docs/`,
README, `CLAUDE.md`, `.claude/skills/`, `internal/`:

| Строка | Попадания вне исторических документов | Допустимо? | Находка |
|---|---|---|---|
| P1 `override void Read(`, `.Read(ref members` | только строка самой ведомости | да | — |
| P1 `ReadField\b`, `ReadField_` | `RC2-Plan.md` §2, §3 (запись решения N1), строка ведомости | да | — |
| P1 `.Field(`, `Field<TMember>` | `trace.Field`, `MemberReader.Field`, `Expression.Field` | да — ведомость их называет | — |
| P1 `.Value<`, `Value<TMember>` | комментарий `WireTrace.cs:27` (`[index].Value`) | да | — |
| P1 `ReadFieldFrom`, `.ReadFrom(ref reader` | `BinaryFormatHeaderV1.ReadFrom` | да | — |
| P1 `WrongLayout(` | определение и один вызов в `MemberWriter` | да | — |
| P2 `TypeContract contract`, `typeof(TypeContract)`, `(TypeContract)` | только `ITypeContract contract` | да | — |
| P2 `ViShap.Viper.Engine.TypeContract`, `…Engine.Member` | `PublicSurfaceTests.cs:217` — `…Engine.TypeContractCache` | да (другой тип) | — |
| P2 `Contracts.Mismatch/WrongLayout/TooManyCalls` | нет | — | — |
| P2 `FoldsNull =>` | нет | — | — |
| P3 `ProductionFiles` | — | — | — |
| P1 тест `APositionalMemberUnderAKeyedLayout` | `ContractCallTests.cs:107` `Write_APositionalMemberUnderAKeyedLayout_ThrowsType` | да, но строка записана «clean» | RC2-08 |
| P4d `FormatterRegistry.Scalars`, `registry.GetField("Scalars"` | `Native-AOT-Design.md:106` — текст решения D2 (с as-built §10) | да (запись решения) | — |
| P4d `FormatterRegistry.Shapes`, `registry.GetField("Shapes"` | нет | — | — |
| P4d `AotConsumer/Program.cs`, `never run` | `never run` — прозa о `Lazy<T>` и `NoChecksum` | да | — |
| P4c/P4d `KnownType` | `UnionDeclarationTests`, `Fixtures/Unions` — «known type» плеча; `Native-AOT-Design.md:80` | да | — |

**`rework/Retired.md`** — все строки перепроверены скриптом по файлам дельты; попадания в QA §30, в
маркерах `retired in Rn`, в `Baselines/` и в выводах `Measurements/` — допустимы, как записано в
строках. Не так, как записано:

| Строка | Попадание | Находка |
|---|---|---|
| R9a `§22.8` — «clean» | `Benchmark-Plan.md:344` (PROF-02), `:542` (SIZE-02), `:638` (DIFF-01), `benchmarks/…/Reporting/SizeReport.cs:119`; §22.8 в контракте нет, конверт V0 — §22.7 | RC2-07 |
| R4 `Enum.ToObject` — «clean» | `benchmarks/…/DataSets/Projection.cs:38` (добавлен в P6, BCL-вызов в проекции бенчмарка) | RC2-08 |
| R3 `FromStream` — «clean — local helper of AllocationTests» | `Generator.Tests/Fixtures/Harness.cs:104` (`LoadFromStream`), `MarketBenchmarks.cs:91` (`DeserializeFromStream`) | RC2-08 |

**За пределами ведомостей.** Найдено то, что rc.2 заменил, а ни одна строка не называет:

- «три пакета» в §24 контракта и в QA (`dotnet pack succeeds for all three packages`) — их четыре (RC2-04);
- §24 «every public entry point that encodes or decodes a caller's type carries
  `[RequiresUnreferencedCode]`…» — правило rc.1; теперь только 26 без информации о типе (RC2-04);
- §18 «`FormatterCache<T>` = the codec of T, resolved once by FormatterRegistry», §2 «`ReflectedContract<T>`
  in v1.0», §14.1 «in v1.0 it is built by reflection» — формулировки rc.1 (RC2-04);
- `CLAUDE.md` «`Benchmark-Graceful-Stop.md` — … not started» (RC2-09).

## 7. Wire

- `git log c728c99..HEAD -- tests/ViShap.Viper.Serialization.Tests/Fixtures/Wire` — пусто. История
  каждого из 13 `*.bin`: последнее изменение — `31074d0` (2026-09-29, R6, единственная перезаморозка);
  `CLAUDE.md` фиксирует исключение и правило «never regenerated».
- `Format/CompatibilityTests`, `Fixtures/Compatibility.cs`, `Fixtures/Wire.cs` в дельте не менялись;
  `Wire.cs` кодирует своим `BinaryWriter` и не ссылается на `WireWriter`, `WireReader`,
  `BinarySerializer`, `ViShap.Viper.Io/Engine/Pipeline`.
- Сгенерированные и рукописные контракты пишут байты отражения: `ByteIdentityTests` (GEN-04),
  `GeneratedCompatibilityTests` (GEN-07), CONF-01…07 через три шва (GEN-11, CTX-08), CTX-01…08 — зелёные;
  AOT-потребитель читает и записывает обратно замороженные полезные нагрузки при выключенной рефлексии
  (вывод нативного и JIT-прогона идентичен).
- Нарушение байтов вне правил провода — RC2-01: созданный контекст меняет тег union для сериализатора
  без контекста (`…06010107…` → `…06010907…`).

## 8. Классы документов

| Файл | Класс в шапке | Класс в `CLAUDE.md` / `internal/README.md` | Находка |
|---|---|---|---|
| `System-Contract.md` | normative | normative / normative | — |
| `QA-Plan.md`, `Benchmark-Plan.md` | plan | plan / plan | — |
| `Benchmark-Graceful-Stop.md` | plan | plan, «not started» / plan, «done (rc.2 P6)» | RC2-09 |
| `rc2/RC2-Plan.md`, `rc2/Native-AOT-Design.md`, `rc2/Audit-Brief.md` | plan | **нет** / plan | RC2-09 |
| `ideas/*`, `performance/*` | plan | plan / plan | — |
| `Development-Workflow.md` | действующий | operational / operational | — |
| `README.md` | operational | operational / operational | — |
| `Architecture-Audit.md`, `Audit-Refactor.md`, `Audit-Closure.md`, `Audit-Future.md` | исторический | historical / historical | — |
| `Audit-Conformance.md` (R9) | исторический | **нет** / **нет** | RC2-09 |
| `audit/*`, `rework/*` | historical / исторический | historical / historical | — |

Живой документ, ссылающийся на исторический как на источник правила: не найдено (бриф ставит `rework/`
в порядок доказательств до `v1.0.0` по решению §3.9 «P8 — the auditor»).

## 9. Готовность к выпуску

| Проверка | Результат |
|---|---|
| Сборка Release / Debug | 0 предупреждений, 0 ошибок во всех проектах |
| Serialization.Tests Debug / Release | 2 265 / 2 265, 0 не пройдено |
| Generator.Tests Debug / Release | 46 / 46, 0 не пройдено |
| `--verify` | 405 пар, 0 failed; Market 304 пары, 140 проб, 0 failed |
| `--smoke` | `Smoke: 2477 benchmarks, 0 failed.`, exit 0 (26 мин 21 с) |
| `Api/AotAnalysisTests` | зелёный (в наборе) |
| CI `native-aot` на `97fba73` | success, run `40313175409` |
| Локальная native AOT публикация win-x64 | 0 `IL####`, вывод идентичен JIT (48 строк) |
| Упаковка (предварительно, не CD-прогон) | `dotnet pack` ×4 с `1.0.0-rc.2` в scratchpad: четыре `.nupkg`, два `.snupkg`; Generator — только `analyzers/dotnet/cs/ViShap.Viper.Generator.dll`, `developmentDependency=true`, `lib/` нет; Serialization несёт `buildTransitive/ViShap.Viper.Serialization.targets`; каждый пакет со своим README |

## 10. Принципы

- **Зависимости вниз; `SerializationLimits` ниже `Pipeline/`.** Новые типы шва (`MemberWriter`) читают
  `state.Limits.MaxKeyedFields` из `OperationState`, как и до rc.2 (`MemberWriter.cs:44–49`); тип
  `SerializationLimits` в `Engine/Contracts/` не упоминается. Генератор не ссылается на сборки времени
  выполнения и сам не упоминается ни одной (GEN-10).
- **У лимитов один владелец.** Контекст, контракт и `SupportedType` не несут лимитов; ничего прочитанного
  не расширяет бюджет. Держится.
- **Монополия байтов.** Шов не даёт ни байта, ни позиции; `MemberReader` в ключевом поле работает над
  `WireReader.SliceField`. Держится.
- **Проверенные счётчики.** Генератор не выпускает цикл, длину и счёт (`Emitter.cs` прочитан целиком).
  Держится.
- **Обход принадлежит движку.** Держится для контракта; для `SupportedTypes.Collection` делегаты
  потребителя работают внутри цикла движка как строитель — это допустимо (форма), но содержимое
  делегата не сверяется с правилом (RC2-01).

### Генератор (бриф §4.3)

- `Emitter.cs` прочитан целиком: прямолинейный код, `switch` по известным ключам, никакого цикла, длины,
  счёта, предела, рамки и байта. Тег выпускается как данные union-карты в конструкторе контекста
  (`Snapshots/SupportedTypes.verified.txt:26`) — по решению D4; §14.4, GEN-09 и `CLAUDE.md` говорят
  «no tag» (RC2-05).
- Таблицы равны `BuiltInCodecs`/`TypeRules` (GEN-10). Сбор кандидатов `Planner.Candidates` сверен с
  `ReflectedContract.Candidates` правило за правилом: свойства чтения-записи без индексатора, каждое
  переопределение один раз у самой производной декларации (`BaseDefinition` ↔ `GetBaseDefinition`),
  видимость по геттеру, поля не `readonly` и не сгенерированные компилятором; проба с захваченным
  параметром первичного конструктора в `[BinaryContract]` даёт одинаковые байты.
- VPR001…VPR021: каждый — в §14.4, в `docs/generator.md`, в таблице `GENERATOR-README.md`, в
  `AnalyzerReleases.Unshipped.md`, в `Descriptors.cs`, и у каждого случай в `DiagnosticTests`
  (`EveryDescriptor_HasACase`).
- Пакет: `netstandard2.0`, `analyzers/dotnet/cs`, `DevelopmentDependency`, `IncludeBuildOutput=false`;
  мета-пакет несёт его с `include="All"` (RC2-03 — о записи решения).

### Тримминг и native AOT (бриф §4.7)

- Подавлений в `src/` нет; ровно 22 + 4 входа несут `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`,
  ниже — только `FormatterRegistry`, `ReflectedContract`, `TypeContractCache.Get`/`Get<T>`.
- `AotAnalysisTests` зелёный; CI `native-aot` зелёный на проверяемом коммите; локальная публикация — без
  предупреждений, вывод равен JIT.

## 11. Находки

### RC2-01 — предложение `SupportedTypes` меняет поведение всего процесса

- **Вес:** блокирует rc.
- **Сторона:** нужно решение владельца.
- **Слой:** `Engine/Contracts/SupportedTypes.cs` (`Union<T>`, `Collection<TCollection, T>`) и
  `Engine/Codec.cs` (`OfferedCodec<T>`) против контракта §14.5, §18, INV-20 и решения D3.
- **Доказательство:** `SupportedTypes.cs:54–88` принимает любые плечи, в том числе для типа без
  `[BinaryUnion]` и с нулём плеч; `SupportedTypes.cs:124…` принимает любые `create`/`add`;
  `BinarySerializerContext.Add(SupportedType)` вызывает `Offer()` в конструкторе контекста;
  `OfferedCodec<T>.Offer` — первое предложение на весь процесс. Проба `union-hostile`: контекст создан и
  не передан ни в одни опции — сериализатор по умолчанию пишет `Base(A)` с тегом `09` вместо `01`,
  читает `MyColl {1, 2}` как `101, 102`, а `Plain` больше не записывается вовсе.
- **Наблюдается:** любой код процесса (библиотека, плагин, тест), создавший контекст, меняет байты и
  результат чтения для всех сериализаторов процесса, в том числе без контекста; порядок создания
  контекстов определяет формат.
- **Ожидается:** §14.5 «Every offer of a type is the codec the rules give that type, so which context
  offers it, and when, changes nothing an operation writes or reads; an offer is not a registry through
  which behaviour can be changed (§18, INV-7)»; §18 «changes nothing an operation observes»; INV-20 «A
  codec or union map a context offers through `SupportedTypes` is the one the reflection path builds»;
  D3 (одобрено): «an offer of a different value is not expressible, because each factory builds the
  registry's codec for its own type»; `docs/writing-contracts.md:437` «nothing a context adds changes how
  another configuration writes or reads». При этом сам §14.5 говорит «The arms of a union are taken as
  given; the serializer does not read `[BinaryUnion]` for a type whose union map was supplied», а D4 и D5
  (as built) приняли плечи и делегаты как аргументы — решения противоречат обоснованию D3.
- **Варианты, видимые в доказательствах:** (a) `Union<T>` сверяет плечи с `[BinaryUnion]` типа и
  отказывает типу без них — чтение атрибутов во время выполнения, которого D4 избегал (D4-b);
  `Collection<,>` при этом остаётся непроверяемым; (b) таблица кодеков на контекст, а не на процесс
  (D3-c или D3-d, со стоимостью, записанной в D3); (c) оставить как есть и переписать §14.5, §18,
  INV-20, `docs/writing-contracts.md`: рукописное предложение доверено и задаёт формат типа для всего
  процесса, первое побеждает.

### RC2-02 — исключение из кода поставленного контракта и `null` из `Create` не описаны

- **Вес:** блокирует rc.
- **Сторона:** контракт (§14.3 молчит) и код (`ObjectMembers.Create` принимает `null`).
- **Слой:** контракт §14.3 (с §8/§9); `Engine/Codecs/ObjectCodec.cs`, `ObjectMembers.Create`.
- **Доказательство:** пробы `create-null` и `contract-throws`; `ObjectCodec.cs` `Create<T>` возвращает
  `contract.Create()` без проверки, `ReadInstance` регистрирует результат как экземпляр ссылки.
- **Наблюдается:** `Create()`, вернувший `null` для класса, даёт `NullReferenceException` из чтения;
  `InvalidOperationException` из `Write`/`ReadPositional` выходит без обёртки (назначение не тронуто).
  Ни то, ни другое контракт не называет.
- **Ожидается:** INV-10 «nothing on a payload path leaves under a framework name»; §2.4 «The engine
  creates an instance and registers its identity before its members are read»; правило того же рода
  уже есть для фабрики `Lazy<T>` (§23: «an exception from that factory is the caller's own and propagates
  unchanged») и фабрики алгоритма (§4.1). Для поставленного контракта правило должно быть записано, а
  `null` из `Create` — отказ движка в таксономии.

### RC2-03 — решение S5-a записано иначе, чем построено

- **Вес:** блокирует rc.
- **Сторона:** нужно решение владельца.
- **Слой:** `RC2-Plan.md` §3 S5 / §3.9 и `src/ViShap.Viper/ViShap.Viper.csproj`.
- **Доказательство:** S5-a: «`ViShap.Viper` references it with `PrivateAssets="all"` (a development
  dependency) | One install gives everything»; `ViShap.Viper.csproj` — `PrivateAssets="none"`; nuspec
  мета-пакета — `<dependency id="ViShap.Viper.Generator" … include="All" />`; проба consumer: генератор
  работает у потребителя одного `ViShap.Viper`.
- **Наблюдается:** код выполняет вторую половину решения («one install») и противоречит первой
  (`PrivateAssets="all"` не пустил бы генератор к потребителю). Контракт §14.4 и `METAPACK-README.md`
  описывают построенное.
- **Ожидается:** решение, которое код может выполнить. Варианты: записать в §3.9 построенное
  (`PrivateAssets="none"`, генератор течёт транзитивно); или `PrivateAssets="all"` буквально — тогда
  §14.4 «one install brings it» и README становятся неверны.

### RC2-04 — контракт хранит формулировки rc.1

- **Вес:** нужно исправить до выпуска.
- **Сторона:** документ — `System-Contract.md` (и `QA-Plan.md`, который зеркалит §24).
- **Слой:** контракт §2, §14.1, §18, §24; QA §32.
- **Доказательство:** §24 Quality gate (`System-Contract.md:3099`) «`dotnet pack` succeeds for all three
  packages»; QA-Plan.md:1577 то же с rc.1; §24 (`:3009`) «every public entry point that encodes or decodes
  a caller's type carries `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, no other public member
  does»; §18 (`:2425`) «`FormatterCache<T>` = the codec of T, resolved once by FormatterRegistry»; §2
  (`:48–49`) «`ReflectedContract<T>` in v1.0»; §14.1 (`:1994`) «in v1.0 it is built by reflection».
- **Наблюдается:** пакетов четыре (`cd.yml` `EXPECTED_COUNT=4`); атрибуты — только на 26 входах без
  информации о типе, 26 других их не несут (§3); кодек приходит из предложения, встроенных или
  рефлексии; контракт v1.0 бывает рукописным и сгенерированным.
- **Ожидается:** §3, §14.3–§14.5 и §18 «What is cached» того же документа.

### RC2-05 — «no tag» в §14.4, GEN-09 и `CLAUDE.md` против D4

- **Вес:** нужно исправить до выпуска.
- **Сторона:** документы — контракт §14.4, QA GEN-09, `CLAUDE.md` «The source generator»; тест GEN-09
  утверждает меньше заявленного.
- **Слой:** контракт §14.4 (различить тег на проводе и тег как данные union-карты).
- **Доказательство:** `Snapshots/SupportedTypes.verified.txt:26`
  `SupportedTypes.Union<global::Shapes.Shape>((1, typeof(global::Shapes.Circle)), (2, …))`;
  `PipelineTests.TheEmittedSource_HoldsNoLoopNoLengthNoCountNoTagAndNoReflection` ищет только
  `BinaryUnion`, `WriteByte`, `ReadByte`; §14.4 «Nothing generated holds … a union tag».
- **Наблюдается:** сгенерированный код содержит теги union-карты, как решил D4.
- **Ожидается:** D4 (выше контракта по брифу §3): «Emitted: `SupportedTypes.Union<Shape>(…)` offers the
  union map».

### RC2-06 — INV-17 не закреплён для пути через контекст

- **Вес:** нужно исправить до выпуска.
- **Сторона:** QA TYP-02 и `Contracts/BoxingTests`.
- **Слой:** `Contracts/BoxingTests` (случаи через сгенерированный и рукописный контекст).
- **Доказательство:** в `BoxingTests.cs` нет `WithContracts`/`BinaryTypeInfo`; бриф §4.2 называет
  «the generated and hand-written contracts box nothing outside a polymorphic slot». Проба boxing:
  272/272 и 72/72 байт на операцию — поведение не под сомнением.
- **Наблюдается:** структурный тест покрывает только отражённый путь.
- **Ожидается:** INV-17 «verified by a counting test double» для каждого пути, который rc.2 добавил.

### RC2-07 — висячие ссылки на §22.8, строка ведомости записана «clean»

- **Вес:** нужно исправить до выпуска.
- **Сторона:** документ — `Benchmark-Plan.md`; внутренний комментарий бенчмарка; ведомость `Retired.md`.
- **Слой:** `Benchmark-Plan.md` PROF-02, SIZE-02, DIFF-01; `Reporting/SizeReport.cs`.
- **Доказательство:** `Benchmark-Plan.md:344, 542, 638`; `SizeReport.cs:119`; `Retired.md:112` (R9a,
  «clean»). В контракте разделы §22.1–§22.7, конверт V0 — §22.7.
- **Наблюдается:** четыре ссылки на несуществующий раздел.
- **Ожидается:** строка R9a: «every living reference updated».

### RC2-08 — результаты строк ведомостей не называют новых безвредных попаданий

- **Вес:** записано.
- **Сторона:** ведомости `RC2-Plan.md` §7 и `rework/Retired.md`.
- **Слой:** столбец результата строк.
- **Доказательство:** §7 P1 `APositionalMemberUnderAKeyedLayout` «clean» —
  `ContractCallTests.cs:107` `Write_APositionalMemberUnderAKeyedLayout_ThrowsType` (ошибка записи всё ещё
  выразима, тест законен); R4 `Enum.ToObject` «clean» — `DataSets/Projection.cs:38`; R3 `FromStream` —
  `Harness.cs:104` (`LoadFromStream`), `MarketBenchmarks.cs:91` (`DeserializeFromStream`).
- **Наблюдается:** попадания законны, но строки о них не говорят.
- **Ожидается:** строка указывает каждое допустимое попадание (так оформлены остальные строки).

### RC2-09 — классы и состояния документов в `CLAUDE.md` и `internal/README.md`

- **Вес:** нужно исправить до выпуска.
- **Сторона:** документы — `CLAUDE.md`, `internal/README.md`.
- **Слой:** `CLAUDE.md` «Where the authoritative information lives»; таблицы `internal/README.md`.
- **Доказательство:** `internal/Audit-Conformance.md` (исторический) не назван ни в `CLAUDE.md`, ни в
  `internal/README.md`; `internal/rc2/` не имеет класса в `CLAUDE.md`; `CLAUDE.md:71–72`
  «`Benchmark-Graceful-Stop.md` — plan. A decided backlog item …, not started», тогда как README —
  «done (rc.2 P6)», merge `3c27571`.
- **Ожидается:** скилл §3.6: каждый файл `internal/` с тем же классом в `CLAUDE.md` и `internal/README.md`.

### RC2-10 — состояние `RC2-Plan.md` устарело

- **Вес:** нужно исправить до выпуска.
- **Сторона:** документ — `RC2-Plan.md`.
- **Слой:** шапка и §8 Progress.
- **Доказательство:** `RC2-Plan.md:7` «Status: drafted 2026-10-09 …; not approved, not started.»;
  `:657` P7 «gate holds — awaiting commit», хотя `docs/rc2-docs` слита (`97fba73`, PR #32).
- **Ожидается:** §8 «a stage is `closed` only after the owner has merged its branch»; P7 — `closed`,
  `97fba73`; шапка — решения приняты, стадии P0–P7 закрыты.

### RC2-11 — буквальный перевод строки вместо `\n`

- **Вес:** записано.
- **Сторона:** документы — `CLAUDE.md`, `QA-Plan.md`.
- **Слой:** текст.
- **Доказательство:** `CLAUDE.md:258–259` «with `` ⏎ `` line endings»; `QA-Plan.md:577–578` (GEN-08) так же.
- **Ожидается:** `` `\n` ``.

### RC2-12 — сообщение «not offered by any BinarySerializerContext» неверно для предложенного типа без контракта

- **Вес:** записано.
- **Сторона:** код.
- **Слой:** `ObjectCodec.Reflected` / `ReflectionPath.Unavailable`.
- **Доказательство:** проба offer-no-contract: `Add(SupportedTypes.Object<Box>())` без `Add(contract)`,
  рефлексия выключена — «'Box' is not offered by any BinarySerializerContext this process has created».
- **Наблюдается:** тип исключения верный (§14.5), но текст называет не ту причину: кодек предложен,
  нет контракта.
- **Ожидается:** сообщение, называющее недостающий контракт.

### RC2-13 — `SupportedType` не назван ни на одной странице `docs/`

- **Вес:** нужно исправить до выпуска.
- **Сторона:** документ — `docs/` (`writing-contracts.md` или `aot.md`).
- **Слой:** `docs/writing-contracts.md`.
- **Доказательство:** `grep -rnw SupportedType docs` — пусто; страницы показывают только
  `Add(SupportedTypes.X())`.
- **Ожидается:** бриф §4.4 «each new public type explained on a page of `docs/`».

## 12. Закрытие

*Пишется в сессии закрытия на `audit/rc2-closure`.*
