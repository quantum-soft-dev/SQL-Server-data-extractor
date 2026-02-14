# APIs: Downstream Ingestion Service (Draft)

Документ описывает HTTP API контракт между экстрактором и downstream-сервисом, который сохраняет данные на S3 (CSV) и публикует schema manifest.

Статус: черновик. Все поля `TBD` нужно согласовать.

## 1) Термины
- **Batch**: один прогон экстрактора (по расписанию или вручную). Внутри batch обрабатывается набор таблиц.
- **Dataset**: единица выгрузки для одной таблицы за один диапазон изменений (например `(from_lsn,to_lsn)`), публикуемая атомарно.
- **Chunk**: порция строк, отправляемая отдельно.
- **Commit**: финализация dataset; после commit downstream “публикует” файлы/манифест.

## 2) Требования
- Поддержка chunk upload по **MaxBytes** (а не по фиксированному числу строк).
- Batch lifecycle:
  - downstream создает batch (возвращает `batch_id`);
  - extractor “закрывает” batch с итоговым статусом (success/fail/abort);
  - downstream хранит ошибки/события, привязанные к batch.
- Batch status (MVP): `RUNNING|SUCCEEDED|FAILED|ABORTED` (без `PARTIAL`).
- Batch type (MVP): `SNAPSHOT|DELTA`.
  - `SNAPSHOT`: полный snapshot **всех выбранных таблиц**.
  - `DELTA`: только изменения через CDC для всех выбранных таблиц.
- Атомарность прогресса (MVP):
  - Атомарность на уровне **dataset** (таблица + диапазон LSN или snapshot): либо dataset целиком опубликован (`commit`), либо не считается загруженным.
  - Экстрактор продвигает `last_processed_lsn` (per-table) только после успешного `commit` соответствующего dataset.
- Идемпотентность:
  - `chunk` должен быть идемпотентным по ключу (TBD).
  - `commit` должен быть идемпотентным по ключу dataset (TBD).
- Атомарность публикации:
  - файлы чанков не считаются “готовыми” до commit.
  - после commit downstream публикует manifest, по которому потребитель читает все чанки.

## 3) Аутентификация и безопасность (TBD)
- Auth scheme (MVP): OAuth 2.0 **Device Authorization Grant** + access/refresh tokens.
- Transport: HTTPS
- Replay protection / request signing: `TBD`
- RBAC/Scopes: `TBD`

## 4) Эндпоинты (TBD)
Минимальный набор (вариант):
### Batch
- `POST /v1/batches` (create/start)
- `POST /v1/batches/{batch_id}:finish` (finish: succeeded/failed/aborted)
- `POST /v1/batches/{batch_id}/errors` (report error/event tied to batch or table)
- `GET /v1/batches/{batch_id}` (status/details for UI/ops)

### Dataset (per table, tied to batch)
- `POST /v1/datasets` (create; includes `batch_id`)
- `POST /v1/datasets/{dataset_id}/chunks` (upload)
- `POST /v1/datasets/{dataset_id}:commit` (commit)
- `POST /v1/datasets/{dataset_id}:abort` (abort)
- `GET /v1/datasets/{dataset_id}` (status)

Альтернатива: адресовать dataset по натуральному ключу `(table, from_lsn, to_lsn)` и не вводить отдельный `dataset_id`.

## 5) Модель данных (TBD)
### Batch
- `batch_id`: string (uuid)
- `source`: object (идентификаторы источника, см. ниже)
- `trigger`: `SCHEDULED|MANUAL`
- `type`: `SNAPSHOT|DELTA`
- `started_at`: ISO 8601
- `finished_at`: ISO 8601 (nullable)
- `status`: `RUNNING|SUCCEEDED|FAILED|ABORTED`
- `summary`: object (опционально: счетчики таблиц/строк/байт/ошибок)

### Конкуренция batch и fencing (Draft, needs finalization)
Требование:
1) Допускаем до 2 активных batch одновременно (чтобы новый прогон мог стартовать, пока старый “зависший” не закрыт фоновым механизмом).
2) Если создан новый batch, предыдущий batch должен быть “отсечен” (superseded): он не может продолжать загрузку/commit.

Возможный механизм (рекомендовано):
- сервер на `POST /v1/batches` возвращает `batch_id` + `lease_token` (aka fence token);
- клиент прикладывает `lease_token` ко всем запросам batch в заголовке (например `X-Batch-Lease`);
- если lease устарел (создан более новый batch), сервер отвечает `409 Conflict` (или `412 Precondition Failed`).

### TTL неактивности batch (MVP)
Если в течение 10 минут нет активности, сервер считает batch неактивным и закрывает его как `ABORTED`.
Активность: любые запросы, привязанные к batch (datasets/chunks/commit/errors/finish).
Дополнительно (MVP): heartbeat endpoint, чтобы не зависеть от “тишины” между запросами:
- `POST /v1/batches/{batch_id}:heartbeat`
Клиентский интервал heartbeat (MVP): каждые 120 секунд, пока batch в `RUNNING`.

#### Source (пример)
- `sql_server`: hostname/instance (TBD)
- `database`: name

### Dataset identity
- `batch_id`: string (uuid)
- `table`: `schema.table`
- `from_lsn`: binary/hex string (nullable для snapshot-only?)
- `to_lsn`: binary/hex string (nullable для snapshot-only?)
- `run_id`: optional GUID (для трассировки)

### Chunk
- `chunk_no`: int
- `content_encoding`: `gzip` (MVP)
- `content_type`: `text/csv`
- `max_bytes`: enforced limit (TBD)
Примечания (MVP):
- `chunk_no` обязателен (0..N-1), чанки могут загружаться параллельно, порядок прихода не важен;
- downstream собирает итоговый файл детерминированно по `chunk_no` (0,1,2,...);
- checksum (MD5/SHA256) не требуется.

### Manifest
- Содержит список чанков и их S3 locations (или opaque references), schema manifest location, и метаданные commit.

### Error/Event reporting
События, связанные с выгрузкой, которые downstream должен хранить для UI и поддержки.
Минимальные поля:
- `occurred_at`: ISO 8601
- `scope`: `BATCH|TABLE|DATASET`
- `table`: nullable (`schema.table`)
- `dataset_id`: nullable
- `severity`: `INFO|WARN|ERROR|FATAL`
- `code`: string (стабильный код ошибки, например `SQL_CONNECTION_FAILED`, `TABLE_NOT_FOUND`, `CDC_NOT_ENABLED`, `PERMISSION_DENIED`)
- `message`: string (человекочитаемо)
- `details`: object (опционально; структурированные детали, без секретов)
- `is_retryable`: bool
- `is_terminal`: bool (если ошибка завершает batch или таблицу)

## 6) CSV формат (TBD)
- Кодировка: UTF-8 (TBD)
- Разделитель: `,` (TBD)
- Quote/escape: RFC4180-ish (TBD)
- Служебные колонки (MVP):
  - `_op`: `I|U|D` (+ как маркировать initial snapshot: `TBD`)
  - `_lsn`: `TBD` (hex string)
  - `_seqval`: `TBD`
  - `_ts`: `TBD` (ISO 8601)

## 7) Schema manifest (JSON)
Отдельный файл на таблицу/версию схемы. Поля (MVP):
- `table`
- `captured_at`
- `schema_hash`
- `columns[]`: `name`, `sql_type`, `nullable`, `length|precision|scale`
- `primary_key[]` и/или `unique_keys[]`
- `watermark_column?`

### Schema resource (MVP)
Рекомендованный способ доставки схемы: отдельный ресурс, идемпотентный по `schema_hash`.
Вариант эндпоинтов:
- `PUT /v1/tables/{table}/schemas/{schema_hash}` (upsert schema manifest)
- `GET /v1/tables/{table}/schemas/{schema_hash}`
Dataset должен ссылаться на `schema_hash`. Экстрактор отправляет schema только:
1) при первичном snapshot;
2) при обнаружении изменения схемы (например ALTER TABLE) с новым `schema_hash`.

## 8) Ошибки и ретраи (TBD)
- Ошибки валидации (400) vs transient (429/503) vs auth (401/403).
- Backoff/retry policy (TBD).
- Поведение при частичной загрузке: `abort` vs auto-expire datasets.

## 10) Batch flow (Draft)
Ожидаемый порядок вызовов:
1) `POST /v1/batches` -> получить `batch_id`.
2) Для каждой таблицы:
   - `POST /v1/datasets` (с `batch_id`, `table`, `from_lsn`, `to_lsn`...)
   - N раз `POST /v1/datasets/{dataset_id}/chunks`
   - `POST /v1/datasets/{dataset_id}:commit`
3) При ошибках по таблице или глобальных ошибках:
   - `POST /v1/batches/{batch_id}/errors` (с нужным `scope` и метаданными)
4) В конце:
   - `POST /v1/batches/{batch_id}:finish` со статусом `SUCCEEDED|FAILED|ABORTED`.

## 11) Open questions
1) Точная форма OAuth Device Flow и эндпоинты (device code, token, refresh) + срок жизни refresh + rotation.
2) Идемпотентность: ключи для `POST /v1/datasets`, `POST /chunks`, `:commit` (заголовок Idempotency-Key или натуральные ключи?).
3) Требуется ли строгая проверка наличия всех чанков на commit (например `expected_chunk_count`) и формат commit manifest.

## 9) Ограничения (TBD)
- Max request size (bytes)
- Max dataset size
- Max chunks per dataset
- Time-to-live для незакоммиченных datasets
