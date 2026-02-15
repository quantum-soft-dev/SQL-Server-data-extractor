using CdcExtractor.Contracts.Config;
using Microsoft.Data.SqlClient;

namespace CdcExtractor.Infrastructure.SqlServer;

/// <summary>
/// Creates and opens <see cref="SqlConnection"/> instances from a pre-built connection string.
/// Use <see cref="FromConfig"/> to construct from <see cref="SqlServerConfig"/>.
/// </summary>
public sealed class SqlConnectionFactory
{
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
}
