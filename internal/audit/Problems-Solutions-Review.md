Проверил именно приложенный **`Audit remediation design`**. В целом дизайн стал намного сильнее предыдущего варианта: он уже рассматривает не 19 независимых симптомов, а несколько корневых архитектурных причин. Особенно хорошо выделены общие слои `BudgetScope`, pre-allocation check, bounded materialization, operation-relative write budget и reference layer.   

Но **как implementation blueprint я бы его пока не утверждал**. Нашёл несколько важных мест.

### 1. Самая серьёзная проблема — S06 всё ещё не определён достаточно точно

В §1.2 предлагается:

```text
IRemainingByteSource.RemainingBytes
```

для `BoundedReadStream` и `BudgetedReadStream`, причём для `BudgetedReadStream` это только:

```text
_maxBytes - _bytesRead
```



Здесь получается проблема.

Допустим:

```text
MaxWireBytes = 64 MB
source реально содержит 29 bytes
header.OnDiskLength = 8 MB
```

`BudgetedReadStream.RemainingBytes` скажет:

```text
64 MB
```

и pre-flight разрешит allocation 8 MB, хотя реальный underlying stream уже закончится через 29 bytes.

То есть предложенный механизм гарантированно закрывает:

```text
declared > budget
```

но не гарантированно закрывает:

```text
declared > physically available source
```

А именно второе тоже входит в S06.

### Правильнее иметь две независимые проверки

```text
remaining budget
remaining physically available bytes
```

И результат:

```text
declared > budget remaining
    → BinaryLimitException

declared <= budget
but declared > actual available
    → BinaryFormatException
```

Для non-seekable source физическую доступность узнать нельзя — там остаётся budget pre-flight, потом bounded incremental read.

Это нужно исправить **в самом design**, до реализации.

---

### 2. `BudgetScope` сейчас объединяет две разные концепции

Предложено:

```text
Leaf
Structural
SelfManaged
```

и одновременно через `Scope == Structural` предлагается решать:

```text
EnterDepth()
ConsumeObjectGraphNodes()
reference tracking
```

 

Вот здесь я вижу архитектурный smell.

Например:

```text
StringBuilder
Uri
ImmutableArray
ValueTuple
Memory<T>
```

могут быть «structural» для одного смысла, но это не означает автоматически:

```text
reference-trackable
object-graph node
```

Особенно `ValueTuple` и другие value types.

Поэтому я бы **не позволял одному enum одновременно определять depth + node accounting + reference semantics**.

Минимум нужно концептуально разделить:

```text
Depth participation
Graph-node participation
Reference tracking
```

Иначе через год появится новый formatter и разработчик будет смотреть на `Scope` и гадать сразу о трёх механизмах.

---

### 3. S02 — предложенное AAD-решение не полностью закрывает custom encryption

Здесь идея правильная:

> header должен стать AEAD associated data. 

Но предложено:

```csharp
bool AuthenticatesAssociatedData => false;

Encrypt(... associatedData ...)
    => Encrypt(... без AAD ...)

Decrypt(... associatedData ...)
    => Decrypt(... без AAD ...)
```

То есть существующий custom algorithm может формально поддержать новый интерфейс, но фактически **игнорировать AAD**.

Тогда:

```text
custom encryption
+
AuthenticatesAssociatedData = false
+
RequireEncryption = false
```

по-прежнему допускает неаутентифицированный header.

Сам design это частично замечает, но называет проверку `RequireEncryption` optional. 

Если задача именно:

> **S02 считается закрытым**

то контракт должен однозначно решить, что происходит с encrypted V1 при:

```text
AuthenticatesAssociatedData == false
```

Я бы не оставлял здесь режим «иногда header authenticated, иногда нет» под одним и тем же V1 contract.

---

### 4. S09 нельзя считать решённым только через Deflate

Design говорит:

```text
Brotli unaffected:
TryDecompress returns false when destination is too small
```



Но security contract должен быть на уровне:

```text
ICompressor / Compressor
```

а не на уровне предположения о поведении конкретного implementation.

Нужна общая гарантия:

```text
decompressed output
    == declared uncompressed length
```

и желательно также чётко определить:

```text
all compressed input consumed
```

Иначе через некоторое время custom compressor снова создаст второй вариант S09.

---

### 5. S10 имеет одну важную неоднозначность с V0

Design говорит:

> V0 deliberately excluded, потому что headerless raw payload может быть embedded in a larger stream. 

Это разумно **для Stream API**.

Но у нас есть:

```csharp
Deserialize<T>(byte[] bytes)
```

Там граница input известна заранее.

Нужно явно определить:

```text
V0 + Stream
    → trailing bytes allowed?

V0 + byte[]
    → trailing bytes allowed?
```

Иначе один и тот же V0 payload получит разную семантику только из-за entry point.

Это не обязательно ошибка, но **такую вещь нельзя оставлять подразумеваемой**.

---

### 6. C03 — решение хорошее, но это сознательная потеря части `PreserveReferences`

Предлагаемая scope-stack модель:

```text
ancestor references
    → preserved

same-field sibling references
    → preserved

cross-field sibling references
    → duplicated
```



Это реально решает конфликт:

```text
unknown keyed field
+
global reference table
```

и делает schema evolution предсказуемой.

Но это **не полное PreserveReferences**.

Поэтому в System Contract нужно написать буквально:

> `PreserveReferences` сохраняет identity только внутри допустимой reference scope; object sharing across sibling keyed fields is not preserved and is serialized again.

Иначе через год тестировщик увидит:

```csharp
Assert.Same(result.A, result.B)
```

и закономерно спросит, почему контракт его нарушает.

---

### 7. S12-B — теперь я согласен с формулировкой «real gap»

Вот это в приложенном design сформулировано хорошо:

```text
MaxKeyedFields
    = per-object structural limit

MaxTotalKeyedFields
    = cumulative operation limit
```



Это как раз правильное продолжение нашего предыдущего обсуждения.

И важно, что там **не предлагают** начать списывать keyed fields из `MaxTotalElements`.

Это правильно.

---

# Что в design я считаю уже правильным

`S03` действительно надо решать на уровне общего recursive path, а не добавлением специальных проверок в `ListFormatter`, `DictionaryFormatter` и т.д. — иначе снова появятся пропуски. 

`S11` через `MaterializeBounded` тоже правильный общий механизм: сначала дешёвый `ICollection.Count`, для lazy enumerable — остановка на `max + 1`, а существующий `Validate...ForWrite` остаётся для element budget. Особенно важно, что в design прямо отмечено **«do not consume the budget twice»**. 

`C01/A02` через:

```csharp
existingInstance = (T)ReadValue(typeof(T))!;
```

— очень хорошее решение, потому что оно убирает отдельную несовместимую реализацию `ref struct` и заставляет reader использовать тот же framing, что writer. 

`C04/A03` тоже правильно чинить **на write side**, а не пытаться потом распознавать повреждённый positional payload на read side. 

---

# Мой итог

Я бы оценил этот design так:

```text
Концепция             ✅ хорошая
Группировка проблем   ✅ правильная
Security direction    ✅ правильная
Reference model       ✅ существенно лучше
Limits model          ✅ существенно лучше

Но перед кодированием:
S06                    ❗ надо уточнить physical-vs-budget availability
BudgetScope            ❗ надо развести semantics
S02/custom crypto      ❗ надо сделать header-authentication contract однозначным
S09                    ❗ exactness должна быть compressor-level invariant
S10/V0                 ❗ определить Stream vs byte[] semantics
C03                    ❗ явно зафиксировать частичную reference-preservation semantics
```

И ещё очень важно: **этот design не должен автоматически превращаться в изменение `src` сейчас**. Сам файл правильно помечает эти исправления как отдельное remediation work и ещё не утверждает, что они реализованы. 

Я бы именно этот документ сначала довёл до **архитектурно однозначного состояния**, и только после этого кодировал. Сейчас он уже близок, но S06 и разделение `BudgetScope` я бы обязательно исправил до начала реализации.
