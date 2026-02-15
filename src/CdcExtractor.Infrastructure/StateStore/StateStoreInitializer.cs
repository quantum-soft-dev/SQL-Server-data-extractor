using CdcExtractor.Infrastructure.SqlServer;
using Dapper;

namespace CdcExtractor.Infrastructure.StateStore;

/// <summary>
/// Idempotent DDL initializer that creates the four extractor metadata tables
/// when they do not already exist.
/// </summary>
public sealed class StateStoreInitializer
{
    private readonly SqlConnectionFactory _connectionFactory;

    private const string CreateTableStatesSql = """
        IF OBJECT_ID('dbo.__ExtractorTableStates', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.__ExtractorTableStates
            (
                TableSchema       nvarchar(128)    NOT NULL,
                TableName         nvarchar(128)    NOT NULL,
                ExtractionMode    nvarchar(10)     NOT NULL,
                LastProcessedLsn  varbinary(10)    NULL,
                BootstrapStatus   nvarchar(20)     NOT NULL,
                SchemaHash        nvarchar(64)     NULL,
                CaptureInstance   nvarchar(128)    NULL,
                LastSyncTime      datetimeoffset   NULL,
                ErrorMessage      nvarchar(2000)   NULL,
                UpdatedAt         datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                CONSTRAINT PK___ExtractorTableStates PRIMARY KEY (TableSchema, TableName)
            );
        END
        """;

    private const string CreateBatchHistorySql = """
        IF OBJECT_ID('dbo.__ExtractorBatchHistory', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.__ExtractorBatchHistory
            (
                BatchId       uniqueidentifier   NOT NULL,
                RemoteBatchId nvarchar(100)      NULL,
                BatchType     nvarchar(10)       NOT NULL,
                [Trigger]     nvarchar(10)       NOT NULL,
                Status        nvarchar(10)       NOT NULL,
                LeaseToken    nvarchar(200)      NULL,
                StartedAt     datetimeoffset     NOT NULL,
                FinishedAt    datetimeoffset     NULL,
                CONSTRAINT PK___ExtractorBatchHistory PRIMARY KEY (BatchId)
            );
        END
        """;

    private const string CreateDatasetHistorySql = """
        IF OBJECT_ID('dbo.__ExtractorDatasetHistory', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.__ExtractorDatasetHistory
            (
                DatasetId       uniqueidentifier   NOT NULL,
                BatchId         uniqueidentifier   NOT NULL,
                RemoteDatasetId nvarchar(100)      NULL,
                TableSchema     nvarchar(128)      NOT NULL,
                TableName       nvarchar(128)      NOT NULL,
                FromLsn         varbinary(10)      NULL,
                ToLsn           varbinary(10)      NULL,
                Status          nvarchar(10)       NOT NULL,
                RowCount        bigint             NOT NULL DEFAULT 0,
                ChunkCount      int                NOT NULL DEFAULT 0,
                BytesSent       bigint             NOT NULL DEFAULT 0,
                ErrorCode       nvarchar(50)       NULL,
                ErrorMessage    nvarchar(2000)     NULL,
                CONSTRAINT PK___ExtractorDatasetHistory PRIMARY KEY (DatasetId),
                CONSTRAINT FK___ExtractorDatasetHistory_Batch
                    FOREIGN KEY (BatchId) REFERENCES dbo.__ExtractorBatchHistory (BatchId)
            );
        END
        """;

    private const string CreateConfigSql = """
        IF OBJECT_ID('dbo.__ExtractorConfig', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.__ExtractorConfig
            (
                [Key]     nvarchar(255)    NOT NULL,
                Value     nvarchar(max)    NOT NULL,
                UpdatedAt datetimeoffset   NOT NULL,
                CONSTRAINT PK___ExtractorConfig PRIMARY KEY ([Key])
            );
        END
        """;

    public StateStoreInitializer(SqlConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Creates all four extractor metadata tables if they do not already exist.
    /// This method is idempotent.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        // Execute in order to satisfy FK dependencies
        await connection.ExecuteAsync(new CommandDefinition(CreateTableStatesSql, cancellationToken: ct)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(CreateBatchHistorySql, cancellationToken: ct)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(CreateDatasetHistorySql, cancellationToken: ct)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(CreateConfigSql, cancellationToken: ct)).ConfigureAwait(false);
    }
}
