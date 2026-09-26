# Решения по рефакторингу — точная запись

**Назначение.** Полная и однозначная запись того, что владелец решил в диалоге о рефакторинге
(2026-09-25 … 2026-09-26), со схемами байтов и правилами, — чтобы любой исполнитель вне этого диалога
понял каждое решение одинаково. Этот файл — источник для переписывания `Rework-Plan.md` и файлов
`*-Changes.md`; где он и они расходятся, прав этот файл.

**Статусы.** Каждый пункт помечен:

```text
[ПОДТВЕРЖДЕНО]   владелец выбрал это явно
[СЛЕДСТВИЕ]      выведено из подтверждённого, владелец ещё не подтвердил отдельно
[ИЗ ПЛАНА]       взято из Rework-Plan.md, в диалоге не оспаривалось и отдельно не подтверждалось
[ОТКРЫТО]        не решено; вариант, если приведён, — только предложение
```

Все байты в схемах — шестнадцатеричные. «varint» — LEB128 без знака, 7 бит на байт, старший бит —
«дальше ещё байт», **только минимальная запись** (§5.1).

---

# 1. Рамка

- **1.1** [ПОДТВЕРЖДЕНО] Всё из рефакторинга входит в `v1.0.0`. После релиза — только аддитивные
  изменения: новый алгоритм, исправление ошибки, генератор исходников.
- **1.2** [ПОДТВЕРЖДЕНО] Провод, публичный API и интерфейсы алгоритмов переделываются без оглядки
  на совместимость: опубликованного формата и потребителей нет.
- **1.3** [ПОДТВЕРЖДЕНО] V1 фиксируется окончательно; цель — чтобы V2 не понадобился рано, а не
  «бессмертие». Отдельного механизма extensions нет.
- **1.4** [ПОДТВЕРЖДЕНО] Reed–Solomon и любые коды восстановления сняты с рассмотрения.

---

# 2. Заголовок V1

## 2.1 Раскладка

```text
magic          4 байта   42 53 45 52          int32 0x52455342, little-endian — без изменений
version        varint    [ПОДТВЕРЖДЕНО] значение 1 → байт 01; одно правило для всех чисел заголовка
payload mode   varint    [ПОДТВЕРЖДЕНО] отдельное поле до сервисов
service count  varint    [ПОДТВЕРЖДЕНО] число записей сервисов
services       записи    [ПОДТВЕРЖДЕНО] §2.3
onDiskLength   varint    [СЛЕДСТВИЕ] длина байтов после заголовка; присутствует всегда, даже без
                         сервисов, — делает кадр самоограниченным
payload        onDiskLength байт
```

Минимальный кадр — без сервисов, без ссылок, payload из одного байта `01` (не-null строка `""`):

```text
42 53 45 52   magic
01            version
00            payload mode
00            ноль сервисов
01            onDiskLength = 1
01            payload
```

## 2.2 Режим payload

- **2.2.1** [ПОДТВЕРЖДЕНО] Режим ссылок записан в кадре, а не берётся из настройки читателя.
  Глобальная опция `PreserveReferences()` остаётся; читатель следует кадру, как сегодня.
- **2.2.2** [СЛЕДСТВИЕ] Биты поля:

```text
bit 0     references   1 = payload использует кадры ссылок (§4.3)
bit 1+    зарезервированы, обязаны быть 0; установленный неизвестный бит →
          BinaryFormatNotSupportedException (новый режим, которого читатель не знает)

00 — без ссылок, 01 — со ссылками
```

## 2.3 Запись сервиса

```text
kind     varint    (номер << 1) | critical
length   varint    длина тела в байтах
body     length байт
```

- **2.3.1** [ПОДТВЕРЖДЕНО] Два класса сервисов: *преобразование* (меняет байты кадра: сжатие,
  шифрование) — всегда критично; *аннотация* (описывает байты: контрольная сумма) — критичность
  задаёт контракт.
- **2.3.2** [ПОДТВЕРЖДЕНО] Канонический порядок: записи идут по возрастанию номера, каждый номер
  не более одного раза. Нарушение — `BinaryFormatException`.
- **2.3.3** [ПОДТВЕРЖДЕНО] Порядок преобразований фиксирован контрактом, а не порядком записей:
  запись — сериализация → контрольная сумма над сырым payload → сжатие → шифрование; чтение — обратно.
- **2.3.4** [СЛЕДСТВИЕ] Неизвестный номер с `critical = 1` → `BinaryFormatNotSupportedException`;
  с `critical = 0` → пропуск по `length`. Для известного номера бит критичности обязан совпадать с
  тем, что задаёт контракт; иначе `BinaryFormatException` (одно значение — одна запись).
- **2.3.5** [СЛЕДСТВИЕ] Тело читается ровно на `length` байт: недочитанный или перечитанный остаток —
  `BinaryFormatException`. `length` проверяется против оставшихся байт до чтения тела.
- **2.3.6** [ПОДТВЕРЖДЕНО] Номера видов — по порядку применения при записи; номер 0 зарезервирован:

```text
номер 0   —             kind 00 / 01 → BinaryFormatException (обнулённая память не читается как сервис)
номер 1   checksum      аннотация        kind 03
номер 2   compression   преобразование   kind 05
номер 3   encryption    преобразование   kind 07
```

  Канонический порядок записей (по возрастанию номера) поэтому совпадает с порядком обработки при
  записи: сумма → сжатие → шифрование; читатель снимает преобразования с конца. Позицию в цепочке
  задаёт контракт (2.3.3), а не номер, поэтому промежутки между номерами не нужны.
- **2.3.7** [ПОДТВЕРЖДЕНО] Контрольная сумма — критичная. Правило для будущих сервисов: всё, что
  защищает или проверяет данные, — критично; тихо пропустить проверку нельзя.
- **2.3.8** [ПОДТВЕРЖДЕНО] Фиксированная граница формата: заголовок от magic до `onDiskLength`
  включительно — не более **4096 байт**. Проверяется до чтения каждого тела: тело обязано поместиться
  в остаток бюджета заголовка; иначе `BinaryFormatException` (граница формата, не политика — как
  256 байт у строк заголовка). Самый большой законный заголовок v1.0 — около 1,3 КиБ. Отдельного
  лимита на тело или на число записей нет: оба ограничены общими 4 КиБ. Размер контрольной суммы —
  1…255 байт, как сегодня (NX-11). Заголовок и AAD читаются в буфер фиксированного размера; из
  non-seekable потока до проверки заголовка набирается не больше 4 КиБ.

## 2.4 Тела сервисов

Общее правило для алгоритма в теле [ПОДТВЕРЖДЕНО]:

```text
id      varint    значение перечисления алгоритма
name    string    только при id = Custom: varint длина + UTF-8, 1…256 байт; при другом id отсутствует
```

`CustomName` и отдельный флаг «имя есть» из сегодняшнего заголовка исчезают: о присутствии имени
говорит сам `id`. Пустое имя при `id = Custom` — `BinaryFormatException` [СЛЕДСТВИЕ].

[СЛЕДСТВИЕ] Отсутствие фазы — это отсутствие записи сервиса. Запись с `id = None` —
`BinaryFormatException`: у «нет сжатия» ровно одна запись — никакой. Поэтому сегодняшние правила
«`Compression = None` → длины равны» и «`Encryption = None` → длины равны» исчезают вместе с полями.

[СЛЕДСТВИЕ §9.11] Политики `RequireChecksum` / `RequireEncryption` проверяются по присутствию записи
сервиса; для шифрования ещё и алгоритм обязан сообщать `AuthenticatesAssociatedData = true`
(`EncryptionGuarantee` отвергнут, §9.11); отсутствие или неаутентифицирующий алгоритм —
`BinaryIntegrityException`, как сегодня.

**Сжатие** (номер 2, kind `05`):

```text
id · [name] · uncompressedLength varint
```

Пример, встроенный Brotli, payload 1 000 байт (`CompressionAlgorithm.Brotli` = 2):

```text
05  03  02 E8 07
│   │   │  └─ uncompressedLength = 1000 (varint)
│   │   └──── id = Brotli
│   └──────── length тела = 3
└──────────── kind: номер 2, critical
```

Пример, custom-алгоритм «lz4x» (`CompressionAlgorithm.Custom` = 255 — двухбайтовый varint `FF 01`):

```text
05  09  FF 01 04 6C 7A 34 78 E8 07
        │     │  │           └─ uncompressedLength = 1000
        │     │  └─ "lz4x"
        │     └──── длина имени 4
        └────────── id = Custom (255)
```

- **2.4.1** [ПОДТВЕРЖДЕНО — сегодняшнее правило NX-01, без изменений по смыслу] Правило коэффициента: при сжатии
  `uncompressedLength ≤ (длина сжатых байт) × MaxDecompressionRatio`, иначе `BinaryLimitException`,
  проверяется при чтении заголовка.
- **2.4.2** [ПОДТВЕРЖДЕНО] Длина открытого текста при шифровании не объявляется; тело шифрования —
  `id · [name] · keyId`. Без шифрования длина сжатых байт равна `onDiskLength`. Порядок проверок:

```text
1. заголовок          onDiskLength ≤ MaxEncryptedBytes и ≤ оставшихся байт;
                      при шифровании — грубо: uncompressedLength ≤ onDiskLength × ratio;
                      без шифрования — точно: uncompressedLength ≤ onDiskLength × ratio
2. расшифровка        буфер ≤ onDiskLength (память — по доставленным байтам)
3. после расшифровки  plaintextLength ≤ MaxCompressedBytes; uncompressedLength ≤ plaintextLength × ratio
4. распаковка         единственное выделение, которое защищает коэффициент, — только после шага 3
```

  Требование к алгоритму [СЛЕДСТВИЕ]: `GetCiphertextLength(n) ≥ n` (шифротекст не короче открытого
  текста); сервис проверяет его вместе с §3.2.

**Контрольная сумма** (номер 1, kind `03`):

```text
id · [name] · hash (остаток тела)
```

Пример, CRC-32 (`ChecksumAlgorithm.Crc32` = 1, 4 байта суммы):

```text
03  05  01 9A 3B C1 07
        │  └─ hash, 4 байта
        └──── id = Crc32
```

- **2.4.3** [СЛЕДСТВИЕ] Длина `hash` = `length` тела − байты `id` и `name`; обязана равняться
  `HashSize` алгоритма, иначе `BinaryFormatException`.

**Шифрование** (номер 3, kind `07`):

```text
id · [name] · keyId (string со свёрткой null: varint (длина + 1), 0 = нет keyId, затем UTF-8 ≤ 256 байт)
```

- **2.4.4** [ПОДТВЕРЖДЕНО] `KeyId` — в теле сервиса шифрования.
- **2.4.5** [СЛЕДСТВИЕ] Кодирование отсутствия `KeyId` — та же свёртка null, что и для строк
  payload (§4.2).

Пример, AES-GCM с `KeyId = "k7"` (`EncryptionAlgorithm.Aes256Gcm` = 1):

```text
07  04  01 03 6B 37
        │  │  └─ "k7"
        │  └──── длина 2 + 1
        └─────── id = Aes256Gcm
```

## 2.5 Полный пример кадра

Сжатие Brotli + AES-GCM, `KeyId = "k7"`, без ссылок, контрольной суммы нет:

```text
42 53 45 52              magic
01                       version
00                       payload mode: без ссылок
02                       две записи сервисов
05 03 02 E8 07           compression: Brotli, uncompressed 1000
07 04 01 03 6B 37        encryption: AES-GCM, KeyId "k7"
<onDiskLength varint>    = GetCiphertextLength(длина сжатых байт)
<шифротекст>             nonce 12 · ciphertext · tag 16; длина открытого текста не объявляется (2.4.2)
```

---

# 3. Связанные данные (AAD) и шифрование

- **3.1** [ПОДТВЕРЖДЕНО, P12 вариант b] AAD = точные байты заголовка на проводе, от первого байта
  magic до последнего байта `onDiskLength` включительно. Отдельного «образа» AAD нет; §22.7
  контракта удаляется.
- **3.2** [ПОДТВЕРЖДЕНО] `IEncryptionAlgorithm` получает точную длину шифротекста как функцию:

```csharp
int GetCiphertextLength(int plaintextLength);   // точное значение, не верхняя граница
// AES-GCM:     plaintextLength + 28   (nonce 12 + tag 16)
// CBC + HMAC:  16 + roundUp(plaintextLength + 1, 16) + 32
```

  Заменяет `GetMaxCiphertextLength`. Сервис проверяет, что алгоритм записал ровно объявленное число
  байт; иначе ошибка конфигурации алгоритма.
- **3.3** [ПОДТВЕРЖДЕНО] Зашифрованный кадр пишется сразу в назначение, без промежуточной копии:
  заголовок (длина известна по 3.2) → шифрование прямо в `destination.GetSpan(n)`.
- **3.4** [СЛЕДСТВИЕ] Алгоритм, не способный заранее знать длину шифротекста (например, со случайным
  дополнением), в Viper не подключается.

---

# 4. Payload

## 4.1 Числа

- **4.1.1** [ПОДТВЕРЖДЕНО, P1 — применено] Любой varint читается только в минимальной записи; иная —
  `BinaryFormatException` (`HST-40`).
- **4.1.2** [ПОДТВЕРЖДЕНО, W1 вариант a, W2] Служебные числа — счётчики, длины, id, ключи, числа
  заголовка — беззнаковые varint; отрицательный счётчик невыразим. Данные (`int`, `double`, …)
  остаются фиксированной ширины, little-endian. Тег union — один байт. Отвергнуто: varint с zigzag
  для данных (медленнее для всех, закрывает копирование блока фиксированных полей); фиксированная
  ширина служебных чисел (+3 байта на каждый счётчик, ссылку, длину).
- **4.1.3** [ПОДТВЕРЖДЕНО, W4] Keyed-поле: `varint key · int32 длина (LE, фиксированная) · payload`.
  Длина фиксированная, потому что она дописывается после записи поля; varint потребовал бы либо
  неминимальной записи, либо сдвига байтов поля.

## 4.2 Null — свёртка в первое число [ПОДТВЕРЖДЕНО, D-3 вариант d]

**Правило.** Null кодируется ровно один раз — в первом числе, с которого начинается значение:
`0` = null, иначе `значение + 1`.

```text
тип значения                        ссылки выкл.                      ссылки вкл.
string                              varint (байтовая длина + 1)       то же — строки не обрамляются
sequence (включая byte[]), map      varint (счётчик + 1)              кадр ссылки несёт null; счётчик — см. 4.2.2
keyed-объект                        varint (число полей + 1)          кадр ссылки несёт null; число полей — см. 4.2.2
позиционный объект                  байт-флаг 00 / 01                 кадр ссылки несёт null
union                               байт-флаг, затем байт тега        кадр ссылки несёт null, затем байт тега
Nullable<T> (T — value type)        байт-флаг 00 / 01, затем T         то же — value type не обрамляется
тип, который не может быть null     ничего                            ничего
```

Примеры:

```text
null (string)                          00
""                                     01
"hello"                                06 68 65 6C 6C 6F
List<int> из 3, ссылки выкл.           04 <int32> <int32> <int32>
List<int> из 3, ссылки вкл.            01 03 <int32> <int32> <int32>      кадр «первое появление id 0», счётчик без +1
struct с keyed-раскладкой, 2 поля      02 ...                             тип не бывает null — число полей без +1
int? = 5                               01 05 00 00 00
int? = null                            00
```

- **4.2.1** [ПОДТВЕРЖДЕНО] Свёртка применяется только когда объявленный тип может быть null — то же
  условие, что у сегодняшнего флага.
- **4.2.2** [ПОДТВЕРЖДЕНО] При включённых ссылках null уже в кадре ссылки, поэтому
  следующий за кадром счётчик или число полей пишется **без** `+ 1`. Иначе у null было бы два места
  и две записи. Правило одной фразой: null пишется ровно один раз — в первом числе, с которого
  начинается значение; ни одного «мёртвого» значения счётчика, требующего отдельного запрета, нет.
- **4.2.3** [ПРОВЕРЕНО ПО КОДУ] `byte[]` — обычный одномерный массив (`ArrayFormatter`), то есть
  последовательность: обрамляется кадром ссылки и подчиняется строке «sequence». «Blob» встречается
  только внутри `BigInteger` и `BitArray`, которые сами не бывают null, — свёртка к нему не относится.
- **4.2.4** [ПОДТВЕРЖДЕНО] `ImmutableArray<T>` (struct, null не бывает): `0` = `default`
  (`IsDefault`), иначе `varint (счётчик + 1)`. Отдельный флаг «present» из §22.5 исчезает.

```text
default                       00
Empty                         01
3 элемента                    04 <элементы>
ImmutableArray<T>? = null     00          флаг Nullable
ImmutableArray<T>? = default  01 00       флаг Nullable, затем свёртка
ImmutableArray<T>? = Empty    01 01
```

  Отвергнуто: писать `default` как `Empty` — меняет значение при круговом переносе.
- **4.2.5** [ПОДТВЕРЖДЕНО] Отвергнуто: битовые маски null на объект и на коллекцию; пропуск
  null-полей keyed-контракта (читатель не отличит «null» от «поле неизвестно писателю», и
  значение конструктора по умолчанию заменило бы null).

## 4.3 Кадр ссылки

- **4.3.1** [ПОДТВЕРЖДЕНО, W3 + D-3] Один varint вместо сегодняшних «байт маркера + int32»:

```text
0                                   null
((id << 1) | 0) + 1                 первое появление объекта id, за ним payload значения
((id << 1) | 1) + 1                 обратная ссылка на id, значение закончилось

первое появление id 0   → 01
обратная ссылка на id 0 → 02
первое появление id 5   → 0B
```

- **4.3.2** [ПОДТВЕРЖДЕНО, W3] Id явные, не неявные: пропуск неизвестного keyed-поля не должен рассинхронить
  счётчики писателя и читателя. Скоупы видимости id (§16.2 контракта) — без изменений.

## 4.4 Порядок ключей keyed-полей при чтении

- **4.4.1** [ПОДТВЕРЖДЕНО — применено 2026-09-26, `KEY-23`] Ключи keyed-полей идут строго по
  возрастанию. Ключ, не больший предыдущего, — `BinaryFormatException`: повтор («Duplicate keyed
  field key») и нарушение порядка («fields must appear in ascending key order»), для известного и для
  пропускаемого ключа одинаково. `HashSet<int>` на каждый keyed-объект заменён одним `int`.
- **4.4.2** [СЛЕДСТВИЕ] Поскольку и провод, и описание контракта упорядочены по ключу, движок
  нового кода (§8.6) может искать член курсором по описанию, а не словарём.

---

# 5. V0

- **5.1** [ПОДТВЕРЖДЕНО] V0 — голый payload: без сжатия, контрольной суммы, шифрования, без
  какого-либо заголовка. Фазы в V0 не добавляются.
- **5.2** [ПОДТВЕРЖДЕНО] V0 уступает V1 только в том, что требует метаданных: сервисы заголовка и
  режим ссылок.
- **5.3** [ПОДТВЕРЖДЕНО] Ссылок в V0 нет; цикл — `BinaryTypeException`, как сегодня.
- **5.4** [ПОДТВЕРЖДЕНО] Keyed-запись работает в non-seekable назначение: V0 пишет через тот же
  собственный буфер, что и V1, затем копирует.
- **5.5** [ПОДТВЕРЖДЕНО] Payload V0 байт в байт равен payload V1 того же значения без ссылок.
- **5.6** [ПОДТВЕРЖДЕНО] Граница при чтении:

```text
span / sequence       по умолчанию — ровно один payload; лишние байты → BinaryFormatException
перегрузка            Deserialize<T>(source, out int consumed) — для нескольких payload подряд
seekable Stream       чтение с опережением, затем позиция возвращается к концу корня
non-seekable Stream   только с явной длиной; без неё → NotSupportedException
```

---

# 6. Запись и буферы

- **6.1** [ПОДТВЕРЖДЕНО] Payload V0 и V1 строится в собственном буфере сериализатора из пула
  (`PayloadBuffer`); длины keyed-полей дописываются в нём; затем одна копия в назначение.
- **6.2** [ПОДТВЕРЖДЕНО] Причины не убирать копию: предварительный проход размеров дороже копии;
  длины в трейлере ломают проверку до выделения памяти; кадр кусками требует самодельного потокового
  шифрования; фиксированный заголовок прячет копию внутри `ArrayBufferWriter`. Буфер даёт
  атомарность: при ошибке в назначение не уходит ни байта.
- **6.3** [ПОДТВЕРЖДЕНО] Исключение — шифрование (§3.3). Для `byte[]` и `Stream` лишней копии нет по
  природе этих назначений.

---

# 7. Выпуск (CD) — применено в `.github/workflows/cd.yml`

- **7.1** [ПОДТВЕРЖДЕНО] Стабильный релиз: только `vX.Y.Z` без ведущих нулей, тег ровно на HEAD
  `origin/main`.
- **7.2** [ПОДТВЕРЖДЕНО] Pre-release: только `vX.Y.Z-alpha.N`, `-beta.N`, `-rc.N`, `N ≥ 1`, на
  коммите, входящем хотя бы в одну ветку origin.
- **7.3** [ПОДТВЕРЖДЕНО] Pre-release уже выпущенной `vX.Y.Z` отклоняется.
- **7.4** Pre-release на NuGet.org удалить нельзя, только скрыть; для замеров без пакета — локальный
  тег, который не пушится.

---

# 8. Шов для генератора исходников

## 8.1 Решение

[ПОДТВЕРЖДЕНО, вариант d] В v1.0 закладывается внутренний шов объекта в *форме метода*: запись и
чтение членов — методы контракта типа, а не цикл движка по списку делегатов. В v1.0 его реализует
только рефлексия (§8.5). Генератор исходников выходит после релиза аддитивно и реализует тот же шов
(§8.7). Шов в v1.0 — `internal`; что станет публичным, решается при выпуске генератора (§8.8).

Почему форма метода: она строго общее формы данных. Список описаний членов всегда можно исполнить
методом с циклом (так делает рефлексия), а прямой код без делегатов списком описаний не выразить.

## 8.2 Инварианты

```text
контракт задаёт только   порядок членов, доступ к ним, создание экземпляра, реакцию на известный ключ
движок владеет           null и кадром ссылки, тегом union, глубиной, бюджетами, циклами, числом
                         keyed-полей, ключами и длинами полей, окном поля, циклом по полям с провода,
                         пропуском неизвестных ключей, проверкой «поле прочитано ровно»
MemberWriter/Reader      не дают ни байтов, ни счётчиков, ни позиции — только значение члена
движок сверяет           каждый вызов контракта с его описанием (§8.4); расхождение —
                         BinaryTypeException, а не искажённый провод
```

## 8.3 Имена и типы [ПОДТВЕРЖДЕНО]

- `TypeContract` — сегодняшнее описание типа (раскладка, члены, ключи, конструируемость); остаётся
  не-generic базой.
- `TypeContract<T> : TypeContract` — абстрактный класс, добавляет поведение.
- `ReflectedContract<T>` — реализация рефлексией.
- `MemberWriter` / `MemberReader` — `ref struct`, единственное, что контракт получает от движка.
- Отвергнуты `IObjectPlan<T>`, `ObjectShape`: «shape» в контракте §22.3 уже означает форму
  кодирования значения. Абстрактный класс, а не интерфейс: при публикации к нему можно добавлять
  члены с реализацией по умолчанию без поломки сгенерированного кода.

```csharp
internal abstract class TypeContract<T> : TypeContract
{
    // Создаёт экземпляр для чтения: конструктор без параметров; для struct — default.
    public abstract T Create();

    // Позиционная раскладка: по одному вызову writer.Member на член, в порядке плана.
    // Keyed-раскладка: по одному вызову writer.Field на член, по возрастанию ключа.
    public abstract void Write(ref MemberWriter writer, in T value);

    // Только позиционная раскладка: по одному вызову reader.Member на член, в порядке плана.
    public abstract void Read(ref MemberReader reader, ref T value);

    // Только keyed-раскладка. Движок вызывает для каждого поля с провода. Возвращает false,
    // если ключ контракту неизвестен, — тогда движок пропускает поле по длине.
    public abstract bool ReadField(ref MemberReader reader, int key, ref T value);

    // Полиморфный слот (union, интерфейс, object): тип известен только во время выполнения.
    // Единственное место, где движок боксит.
    internal sealed override void WriteBoxed(ref MemberWriter writer, object value) =>
        Write(ref writer, (T)value);
}

internal ref struct MemberWriter
{
    public void Member<TMember>(TMember value);            // позиционный член: значение целиком, с null/кадром
    public void Field<TMember>(int key, TMember value);    // keyed-поле: ключ, резерв int32, значение, дописать длину
}

internal ref struct MemberReader
{
    public TMember Member<TMember>();                      // позиционный член
    public TMember Value<TMember>();                       // значение текущего keyed-поля, ровно один раз в ReadField
}
```

Чтение полиморфного слота устроено так же, как `WriteBoxed`: движок по тегу union выбирает
`TypeContract` runtime-типа и вызывает его не-generic вход, который внутри типизирован.

## 8.4 Сверка вызовов движком

```text
запись, позиционная   i-й вызов Member<TMember> ⇒ typeof(TMember) == Members[i].MemberType;
                      после Write: число вызовов == Members.Length
запись, keyed         i-й вызов Field<TMember>(key) ⇒ key == Members[i].Key (описание упорядочено
                      по ключу) и typeof(TMember) совпадает; после Write: число вызовов == Members.Length
чтение, позиционное   то же, что при записи, для Member<TMember>()
чтение, keyed         ReadField вернул true ⇒ Value<TMember>() вызван ровно один раз, тип совпадает
                      с членом этого ключа, окно поля прочитано ровно; вернул false ⇒ Value не вызывался
любое расхождение     BinaryTypeException с именем типа и члена — ошибка контракта, а не данных
```

Сравнение `typeof(TMember)` с сохранённым `Type` в generic-методе JIT сворачивает; цена сверки —
один счётчик и одно сравнение на член.

## 8.5 Реализация рефлексией в v1.0

```csharp
internal sealed class ReflectedContract<T> : TypeContract<T>
{
    private readonly MemberAccessor<T>[] _accessors;                 // по одному на член, в порядке описания
    private readonly FrozenDictionary<int, MemberAccessor<T>> _byKey; // только для keyed

    public override void Write(ref MemberWriter writer, in T value)
    {
        foreach (var accessor in _accessors)
            accessor.WriteTo(ref writer, in value);
    }

    public override void Read(ref MemberReader reader, ref T value)
    {
        foreach (var accessor in _accessors)
            accessor.ReadFrom(ref reader, ref value);
    }

    public override bool ReadField(ref MemberReader reader, int key, ref T value) =>
        _byKey.TryGetValue(key, out var accessor) && accessor.ReadFieldFrom(ref reader, ref value);
}

internal delegate TMember Getter<T, TMember>(in T owner);
internal delegate void Setter<T, TMember>(ref T owner, TMember value);

internal abstract class MemberAccessor<T>
{
    public abstract void WriteTo(ref MemberWriter writer, in T owner);
    public abstract void ReadFrom(ref MemberReader reader, ref T owner);
    public abstract bool ReadFieldFrom(ref MemberReader reader, ref T owner);
}

internal sealed class MemberAccessor<T, TMember> : MemberAccessor<T>
{
    private readonly Getter<T, TMember> _get;
    private readonly Setter<T, TMember> _set;
    private readonly int? _key;

    public override void WriteTo(ref MemberWriter writer, in T owner)
    {
        if (_key is { } key)
            writer.Field(key, _get(in owner));
        else
            writer.Member(_get(in owner));
    }

    public override void ReadFrom(ref MemberReader reader, ref T owner) =>
        _set(ref owner, reader.Member<TMember>());

    public override bool ReadFieldFrom(ref MemberReader reader, ref T owner)
    {
        _set(ref owner, reader.Value<TMember>());
        return true;
    }
}
```

- `MemberAccessor<T, TMember>` создаётся один раз на член через `MakeGenericType`; геттер и сеттер
  компилируются один раз. Бокса нет: `TMember` типизирован от начала до конца.
- Setter по `ref T`: для struct-владельца присваивание идёт в сам экземпляр, а не в копию.
- Порядок и ключи `_accessors` строятся по сегодняшним правилам `TypeContractCache.Build`
  (INV-12: `[BinaryOrder]`, затем ordinal имени, затем уровень наследования — база первой) и со
  всеми сегодняшними отказами при построении (`[BinaryKey]` без `[BinaryContract]`, дубликаты,
  делегаты и т. д.).
- Путь рефлексии помечается `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` на публичных
  точках входа (R8).

## 8.6 Порядок работы движка

**Запись объекта** объявленного типа `D` со значением `v`:

```text
1. null и кадр ссылки (§4.2, §4.3)                          движок
2. глубина +1, бюджет узлов −1, обнаружение цикла           движок
3. runtime-тип R ≠ D: байт тега union, контракт типа R      движок
4. keyed: varint числа полей (с +1, если D может быть null
   и ссылки выключены, §4.2)                                движок
5. contract.Write(ref writer, v)                            контракт: только Member / Field
6. сверка числа вызовов (§8.4)                              движок
```

**Чтение** — зеркально, с двумя правилами порядка:

```text
- экземпляр создаётся (Create) и регистрируется в таблице ссылок ДО чтения членов, иначе цикл,
  возвращающийся к нему, не разрешится; при populate-in-place вместо Create берётся переданный
  экземпляр
- keyed: движок читает число полей; для каждого — ключ, int32 длину, проверку против оставшихся
  байт, окно ровно на поле; contract.ReadField → true: окно обязано быть прочитано ровно;
  false: пропуск по длине
```

## 8.7 Генератор после релиза — иллюстрация, не норма

Ниже — эскиз, показывающий, что шова §8.3 достаточно. Регистрация и доступ к непубличным членам
решаются при выпуске генератора (§8.8) и могут выглядеть иначе.

Код пользователя:

```csharp
[BinaryContract]
public partial class Person
{
    [BinaryKey(1)] public string Name { get; set; } = "";
    [BinaryKey(2)] public int Age { get; set; }
    [BinaryKey(3)] public Person? Manager { get; set; }
}
```

Сгенерировано в сборке пользователя (`Person.Viper.g.cs`):

```csharp
partial class Person
{
    private sealed class __ViperContract : TypeContract<Person>
    {
        public __ViperContract() : base(MemberLayout.Keyed,
            Member<string>(key: 1, "Name"), Member<int>(key: 2, "Age"), Member<Person?>(key: 3, "Manager")) { }

        public override Person Create() => new();

        public override void Write(ref MemberWriter w, in Person v)
        {
            w.Field(1, v.Name);
            w.Field(2, v.Age);
            w.Field(3, v.Manager);
        }

        public override void Read(ref MemberReader r, ref Person v) =>
            throw new NotSupportedException();                    // keyed: движок вызывает ReadField

        public override bool ReadField(ref MemberReader r, int key, ref Person v)
        {
            switch (key)
            {
                case 1: v.Name = r.Value<string>(); return true;
                case 2: v.Age = r.Value<int>(); return true;
                case 3: v.Manager = r.Value<Person?>(); return true;
                default: return false;
            }
        }
    }

    [ModuleInitializer]
    internal static void __ViperRegister() => ContractRegistry.Register(new __ViperContract());
}
```

Для позиционного типа — `w.Member(v.Age); w.Member(v.Name);` в порядке INV-12, вычисленном при
компиляции по тем же правилам, что §8.5. Байты сгенерированного контракта и `ReflectedContract<T>`
обязаны совпадать; это проверяют тесты соответствия (R8, `CONF-*`) и замороженные фикстуры.

Путь `Serialize(person)` с генератором:

```text
FormatterCache<Person>.Instance → объектный форматтер
  контракт: ContractRegistry → сгенерированный; нет → ReflectedContract<Person>
движок: кадр/null · глубина · узлы · varint числа полей 04 (3 + 1)
  contract.Write → w.Field(1, "Ada")
    движок: varint 01 · резерв int32 · FormatterCache<string>.Write → 04 41 64 61 · дописать длину 4
  … w.Field(2, 36) → 02 · 04 00 00 00 · 24 00 00 00
  … w.Field(3, null) → 03 · 01 00 00 00 · 00
движок: сверка — 3 вызова, ключи 1, 2, 3
```

## 8.8 Откладывается до генератора

- Публикация `TypeContract<T>`, `MemberWriter`, `MemberReader` (выбор между «генератор выдаёт
  только описания» и «генератор выдаёт методы»).
- Доступ к непубличным членам с `[BinaryInclude]`: требовать `partial` или `[UnsafeAccessor]`.
- Регистрация сгенерированного контракта: `[ModuleInitializer]` + реестр, статический абстрактный
  член на типе, или контекст в опциях (как `JsonSerializerContext`).

---

# 9. Решения из Rework-Plan §13

- **9.1** [ПОДТВЕРЖДЕНО] D-9: номер версии нового V1 — 1 (§2.1).
- **9.2** [ПОДТВЕРЖДЕНО] D-2 (отпечаток схемы), D-4 (Zstandard, LZ4), D-10 (метка релиза генератора) —
  после релиза, аддитивно: отпечаток — новый критичный сервис с новым номером, Zstd/LZ4 — новые
  алгоритмы в отдельных пакетах, генератор — §8. В v1.0 не делаются и не блокируют ни один этап.

- **9.3** [ПОДТВЕРЖДЕНО] D-6: удаляются целиком `StreamExtensions` (12 методов чтения и
  `stream.Serialize`) и `BinarySerializerOptions.FromHeader` ×3 / `FromStream` ×3. В построитель
  добавляется ключ для чтения отдельно от выбора шифрования записи:

```csharp
public BinarySerializerOptionsBuilder WithKeys(ReadOnlySpan<byte> key, string? keyId = null);
public BinarySerializerOptionsBuilder WithKeys(Func<string?, byte[]?> keyResolver);
public BinarySerializerOptionsBuilder WithKeys(IKeyProvider keys);
```

  `WithEncryption(algorithm, key | resolver | provider, keyId)` не меняется и по-прежнему задаёт и
  ключи. Задать ключи и через `WithEncryption`, и через `WithKeys` — [СЛЕДСТВИЕ]
  `BinaryConfigurationException` при `Build()` (одно место для ключей). `BinaryFormatInspector.Peek`
  остаётся — для диагностики и выбора ключа. Причина: чтение V1 и так берёт алгоритмы из заголовка;
  «конфигурация по заголовку» существовала только потому, что ключ для чтения можно было передать
  лишь вместе с шифрованием записи. Пример:

```csharp
var reader = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithKeys(keyId => vault.Get(keyId))
    .Build());
Order? order = reader.Deserialize<Order>(stream);   // любой V1-кадр, с шифрованием или без
```

- **9.4** [ПОДТВЕРЖДЕНО] D-8: заполнение существующего объекта — отдельный метод `Populate`, только
  для классов, только синхронные источники:

```csharp
public void Populate<T>(ReadOnlySpan<byte> source, T target) where T : class;
public void Populate<T>(ReadOnlySequence<byte> source, T target) where T : class;
public void Populate<T>(Stream source, T target) where T : class;
```

  Перегрузки `Deserialize<T>(…, T existingInstance)` и `Deserialize<T>(…, ref T existingInstance)`
  удаляются; для struct — обычное `value = serializer.Deserialize<T>(…)` (поведение то же). `byte[]`
  приводится к `ReadOnlySpan<byte>`. Правила §3.1 контракта сохраняются: только member-encoded типы
  (иначе `BinaryTypeException`); null-корень или корень-обратная ссылка — `BinaryFormatException`;
  другой runtime-тип union — `BinaryTypeException`; пустой вход объект не трогает; заполняется только
  корень, вложенные объекты создаются заново; у keyed-контракта отсутствующие в payload поля сохраняют
  текущее значение. Асинхронные формы `PopulateAsync` — в v1.0, см. 9.5.

- **9.5** [ПОДТВЕРЖДЕНО] D-5: асинхронность только на границе кадра. Движок никогда не ожидает:
  чтение асинхронно набирает один целый V1-кадр (длину даёт заголовок ≤ 4 КиБ), затем разбирает его
  синхронно; запись синхронно строит кадр в собственном буфере (§6), затем асинхронно отдаёт байты.
  Полностью асинхронный движок отвергнут. Методы:

```csharp
public ValueTask     SerializeAsync<T>(Stream destination, T value, CancellationToken cancellationToken = default);
public ValueTask     SerializeAsync<T>(PipeWriter destination, T value, CancellationToken cancellationToken = default);
public ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken cancellationToken = default);
public ValueTask<T?> DeserializeAsync<T>(PipeReader source, CancellationToken cancellationToken = default);
public ValueTask     PopulateAsync<T>(Stream source, T target, CancellationToken cancellationToken = default) where T : class;
public ValueTask     PopulateAsync<T>(PipeReader source, T target, CancellationToken cancellationToken = default) where T : class;
```

  Отмена и сбой:

```text
токен              проверяется, пока ожидаются байты; разбор кадра, уже лежащего в памяти, не
                   прерывается — он ограничен лимитами
PipeReader         при отмене или сбое ничего не потребляется: AdvanceTo(начало кадра, прочитанный конец)
Stream (чтение)    вынутые байты не возвращаются; после отмены или сбоя позиция не определена, поток
                   непригоден для дальнейшего разбора кадров (как §20 для синхронного пути)
запись             до начала отдачи байт отмена ничего не оставляет (буфер атомарен); отмена во время
                   WriteAsync / FlushAsync может оставить часть кадра — свойство назначения
OperationCanceledException   стандартное исключение .NET, вне таксономии §8
```

- **9.6** [ПОДТВЕРЖДЕНО] Асинхронное чтение принимает только V1. V0 читается только синхронно, из
  кадра, который выделил вызывающий. Встретив V0 (включён `AllowV0Fallback`, magic нет), асинхронный
  вход бросает `NotSupportedException`, называющий правило. Асинхронная *запись* V0 разрешена.

  **Обязательный текст для потребителя.** Объяснение ниже с примером переносится (по-английски) в
  контракт §10.2 и §3.1 на этапе R3, в XML-документацию `DeserializeAsync` и `AllowV0Fallback` и в
  потребительскую документацию `docs/` к релизу:

  > V0 не несёт ни magic, ни длины: это кодек для протоколов, у которых уже есть собственное
  > кадрирование — длина сообщения, тип сообщения, канал. Раз границы сообщения знает протокол,
  > вызывающий уже держит байты одного сообщения и читает их синхронно. Асинхронное ожидание нужно
  > только тому, кто не знает, где кончается сообщение, а у V0 это знает протокол, а не Viper.

```csharp
// V1 из сокета: границу кадра знает Viper (заголовок несёт длину)
Order? order = await serializer.DeserializeAsync<Order>(networkStream, cancellationToken);

// V0 внутри собственного протокола: границу кадра знает протокол
var compact = new BinarySerializer(BinarySerializerOptions.Configure()
    .WithVersion(0).AllowV0Fallback().Build());

while (true)
{
    ReadResult read = await pipe.ReadAsync(cancellationToken);
    ReadOnlySequence<byte> buffer = read.Buffer;

    // собственный протокол: 4 байта длины (little-endian), затем V0-payload
    if (TryReadFrame(ref buffer, out ReadOnlySequence<byte> frame))
    {
        Order? message = compact.Deserialize<Order>(frame);   // синхронно: кадр уже целиком в памяти
        Handle(message);
    }

    pipe.AdvanceTo(buffer.Start, buffer.End);
    if (read.IsCompleted) break;
}

static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frame)
{
    var reader = new SequenceReader<byte>(buffer);
    if (!reader.TryReadLittleEndian(out int length) || reader.Remaining < length)
    {
        frame = default;
        return false;
    }

    frame = buffer.Slice(reader.Position, length);
    buffer = buffer.Slice(frame.End);
    return true;
}
```

- **9.7** [ПОДТВЕРЖДЕНО] `SerializePooled` возвращает класс `PooledPayload : IDisposable`: держит
  арендованный массив; `Memory` / `Span` доступны до `Dispose`; `Dispose` повторно безопасен, очищает
  байты и возвращает массив в пул; доступ после `Dispose` — `ObjectDisposedException`. `struct`
  отвергнут: копия struct — вторая владеющая ссылка, двойной `Dispose` вернул бы массив в пул дважды,
  и данные одного сообщения попали бы в другое.

```csharp
public PooledPayload SerializePooled<T>(T value);

using PooledPayload payload = serializer.SerializePooled(order);
await socket.SendAsync(payload.Memory, cancellationToken);
```

- **9.8** [ПОДТВЕРЖДЕНО] Чтение с отчётом о прочитанном — родной тип для каждого источника, для V0 и
  V1 одинаково:

```csharp
public T? Deserialize<T>(ReadOnlySpan<byte> source, out int bytesConsumed);
public T? Deserialize<T>(ReadOnlySequence<byte> source, out SequencePosition consumed);
```

  Без этой перегрузки span / sequence — ровно один payload (V0) или ровно один кадр (V1); байты после
  него — `BinaryFormatException`. С ней чтение останавливается в конце корня (V0) или кадра (V1) и
  сообщает, где. `SequencePosition` передаётся прямо в `PipeReader.AdvanceTo`.

- **9.9** [ПОДТВЕРЖДЕНО] Отдельных перегрузок чтения для `byte[]` нет: массив неявно приводится к
  `ReadOnlySpan<byte>`, вызовы `Deserialize<T>(bytes)` и `Populate(bytes, target)` компилируются без
  изменений. Следствие: `null`-массив становится пустым span и отвергается как пустой payload
  (`BinaryFormatException`), а не `ArgumentNullException`. Запись `byte[] Serialize<T>(T value)`
  остаётся.

- **9.10** [ПОДТВЕРЖДЕНО] Итоговая публичная поверхность `BinarySerializer` v1.0 (параметр
  `cancellationToken` сокращён до `ct`; у всех асинхронных методов он `= default`):

```csharp
public BinarySerializer(BinarySerializerOptions? options = null);

// запись
public void          Serialize<T>(IBufferWriter<byte> destination, T value);
public byte[]        Serialize<T>(T value);
public PooledPayload SerializePooled<T>(T value);
public void          Serialize<T>(Stream destination, T value);
public ValueTask     SerializeAsync<T>(Stream destination, T value, CancellationToken ct = default);
public ValueTask     SerializeAsync<T>(PipeWriter destination, T value, CancellationToken ct = default);

// чтение
public T?            Deserialize<T>(ReadOnlySpan<byte> source);
public T?            Deserialize<T>(ReadOnlySpan<byte> source, out int bytesConsumed);
public T?            Deserialize<T>(ReadOnlySequence<byte> source);
public T?            Deserialize<T>(ReadOnlySequence<byte> source, out SequencePosition consumed);
public T?            Deserialize<T>(Stream source);
public ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken ct = default);
public ValueTask<T?> DeserializeAsync<T>(PipeReader source, CancellationToken ct = default);
public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(Stream source, CancellationToken ct = default);     // §9.21
public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(PipeReader source, CancellationToken ct = default); // §9.21

// заполнение существующего объекта — симметрично чтению
public void          Populate<T>(ReadOnlySpan<byte> source, T target) where T : class;
public void          Populate<T>(ReadOnlySpan<byte> source, T target, out int bytesConsumed) where T : class;
public void          Populate<T>(ReadOnlySequence<byte> source, T target) where T : class;
public void          Populate<T>(ReadOnlySequence<byte> source, T target, out SequencePosition consumed) where T : class;
public void          Populate<T>(Stream source, T target) where T : class;
public ValueTask     PopulateAsync<T>(Stream source, T target, CancellationToken ct = default) where T : class;
public ValueTask     PopulateAsync<T>(PipeReader source, T target, CancellationToken ct = default) where T : class;
```

  Удаляются по сравнению с сегодняшним: `Deserialize<T>(byte[])`, все `Deserialize` с существующим
  экземпляром (класс и `ref` struct), весь `StreamExtensions`, `FromHeader` / `FromStream`.
  Добавляются в построитель: `WithKeys(...)` ×3 (§9.3). Остаётся: `BinaryFormatInspector`.
  Правила для каждого входа: пустой вход — `BinaryFormatException`; без «сколько прочитано» — ровно
  один payload / кадр (§9.8); асинхронное чтение — только V1 (§9.6).

- **9.11** [ПОДТВЕРЖДЕНО] Интерфейсы алгоритмов v1.0 — по одному методу на направление, без
  реализаций по умолчанию:

```csharp
public interface ICompressionAlgorithm
{
    CompressionAlgorithm Kind { get; }
    string? CustomName { get; }

    void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination);

    // Пишет ровно expectedLength байт; больше, меньше или незавершённый поток — BinaryFormatException.
    void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength);
}

public interface IChecksumAlgorithm
{
    ChecksumAlgorithm Kind { get; }
    string? CustomName { get; }
    int HashSizeInBytes { get; }                 // 1…255
    void Compute(ReadOnlySpan<byte> source, Span<byte> destination);   // destination.Length == HashSizeInBytes
}

public interface IEncryptionAlgorithm
{
    EncryptionAlgorithm Kind { get; }
    string? CustomName { get; }
    bool AuthenticatesAssociatedData { get; }
    int KeySizeInBytes { get; }

    int GetCiphertextLength(int plaintextLength);          // точная, ≥ plaintextLength

    // destination.Length == GetCiphertextLength(plaintext.Length); заполняется целиком.
    void Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key,
                 ReadOnlySpan<byte> associatedData, Span<byte> destination);

    // destination.Length == ciphertext.Length; возвращает длину открытого текста.
    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> associatedData, Span<byte> destination);
}
```

  Удаляются: `GetMaxCompressedLength`, `GetMaxCiphertextLength`, `Compress`/`Decompress`
  «span → span», `SupportsIncrementalDecompression`, `Encrypt`/`Decrypt` без associated data.
  Отвергнуты: `IBufferWriter` для шифрования (размер известен точно, §3.2–3.3); `EncryptionGuarantee`
  (средний уровень не использует ни одна политика); реализации по умолчанию в v1.0 (именно они
  давали тихие ошибки — AAD, выброшенные по умолчанию). После релиза новый член добавляется только с
  реализацией по умолчанию — аддитивно.

  Проверки сервисами [СЛЕДСТВИЕ]:

```text
KeySizeInBytes       статический ключ — при Build(), BinaryConfigurationException;
                     ключ от провайдера — при получении, BinaryEncryptionKeyException
GetCiphertextLength  результат < plaintextLength или отрицательный → BinaryConfigurationException
Encrypt              заполнено не ровно destination.Length → BinaryConfigurationException (ошибка алгоритма)
Decrypt              результат вне 0…ciphertext.Length → BinaryConfigurationException
Decompress           записано не ровно expectedLength → BinaryFormatException
HashSizeInBytes      вне 1…255 → BinaryConfigurationException (NX-11)
```

- **9.12** [ПОДТВЕРЖДЕНО] Встроенные алгоритмы v1.0:

```text
сжатие       Deflate · Brotli                                   без изменений; ZLib не добавляется
сумма        Crc32 · XxHash3 (64 бит) · XxHash128 (128 бит)     новые — из System.IO.Hashing (уже зависимость);
                                                                Crc64 и XxHash64 не добавляются
шифрование   Aes256Gcm · ChaCha20Poly1305                       новый — из BCL; nonce 12, тег 16, ключ 32
ключи        StaticKeyProvider · DelegateKeyProvider · HkdfKeyProvider
                                                                новый: ключ сообщения = HKDF(корневой ключ,
                                                                info = KeyId); корневой ключ не раскрывается,
                                                                выведенный — собственная копия (SecretKey)
после релиза Zstandard, LZ4, AES-GCM-SIV — отдельные пакеты (§9.2)
```

  `ChaCha20Poly1305.IsSupported == false` → `BinaryFormatNotSupportedException` с именем алгоритма:
  при `Build()`, если алгоритм выбран для записи, и при чтении кадра, который его называет.
  Контрольная сумма по умолчанию — нет. Значения перечислений: новые встроенные получают следующие
  свободные номера; `Custom` = 255 во всех трёх перечислениях (в теле сервиса — varint `FF 01`).

- **9.13** [ПОДТВЕРЖДЕНО] Типизированный движок (R4):

  **(1) Форма — типизированные формы + кодеки движка.** Форматтер описывает только форму; цикл по
  данным с провода живёт в кодеке движка (INV-5 — устройство кода, а не соглашение). Единый
  `IFormatter<T>` для всего отвергнут: форматтер коллекции писал бы цикл по счётчику с провода сам.

```csharp
internal interface IScalarFormatter<T>
{
    void Write(ref WireWriter writer, T value);
    T Read(ref WireReader reader);
}

internal interface ISequenceShape<TCollection, TElement>
{
    int? CountOf(TCollection collection);
    TCollection Create(int capacity);
    void Add(ref TCollection builder, TElement element);
    TCollection Complete(TCollection builder);
}

internal interface IMapShape<TMap, TKey, TValue>
{
    int? CountOf(TMap map);
    TMap Create(int capacity);
    void Add(ref TMap builder, TKey key, TValue value);
    TMap Complete(TMap builder);
}
// объекты — TypeContract<T> (§8); композиты (кортежи, KeyValuePair, Lazy, многомерные массивы) —
// типизированные композиты с CompositeWriter/CompositeReader без сырых чисел (NX-12)

internal sealed class SequenceCodec<TCollection, TElement>(ISequenceShape<TCollection, TElement> shape)
{
    public TCollection Read(ref WireReader reader, ref OperationState state)
    {
        ElementCount count = reader.ReadCount(CountKind.Collection, ref state);
        var builder = shape.Create(count.CapacityHint);
        for (int i = 0; i < count.Value; i++)
            shape.Add(ref builder, Engine.Read<TElement>(ref reader, ref state));
        return shape.Complete(builder);
    }
}
```

  `FormatterCache<T>.Instance` — один кодек на `T` (скаляр, последовательность, словарь, объект,
  `Nullable<T>`, полиморфный слот), собирается один раз; поиск — чтение статического поля.

  **(2) Перечисление при записи.** `T[]`, `List<T>`, `ImmutableArray<T>` — через span
  (`CollectionsMarshal.AsSpan`); коллекции BCL со struct-энумератором (`HashSet<T>`,
  `Dictionary<K,V>`, `Queue<T>`, `Stack<T>`, `SortedSet<T>`, …) — через него в своей форме;
  остальное — `IEnumerable<T>` (одна аллокация энумератора).

  **(3) Массив при чтении.** Если `count × sizeof(T) ≤ оставшихся байт` (элемент в памяти не больше
  элемента на проводе — примитивы и `unmanaged`-структуры фиксированной ширины), массив выделяется
  сразу точной длины и читается в него; иначе элементы копятся в арендованном буфере, и в конце
  создаётся один массив точной длины. Правило NX-01 («память — по доставленным байтам») соблюдается
  в обоих путях.

- **9.14** [ПОДТВЕРЖДЕНО] Аллокации и цели (замена `Rework-Plan.md` §11).

```text
обнаружение циклов при записи   стек предков текущего пути (массив из пула, длина ≤ MaxDepth) и
(ссылки выключены)              линейный поиск по ссылке; цикл — BinaryTypeException, как сегодня.
                                HashSet на операцию убирается
таблицы ссылок                  из пула на операцию, очищаются и возвращаются
ёмкость коллекций при чтении    если count × минимальный размер элемента на проводе ≤ оставшихся
                                байт — ёмкость сразу count; иначе рост от CapacityHint (≤ 1024)
async                           [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
AesGcm / ChaCha20Poly1305       новый экземпляр на операцию (ключевое расписание живёт только в
                                вызове); кэш по ключу отвергнут
боксинг                         только в полиморфном слоте ([BinaryUnion], интерфейс, object);
                                enum — через Unsafe.As, не через Convert.ChangeType
```

  Цели по путям после прогрева. Каждая строка — чекпойнт бенчмарк-плана; до замера — цель, не
  утверждение (§28 бенчмарк-плана):

```text
запись в IBufferWriter, V1 или V0, без фаз, record из примитивов и строк     0 байт
то же со сжатием Brotli                                                      0 байт управляемой памяти
то же со сжатием Deflate                                                     объект DeflateStream
то же с шифрованием                                                          объект AesGcm / ChaCha20Poly1305
Serialize<T>(T) → byte[]                                                     ровно возвращаемый массив
SerializePooled                                                              объект PooledPayload
чтение из span, record из примитивов                                         ровно возвращаемый объект
чтение графа                                                                 ровно граф; без промежуточных копий
                                                                             и перевыделений, когда счётчик
                                                                             подкреплён байтами
с PreserveReferences                                                         как без них
асинхронные методы                                                           как синхронные
первое использование типа                                                    кодек и контракт, один раз
```

- **9.15** [ПОДТВЕРЖДЕНО] Инварианты рефакторинга (замена `Rework-Plan.md` §3). Каждый держится
  структурным тестом; тест переписывается вместе с кодом и не удаляется. Этап, который не может
  выполнить инвариант, останавливается, и вопрос идёт владельцу.

```text
INV-1   один OperationState (struct по ref) на публичный вызов; ниже никто не строит лимиты,
        бюджет или ключи
INV-2   монополия на байты: WireReader / WireWriter; объектам — только MemberWriter / MemberReader
        без байтов, счётчиков и позиции
INV-3   объявленная длина или счётчик сверяется с доставленными байтами до выделения памяти; то же
        правило решает, выделять ли массив точной длины и ёмкость коллекции
INV-4   цикл по данным с провода — только по проверенному ElementCount
INV-5   один владелец обхода: форматтеры описывают форму, цикл — в кодеке движка; контракт типа
        (рефлексия или сгенерированный) задаёт только порядок членов, доступ, создание, реакцию на ключ
INV-6   алгоритмы — чистая механика, вызываются внутри барьера фаз, лимитов не видят
INV-7   нет процессного изменяемого реестра, способного изменить алгоритм
INV-8   на проводе только теги, никогда имена типов
INV-9   у каждого поля ровно одна кодировка: bool 0/1; строгий UTF-8; минимальный varint;
        ключи keyed-полей строго по возрастанию; записи сервисов по возрастанию номера, каждый
        не более раза, номер 0 недопустим; бит критичности совпадает с контрактом; null — одна
        запись, в первом числе значения; нет записи с id = None; зарезервированные биты режима — 0;
        дубликат в коллекции — ошибка
INV-10  таксономия исключений полна: ничто с пути payload не выходит под именем фреймворка
INV-11  ключевой материал — всегда своя копия; сериализатор очищает только то, чем владеет
INV-12  план членов — полный порядок по цепочке наследования, одинаковый для ReflectedContract<T>
        и сгенерированного контракта (тесты соответствия CONF-*)
INV-13  лимиты — политика, проверяются один раз; payload не может их поднять
INV-14  заголовок защищён целиком: AAD — точные байты заголовка от magic до onDiskLength
INV-15  ошибка данных или графа не оставляет в назначении ни одного байта (шифрование начинается
        только после того, как payload целиком собран в буфере)
INV-16  движок никогда не ожидает: await — только на границе кадра; в Engine/ и Formatters/ нет
        асинхронных методов
INV-17  боксинг — только в полиморфном слоте; проверяется считающим тестовым двойником
INV-18  payload V0 равен payload V1 того же значения без ссылок, байт в байт
```

- **9.16** [ПОДТВЕРЖДЕНО] W11: корень прочитан ровно — хвост после корня внутри V1-кадра —
  `BinaryFormatException`; для span / sequence то же по умолчанию и для V0 (§9.8). W12: фикстуры
  `Fixtures/Wire/*.bin` перезамораживаются один раз — на этапе смены формата — из корпуса;
  исключение записывается в `CLAUDE.md`, правило «никогда не перегенерировать» восстанавливается в
  том же изменении.

- **9.17** [ПОДТВЕРЖДЕНО] Этапы (замена `Rework-Plan.md` §12). Порядок — сначала нутро (R1–R5),
  потом формат (R6): код кодирования пишется один раз, старые фикстуры и оракул ловят изменения
  поведения на R1–R5. Правила выполнения — `Rework-Plan.md` §0 (по одному этапу; набор зелёный в
  конце каждого; контракт, QA- и бенчмарк-планы меняются в том же этапе, что код).

```text
R0  замер «до» и оракул                         формат не меняется
    - локальный тег pre-rework (не v*: CD его не видит), полный прогон Track A как Baseline,
      тег удаляется; сырые результаты коммитятся в benchmarks/.../Baselines/pre-rework/
    - оракул: один текстовый файл SHA-256 вывода по каждому случаю корпуса (§23-семейства и формы
      графа; V0 и V1; со ссылками и без); тест сравнивает и при расхождении печатает hex обоих
      вариантов; удаляется на R6
    gate: замер закоммичен; оракул закоммичен; набор зелёный
R1  WireReader / WireWriter (ref struct) над буферами; ValueReader / ValueWriter удаляются
    gate: фикстуры и оракул байт в байт; нет виртуального вызова на байт
R2  собственный буфер из пула для V0 и V1; длины keyed-полей дописываются в нём; атомарная
    запись (INV-15); удаляются MeteredReadStream, MeteredWriteStream, WindowReadStream и
    MemoryStream в пути payload; стек предков для циклов; таблицы ссылок из пула
    gate: V0 keyed пишется в non-seekable назначение; в Pipeline/ нет MemoryStream;
          фикстуры и оракул байт в байт
R3  публичная поверхность §9.10 целиком; удаляются StreamExtensions, FromHeader / FromStream;
    WithKeys; чтение non-seekable; граница V0 (§5.6, §9.6, §9.8); async (§9.5)
    gate: каждый вход читает non-seekable источник; тест-двойник, падающий на лишнем чтении,
          доказывает «ровно один кадр»; эквивалентность входов (Api/CrossEntryPointTests)
R4  типизированный движок (§9.13), шов TypeContract<T> / ReflectedContract<T> (§8), ёмкость по
    подкреплённому счётчику, пуловые async-автоматы (§9.14)
    gate: фикстуры и оракул байт в байт; INV-17; холодный старт сравнён с R0, регресс описан в
          internal/performance/
R5  интерфейсы алгоритмов (§9.11), новые встроенные (§9.12)
    gate: каждый алгоритм — круговой перенос через пайплайн; KeySizeInBytes при Build()
R6  новый формат целиком: заголовок-сервисы (§2), AAD (§3), payload (§4); фикстуры
    перезамораживаются один раз (W12); оракул удаляется
    gate: каждое правило §22 контракта приколото байтовым тестом; новые фикстуры; размеры
          сравнены с R0
R7  снят: фазы V0 отменены (§5.1), остальное — в R2 и R3
R8  тесты соответствия CONF-* и аннотации AOT / trimming на публичных входах рефлексии
    gate: CONF-* проходят на ReflectedContract<T>; проект-потребитель под AOT-анализом видит
          только помеченные входы
R9  контракт, QA- и бенчмарк-планы сведены полностью; README трёх пакетов; Track A на кандидате в
    релиз; пробный прогон CD
    gate: §24 контракта и §32 QA-плана закрыты; dotnet pack проходит для всех трёх пакетов
после релиза  Track B на теге v1.0.0
```

- **9.18** [ПОДТВЕРЖДЕНО — переименовать; способ — рекомендация a, владелец может сменить]
  Встроенные алгоритмы получают суффикс семейства, чтобы не совпадать с типами BCL и других
  библиотек (`System.IO.Hashing.Crc32`, `XxHash3`, `XxHash128`,
  `System.Security.Cryptography.ChaCha20Poly1305`). Стиль — тот же, что у `NoChecksum`,
  `NoCompression`, `NoEncryption`. Переименование — на этапе R5, вместе с интерфейсами.

```text
Crc32        → Crc32Checksum            новые: XxHash3Checksum · XxHash128Checksum
Deflate      → DeflateCompression       Brotli → BrotliCompression
Aes256Gcm    → Aes256GcmEncryption      новый: ChaCha20Poly1305Encryption
без изменений: NoChecksum · NoCompression · NoEncryption · StaticKeyProvider ·
               DelegateKeyProvider · HkdfKeyProvider
значения перечислений (ChecksumAlgorithm.Crc32 и т. д.) не меняются
```

  Отвергнуто: переименовать только конфликтующие (два стиля в одном API); префикс `Viper`
  (шум); статические фабрики с внутренними классами (лишний слой, тип нельзя проверить).

- **9.19** [ПОДТВЕРЖДЕНО] Два пробела плана, закрытые 2026-09-26:

  **Кэши (R4).** Папка `src/ViShap.Viper.Serialization/Cache/` удаляется целиком вместе с кэшем
  `FormatterRegistry`: `ActivatorCache`, `MethodInvokerCache`, `DictionaryAccessorCache`,
  `TupleAccessorCache`, `LazyAccessorCache`, `FrozenFactoryCache`, `ImmutableFactoryCache`,
  `ReadOnlySequenceAccessorCache`, `CollectionCountCache`. Все они существуют, потому что движок
  работает с `object` и `Type`; типизированные формы вызывают `new`, `Add`, `Count`, `Key`, `Item1`,
  `Value`, `ToFrozenSet<T>`, `ImmutableArray.Create<T>` напрямую. Остаются: `FormatterCache<T>`
  (статическое поле), кэш контрактов по типу (`TypeContract<T>`, полиморфный слот ищет по
  runtime-типу), кэш union-карт, кэш фабрик форм по определению generic-типа (одна
  `MakeGenericType` на закрытый тип). Тесты `Concurrency/CacheTests` переписываются под оставшиеся
  кэши: `CN-06…CN-13` и `CN-19` выводятся из работы, `CN-03…CN-05`, `CN-14…CN-16` переносятся на
  новые кэши.

  **Корпус оракула (R0).** Оракул не изобретает собственных случаев: он хэширует вывод по готовым
  корпусам тестового проекта — `tests/.../RoundTrip/Corpus*.cs` (примитивы, время и системные типы,
  массивы, композиты, коллекции) под каждым профилем `CorpusProfiles`, V0-корпус
  `Format/V0CorpusTests`, графы ссылок `References/` и keyed-формы `Contracts/` — под V0 и V1, со
  ссылками и без, где формат это допускает.

- **9.20** [ПОДТВЕРЖДЕНО] Генератор — после релиза (вариант a), аннотации AOT — в v1.0 (R8). Путь
  аддитивной интеграции `ViShap.Viper.Generator` записан в `Rework-Plan.md` §13.1 явно; открытые на
  тот момент решения перечислены там же и принимаются при выпуске генератора.

- **9.21** [ПОДТВЕРЖДЕНО] Непрерывное чтение потока кадров — в v1.0:

```csharp
public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(Stream source, CancellationToken cancellationToken = default);
public IAsyncEnumerable<T?> DeserializeAsyncEnumerable<T>(PipeReader source, CancellationToken cancellationToken = default);
```

```text
каждый кадр          отдельная операция: свой OperationState, свои лимиты и бюджеты — поток кадров
                     не ограничен ничем, каждый кадр ограничен всем
конец источника      ровно между кадрами — перечисление завершается;
                     посреди кадра — BinaryFormatException
V0                   не поддерживается — NotSupportedException (границы знает протокол вызывающего, §9.6)
отмена               как §9.5; PipeReader при отмене не потребляет начатый кадр
```

  Причина: `DeserializeAsync` в цикле не отличает нормальный конец потока (пустой вход →
  `BinaryFormatException`) от обрыва посреди кадра. Отвергнуто: оставить пользователю ловить
  исключение; `TryDeserializeAsync` с кортежем.

---

# 10. Открытые вопросы — сводка

Открытых вопросов нет. P8/P9 исправлены при переписывании планов 2026-09-26: коллизии ID в
`Benchmark-Plan-Changes.md` (`MICRO-13/14` → `MICRO-15/16`, `WL-15` → `WL-18`), ошибочный префикс
`PM-17…20` → `CTR-27…30` в `QA-Plan-Changes.md`, «одиннадцать фикстур» → тринадцать. Неточности
`Audit-Future.md` записаны в его §10 «Поправки». `Rework-Plan.md` и три файла `*-Changes.md`
переписаны по этому документу.
