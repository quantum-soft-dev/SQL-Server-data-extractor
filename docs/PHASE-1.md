# Фаза 1: MVP End-to-End

## Overview
Собрать минимально работоспособную систему, которая на Windows Server устанавливается, настраивается через Windows App, запускает Windows Service по расписанию, делает **SNAPSHOT batch** (полная загрузка всех выбранных таблиц) и далее **DELTA batch** (CDC изменения), отправляя данные в downstream по HTTP API чанками и закрывая batch.

## Цели
1) End-to-end поток: SQL Server -> Extractor Service -> HTTP -> Downstream -> S3 (CSV) + schema manifest.
2) Самообслуживание клиента без DBA: проверки prerequisites, включение CDC, установка retention>=7 дней.
3) Управление через Windows App: выбор таблиц, ручной запуск, просмотр статусов и логов (через IPC).

## Scope (входит)
**Extractor / Service (.NET)**
1) Планировщик: 08:00 / 12:00 / 18:00 (конфигурируемо), запрет параллельных прогонов.
2) CDC Manager:
   - проверка SQL Agent;
   - включение CDC на БД и на таблицах;
   - настройка retention: минимум 7 дней (не понижать, если больше).
3) SNAPSHOT batch:
   - выгрузка всех выбранных таблиц (полные данные);
   - отправка CSV чанками (gzip, MaxBytes).
4) DELTA batch:
   - чтение CDC диапазонов (from_lsn/to_lsn), выгрузка событий I/U/D;
   - служебные колонки в CSV: `_op`, `_lsn`, `_seqval`, `_ts` (как именно — в API контракте).
5) State store (локально): хранение `last_processed_lsn` и статуса bootstrap.
6) CDC gap detection: если история очищена и дельта недоступна — перевод в re-bootstrap (полный snapshot со следующего SNAPSHOT batch) + явное сообщение.

**Windows App (installer + конфигуратор + менеджер)**
1) Подключение к SQL Server (SQL login или Windows/AD).
2) UI выбора таблиц:
   - список таблиц, поиск по паттерну, select all, чекбоксы;
   - отображение причин “нельзя подключить” (нет прав/нет SQL Agent/нет unique index и т.д.).
3) Просмотр статусов batch, прогресса по таблицам.
4) Ручной запуск batch (manual trigger).
5) Онлайн-логи из сервиса (stream) и последние N строк.

**IPC (Windows App <-> Service)**
1) Transport: Windows Named Pipes.
2) Protocol: JSON-RPC (StreamJsonRpc).
3) Стрим логов: subscription + notifications.

**Downstream API (минимальный контракт)**
1) Batch lifecycle: create, heartbeat (120 sec), finish, errors.
2) Dataset: create, chunks upload, commit/abort, status.
3) Schema resource: `PUT /tables/{table}/schemas/{schema_hash}` (шлем при snapshot/alter).
4) Конкуренция batch и “отсечение” старого batch (lease/fence token).

## Out of scope (не входит)
1) Частичный batch (`PARTIAL`) и сложные политики продолжения после table-level ошибок.
2) Replica-режим (применение апдейтов в актуальное состояние в downstream).
3) Сложная стратегия snapshot консистентности (фиксируем MVP алгоритм отдельно).
4) Автоматический upgrade/auto-update (кроме базовой установки).
5) Сильная валидация CSV/scheme на downstream (downstream “blind store”).

## Deliverables
1) Рабочий сервис и Windows App (установка, конфиг, запуск).
2) Документация: `PRD.md`, `APIs.md`, инструкции установки/настройки.
3) Демонстрационный прогон на тестовой SQL Server БД: snapshot -> delta.

## Definition of Done / Acceptance
1) На чистой машине Windows Server можно установить компонент(ы), настроить подключение и выбрать таблицы.
2) SNAPSHOT batch выгружает все выбранные таблицы и успешно закрывается.
3) Следующий DELTA batch выгружает только изменения по CDC и продвигает `last_processed_lsn` только после commit.
4) При ошибках prerequisites/permissions/table missing:
   - batch останавливается;
   - ошибка репортится в downstream и отображается в UI;
   - локальные логи понятны пользователю.
5) При старте нового batch старый “зависший” batch не может коммитить (fencing).

