using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace CdcExtractor.Service.Logging;

/// <summary>
/// Configures Serilog as the logging provider for the service host.
/// Writes to a rolling daily log file and to the Windows Event Log (errors only).
/// </summary>
public static class SerilogSetup
{
    /// <summary>
    /// Registers Serilog on the <see cref="IHostBuilder"/> with file and Event Log sinks.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IHostBuilder UseSerilogLogging(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog((context, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "CdcExtractor")
                .WriteTo.File(
                    path: @"C:\ProgramData\SQLExtractor\logs\extractor-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .WriteTo.EventLog(
                    source: "SQL CDC Extractor",
                    restrictedToMinimumLevel: LogEventLevel.Error);
        });
    }
}
