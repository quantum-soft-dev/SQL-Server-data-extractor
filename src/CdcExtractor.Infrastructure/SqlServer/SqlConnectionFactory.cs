using CdcExtractor.Contracts.Config;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace CdcExtractor.Infrastructure.SqlServer;

/// <summary>
/// Creates and opens <see cref="SqlConnection"/> instances from a pre-built connection string.
/// Use <see cref="FromConfig"/> to construct from <see cref="SqlServerConfig"/>.
/// Provides a shared <see cref="SqlRetryPipeline"/> for transient fault handling.
/// </summary>
public sealed class SqlConnectionFactory
{
    /// <summary>
    /// SQL Server error numbers considered transient and safe to retry.
    /// </summary>
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        -2,     // Timeout expired
        233,    // Connection closed by server
        1205,   // Deadlock victim
        10053,  // Transport-level error (connection forcibly closed)
        10054,  // Transport-level error (connection reset by peer)
        10060,  // Network or instance-specific error
        40143,  // Connection could not be initialized
        40197,  // Service error processing request
        40501,  // Service is busy
        40613,  // Database not currently available
        49918,  // Cannot process request — not enough resources
        49919,  // Cannot process create/update request
        49920,  // Cannot process request — too many operations in progress
    ];

    /// <summary>
    /// Shared Polly resilience pipeline for retrying transient SQL Server faults.
    /// Use this to wrap Dapper calls: <c>await SqlRetryPipeline.ExecuteAsync(async ct => ..., ct)</c>.
    /// </summary>
    public static ResiliencePipeline SqlRetryPipeline { get; } = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<SqlException>(ex => IsTransient(ex))
                .Handle<TimeoutException>(),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        })
        .Build();

    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// Builds a <see cref="SqlConnectionFactory"/> from a <see cref="SqlServerConfig"/> record,
    /// choosing integrated or SQL Login authentication based on <see cref="SqlServerConfig.AuthType"/>.
    /// </summary>
    public static SqlConnectionFactory FromConfig(SqlServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrEmpty(config.Instance)
                ? config.Server
                : $"{config.Server}\\{config.Instance}",
            InitialCatalog = config.Database,
            Encrypt = config.Encrypt,
            TrustServerCertificate = false,
        };

        if (string.Equals(config.AuthType, "SqlLogin", StringComparison.OrdinalIgnoreCase))
        {
            builder.UserID = config.Username;
            builder.Password = config.Password;
            builder.IntegratedSecurity = false;
        }
        else
        {
            // WindowsAd or Windows auth
            builder.IntegratedSecurity = true;
        }

        return new SqlConnectionFactory(builder.ConnectionString);
    }

    /// <summary>
    /// Creates a new <see cref="SqlConnection"/> and opens it asynchronously.
    /// The caller owns the returned connection and must dispose it.
    /// </summary>
    public async Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates a new closed <see cref="SqlConnection"/>.
    /// The caller owns the returned connection and must dispose it.
    /// </summary>
    public SqlConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// Returns true if the <see cref="SqlException"/> is a transient fault
    /// that is safe to retry (deadlock, timeout, connection drop).
    /// </summary>
    public static bool IsTransient(SqlException ex) =>
        ex.Errors.Cast<SqlError>().Any(e => TransientErrorNumbers.Contains(e.Number));
}
