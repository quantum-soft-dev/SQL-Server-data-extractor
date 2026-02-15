<!--
Sync Impact Report
===================
Version change: 1.1.0 -> 1.2.0
Modified principles: none
Modified sections: none
Added sections:
  - Development Workflow & Quality Gates > Documentation & Library
    Reference: new subsection with Context7 MCP plugin guidance
Removed sections: none
Templates requiring updates:
  - .specify/templates/plan-template.md — ✅ no update needed
  - .specify/templates/spec-template.md — ✅ no update needed
  - .specify/templates/tasks-template.md — ✅ no update needed
  - .specify/templates/commands/*.md — N/A (directory empty)
  - .specify/templates/agent-file-template.md — ✅ no update needed
Follow-up TODOs: none
-->

# SQL Server CDC Data Extractor Constitution

## Core Principles

### I. Test-First / TDD (NON-NEGOTIABLE)

Development MUST follow the Test-Driven Development cycle:

1. **Red**: Write a failing test that defines the expected behavior.
2. **Green**: Write the minimum code to make the test pass.
3. **Refactor**: Improve code structure while keeping tests green.

Rules:
- No production code MUST be written without a corresponding test
  written first.
- Tests MUST fail before implementation begins (verify the "Red" step).
- Unit tests MUST be isolated: no database, no file system, no network.
  Use interfaces/mocks/fakes for external dependencies.
- Integration tests MUST cover: IPC contracts (Named Pipes/JSON-RPC),
  HTTP API client behavior, CDC interactions with SQL Server,
  state store (SQL Server) read/write, configuration loading.
- Test naming convention: `MethodUnderTest_Scenario_ExpectedResult`
  (e.g., `ReadCdcChanges_WhenGapDetected_ThrowsCdcGapException`).
- Test projects MUST mirror the source project structure
  (e.g., `src/CdcExtractor.Core` -> `tests/CdcExtractor.Core.Tests`).
- Code coverage is a guide, not a target. Focus on meaningful
  behavioral coverage, not percentage.

### II. Domain-Driven Design (DDD)

The codebase MUST use DDD tactical patterns where they add clarity:

- **Bounded Contexts**: Separate the domain into clear contexts:
  `Extraction` (CDC/snapshot logic), `Scheduling` (cron/triggers),
  `Sink` (HTTP API client), `Configuration` (settings/state),
  `Ipc` (manager-service communication), `CdcManagement`
  (enable/verify/retention).
- **Entities & Value Objects**: Use Value Objects for immutable
  concepts (e.g., `Lsn`, `TableIdentifier`, `BatchId`, `DatasetId`,
  `SchemaHash`). Use Entities for objects with identity and lifecycle
  (e.g., `TableState`, `BatchRun`).
- **Aggregates**: Group related entities under aggregate roots
  (e.g., `BatchRun` owns `DatasetRun` instances).
- **Domain Events**: Use domain events for cross-context
  communication (e.g., `CdcGapDetected`, `SnapshotCompleted`,
  `SchemaChanged`).
- **Repository Pattern**: Abstract data access (state store, config)
  behind repository interfaces. Production implementations use
  SQL Server; tests use in-memory fakes.
- **Application Services**: Orchestrate use cases. Keep domain logic
  in domain objects, not in services.
- **Ubiquitous Language**: Use terms from the PRD consistently:
  `Batch`, `Dataset`, `Chunk`, `Snapshot`, `Delta`, `LSN`,
  `Retention`, `Capture Instance`, `Re-bootstrap`.

DDD MUST NOT be applied dogmatically. If a pattern adds complexity
without clarity (e.g., for simple CRUD or configuration), use a
simpler approach.

### III. Exception Handling (NON-NEGOTIABLE)

Every exception MUST be handled explicitly and logged. Silent
failures are forbidden.

Rules:
- **No empty catch blocks**. Every `catch` MUST either:
  (a) log the exception and re-throw, or
  (b) log the exception and handle it with a defined recovery
      strategy, or
  (c) wrap the exception in a domain-specific exception with context
      and throw.
- **No `catch (Exception)` without logging**. Generic catches MUST
  log the full exception (message + stack trace + inner exceptions).
- **Structured exception hierarchy**: Define domain exceptions
  (e.g., `CdcGapException`, `SinkUploadException`,
  `PrerequisiteCheckFailedException`) that carry diagnostic context
  (table name, LSN range, batch ID, etc.).
- **Fail-fast at boundaries**: Invalid configuration, missing
  prerequisites, and permission errors MUST be detected at startup
  or job-start and reported immediately, not mid-run.
- **Global unhandled exception handler**: The Windows Service host
  and the WPF App MUST register global exception handlers
  (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`,
  `DispatcherUnhandledException`) that log and gracefully shut down.
- **async/await**: All `async` methods MUST propagate exceptions
  correctly. Never use `async void` except for event handlers, and
  those MUST have try/catch.
- **CancellationToken**: All long-running and I/O operations MUST
  accept and respect `CancellationToken` for graceful shutdown.

### IV. Observability & Logging (NON-NEGOTIABLE)

Log messages MUST be readable and informative without requiring
knowledge of the source code. A support engineer or operator MUST
be able to understand what happened from logs alone.

Rules:
- **Structured logging**: Use `Microsoft.Extensions.Logging` with
  structured log providers (e.g., Serilog). Log entries MUST be
  JSON-serializable with named properties.
- **Correlation**: Every batch run MUST have a `CorrelationId`.
  Every log entry within a run MUST include `CorrelationId`,
  `BatchId`, and where applicable `TableName`, `DatasetId`.
- **Log levels** MUST be used correctly:
  - `Trace`: Internal diagnostic detail (CDC row-level processing).
  - `Debug`: Developer-useful detail (SQL queries, HTTP requests).
  - `Information`: Business events (batch started, table committed,
    schema uploaded). Default production level.
  - `Warning`: Recoverable issues (retry triggered, retention
    raised, slow query).
  - `Error`: Failures requiring attention (table skipped, HTTP 5xx,
    permission denied).
  - `Critical`: Service cannot continue (state corruption, global
    unhandled exception).
- **Message format**: Log messages MUST follow the pattern:
  `"[Action] [Subject] [Context]. [Outcome/Reason]"`.
  Example: `"Uploading chunk 3/9 for table dbo.Orders
  (dataset {DatasetId}). Size: {Bytes} bytes"`.
  Anti-pattern: `"Error occurred"`, `"Something went wrong"`,
  `"Exception in method X"`.
- **Sensitive data**: Connection strings, tokens, passwords MUST
  NEVER appear in logs. Mask or redact.
- **Metrics**: Expose counters/gauges for: batch duration, rows
  extracted, rows uploaded, CDC lag (LSN delta), retry count,
  error count per table.
- **Windows Event Log**: Critical and Error events MUST be written
  to Windows Event Log in addition to file logs, so that system
  administrators can detect issues via standard monitoring.

### V. .NET Best Practices

The codebase MUST follow modern .NET conventions and idioms:

- **Target framework**: .NET 8+ (LTS). Use the latest stable LTS
  release available at the time of development.
- **Dependency Injection**: Use `Microsoft.Extensions.DependencyInjection`
  throughout. Register services with appropriate lifetimes
  (`Singleton`, `Scoped`, `Transient`). Avoid service locator
  anti-pattern.
- **Configuration**: Use `Microsoft.Extensions.Configuration`
  (JSON/YAML + environment variables + secrets). Bind to strongly
  typed `IOptions<T>` / `IOptionsMonitor<T>` objects with
  validation via `DataAnnotations` or `IValidateOptions<T>`.
- **async/await**: All I/O-bound operations MUST be async.
  Never use `.Result` or `.Wait()` on tasks (deadlock risk).
  Use `ConfigureAwait(false)` in library code.
- **Nullable reference types**: Enable `<Nullable>enable</Nullable>`
  project-wide. Treat warnings as errors for nullable.
- **Code style**: Follow `.editorconfig` rules. Use `dotnet format`
  for enforcement. Naming: PascalCase for public members, `_camelCase`
  for private fields, `I` prefix for interfaces.
- **Project structure**: Separate concerns into projects:
  - `CdcExtractor.Domain` — domain models, interfaces, events.
  - `CdcExtractor.Application` — use cases, application services.
  - `CdcExtractor.Infrastructure` — SQL Server access, HTTP client,
    state store, file I/O.
  - `CdcExtractor.Service` — Windows Service host, scheduling, IPC.
  - `CdcExtractor.App` — WPF application (configurator + manager).
  - `CdcExtractor.Contracts` — shared DTOs, IPC contracts.
- **Disposable resources**: All `IDisposable`/`IAsyncDisposable`
  resources MUST be disposed deterministically (`using` statements
  or DI container lifetime management).
- **Immutability**: Prefer `record` types for DTOs and Value Objects.
  Use `init`-only properties where mutation is not required.
- **Collections**: Expose `IReadOnlyList<T>`,
  `IReadOnlyCollection<T>` from public APIs. Use
  `ImmutableArray<T>` for truly immutable collections where
  performance matters.

### VI. Reliability & Data Integrity

The system MUST guarantee "at-least-once" delivery and protect
against data loss:

- **LSN advancement**: `last_processed_lsn` MUST be updated ONLY
  after successful `commit` of the corresponding dataset to
  downstream. Never optimistically advance.
- **Idempotency**: All HTTP calls to downstream (chunk upload,
  commit) MUST be idempotent. Use deterministic keys:
  `(table, from_lsn, to_lsn, chunk_no)`.
- **CDC gap detection**: If `last_processed_lsn` is less than the
  minimum available LSN, the system MUST NOT silently skip data.
  It MUST log an Error, flag the table for re-bootstrap, and notify
  the user via UI.
- **Transient fault handling**: Use retry with exponential backoff
  for network errors, SQL deadlocks, HTTP 429/503. Classify errors
  as transient vs terminal. Use Polly or equivalent.
- **Graceful shutdown**: On service stop or cancellation, the
  current batch MUST be completed or cleanly aborted (not left in
  an indeterminate state).
- **State store durability**: SQL Server state store MUST use
  explicit transactions for LSN updates. State tables MUST reside
  in the source database (or a dedicated state database on the
  same instance). Use `READ COMMITTED` isolation or higher for
  state read/write operations.

### VII. Security by Default

Security MUST be built in, not bolted on:

- **Secrets management**: Connection strings and tokens MUST NOT be
  stored in plain text config files. Use Windows Credential Manager
  (DPAPI) for local secrets, environment variables for CI.
- **SQL Server access**: Use the principle of least privilege.
  Document exact permissions required per operation.
- **Transport**: SQL Server connections MUST use `Encrypt=True`.
  Downstream HTTP MUST use HTTPS exclusively.
- **Named Pipes ACL**: IPC pipes MUST restrict access to authorized
  Windows accounts/groups only.
- **Input validation**: All external input (config values, SQL
  identifiers from metadata) MUST be validated. Table/schema names
  MUST be quoted with `[brackets]` to prevent SQL injection.
  Parameterize all queries.
- **No secrets in logs**: Enforce via structured logging redaction
  (see Principle IV).

### VIII. Simplicity (YAGNI)

Complexity MUST be justified. Start with the simplest solution that
meets current requirements:

- Do NOT add features, abstractions, or configuration options that
  are not required by the current phase (see `PHASES.md`).
- Prefer inline code over premature abstraction. Extract only when
  duplication exceeds three occurrences or when testability demands
  it.
- Prefer composition over inheritance.
- Every new project/assembly MUST have a clear, distinct
  responsibility. Do NOT create projects for organizational
  convenience alone.
- Configuration options MUST have sensible defaults. The user MUST
  be able to get started with minimal config (connection string +
  table selection).

## Technology Stack & Constraints

- **Runtime**: .NET 8+ (LTS), C# 12+.
- **OS**: Windows 10/11, Windows Server 2016+.
- **SQL Server**: 2016+ with CDC support (Enterprise, Standard,
  or Developer edition).
- **UI**: WPF (.NET 8+) for the Windows application.
- **Service host**: `Microsoft.Extensions.Hosting` with
  `BackgroundService` registered as a Windows Service via
  `Microsoft.Extensions.Hosting.WindowsServices`.
- **IPC**: Windows Named Pipes with StreamJsonRpc (Microsoft).
- **HTTP client**: `HttpClient` via `IHttpClientFactory` with
  Polly for resilience policies.
- **State store**: SQL Server (same instance as source data or
  dedicated). Access via `Microsoft.Data.SqlClient` + Dapper or
  Entity Framework Core (choose one per project convention).
- **Logging**: `Microsoft.Extensions.Logging` + Serilog
  (file sink + Windows Event Log sink).
- **Testing**: xUnit + Moq (or NSubstitute) + FluentAssertions.
  Integration tests use Testcontainers for SQL Server where
  feasible.
- **Build**: `dotnet build` / `dotnet test` / `dotnet publish`.
  MSBuild for packaging.
- **Code quality**: `.editorconfig`, `dotnet format`,
  nullable reference types enabled, warnings-as-errors in CI.
- **Secrets**: DPAPI (Windows Credential Manager) for local
  token/credential storage.

## Development Workflow & Quality Gates

### Workflow

1. **Branch per feature/fix**: Create a branch from `main`.
   Naming: `<type>/<short-description>`
   (e.g., `feature/cdc-gap-detection`, `fix/retry-deadlock`).
2. **TDD cycle**: For every change:
   - Write/update tests first (Red).
   - Implement (Green).
   - Refactor.
   - Commit with passing tests.
3. **Small, focused commits**: Each commit MUST compile and pass
   all tests. Commit message format:
   `<type>(<scope>): <description>` (Conventional Commits).
4. **Pull request**: All changes MUST go through PR with review.
   PR description MUST reference the spec/task.

### Documentation & Library Reference

When implementing features that use external libraries or frameworks,
the AI coding assistant MUST use the **Context7 MCP plugin** to
retrieve up-to-date documentation and code examples. This ensures
implementations follow current API conventions rather than relying
on potentially outdated training data.

Rules:
- **Before using an unfamiliar API**: The assistant MUST query
  Context7 (`resolve-library-id` then `query-docs`) to obtain
  current documentation for the target library.
- **When debugging library-related issues**: The assistant SHOULD
  query Context7 to verify correct API usage against the latest
  docs before proposing fixes.
- **Primary project dependencies** covered by this guidance:
  StreamJsonRpc, Dapper, Serilog, Polly, xUnit,
  FluentAssertions, Moq/NSubstitute, Testcontainers,
  Microsoft.Extensions.* (DI, Configuration, Hosting, Logging).
- **When Context7 has no results**: Fall back to official
  documentation websites. Document the lookup attempt so future
  sessions know the library is not indexed.
- **Do NOT query Context7** for .NET BCL (Base Class Library) or
  C# language features — these are well-known and stable.

### Quality Gates (all MUST pass before merge)

1. **Build**: `dotnet build` succeeds with zero warnings
   (warnings-as-errors).
2. **Tests**: All unit and integration tests pass.
3. **Code format**: `dotnet format --verify-no-changes` passes.
4. **No TODOs without tracking**: Any `TODO` in code MUST
   reference a task/issue ID.
5. **Constitution compliance**: Reviewer MUST verify:
   - No silent exceptions (Principle III).
   - Log messages are informative (Principle IV).
   - New code follows DDD where applicable (Principle II).
   - No unnecessary complexity (Principle VIII).

## Governance

This constitution is the authoritative source for development
standards in the SQL Server CDC Data Extractor project. It
supersedes informal practices and ad-hoc conventions.

### Amendment Procedure

1. Propose a change via PR modifying this file.
2. Describe the rationale and impact in the PR description.
3. Update `CONSTITUTION_VERSION` following semantic versioning:
   - **MAJOR**: Principle removed, redefined, or made incompatible.
   - **MINOR**: New principle/section added or materially expanded.
   - **PATCH**: Clarifications, wording fixes, non-semantic changes.
4. Update `LAST_AMENDED_DATE` to the merge date.
5. Propagate changes to dependent templates if affected.

### Compliance

- All PRs and code reviews MUST verify compliance with this
  constitution.
- Deviations MUST be documented and justified in the PR with a
  reference to the specific principle being deviated from and the
  reason.
- Runtime development guidance (prompts, agent instructions) MUST
  reference this constitution for principle enforcement.

**Version**: 1.2.0 | **Ratified**: 2026-02-15 | **Last Amended**: 2026-02-15
