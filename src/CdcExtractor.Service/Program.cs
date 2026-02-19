using System.Runtime.Versioning;
using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Infrastructure.Http;
using CdcExtractor.Infrastructure.SqlServer;
using CdcExtractor.Infrastructure.StateStore;
using CdcExtractor.Service.Ipc;
using CdcExtractor.Service.Logging;
using CdcExtractor.Service.Workers;
using Microsoft.Extensions.Options;
using Serilog;

[assembly: SupportedOSPlatform("windows")]

// Global unhandled exception handlers — log before process terminates
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is Exception ex)
    {
        Log.Fatal(ex, "Unhandled exception — process terminating: {IsTerminating}", e.IsTerminating);
    }
    else
    {
        Log.Fatal("Unhandled non-exception object: {Error}", e.ExceptionObject);
    }

    Log.CloseAndFlush();
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Unobserved task exception");
    e.SetObserved();
};

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Bind configuration
    builder.Services.Configure<AppConfig>(builder.Configuration);
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<IOptions<AppConfig>>().Value.SqlServer);
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<IOptions<AppConfig>>().Value.Downstream);
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<IOptions<AppConfig>>().Value.Schedule);
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<IOptions<AppConfig>>().Value.Cdc);
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<IOptions<AppConfig>>().Value.Extraction);

    // Infrastructure — SQL Server
    builder.Services.AddSingleton(sp =>
        SqlConnectionFactory.FromConfig(sp.GetRequiredService<SqlServerConfig>()));
    builder.Services.AddSingleton<ICdcManager, CdcManager>();
    builder.Services.AddSingleton<ICdcReader, CdcReader>();
    builder.Services.AddSingleton<ISchemaInspector, SchemaInspector>();
    builder.Services.AddSingleton<StateStoreInitializer>();
    builder.Services.AddSingleton<IStateStore, DapperStateStore>();
    builder.Services.AddSingleton<IBatchHistoryStore, DapperBatchHistoryStore>();

    // Infrastructure — HTTP
    var tokenFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SQLExtractor", "token.dat");
    builder.Services.AddSingleton(new DpapiTokenStore(tokenFilePath));
    builder.Services.AddSingleton<ITokenProvider>(sp => sp.GetRequiredService<DpapiTokenStore>());

    builder.Services.AddTransient<TokenRefreshHandler>();
    builder.Services.AddHttpClient<IDownstreamClient, DownstreamClient>((sp, client) =>
    {
        var config = sp.GetRequiredService<DownstreamConfig>();
        client.BaseAddress = new Uri(config.BaseUrl);
    })
    .AddHttpMessageHandler<TokenRefreshHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.UseJitter = true;
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
    });

    // Application services
    builder.Services.AddSingleton<ChunkingService>();
    builder.Services.AddSingleton<ISchemaService, SchemaService>();
    builder.Services.AddSingleton<ISnapshotService, SnapshotService>();
    builder.Services.AddSingleton<IDeltaService, DeltaService>();
    builder.Services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
    builder.Services.AddSingleton<CdcSetupService>();
    builder.Services.AddSingleton<ExtractionOrchestrator>();

    // Workers
    builder.Services.AddSingleton<HeartbeatWorker>();
    builder.Services.AddSingleton<IHeartbeatCoordinator>(sp => sp.GetRequiredService<HeartbeatWorker>());
    builder.Services.AddSingleton<SchedulerWorker>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<SchedulerWorker>());

    // IPC — create LogBroadcaster as a shared instance for both DI and Serilog sink
    var logBroadcaster = new LogBroadcaster();
    builder.Services.AddSingleton(logBroadcaster);
    builder.Services.AddSingleton<ExtractorServiceRpc>();
    builder.Services.AddHostedService<IpcServer>();

    // Logging
    builder.Services.AddSerilog(loggerConfig =>
    {
        loggerConfig
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "CdcExtractor")
            .Destructure.ByTransforming<SqlServerConfig>(c => new
            {
                c.Server, c.Instance, c.Database, c.AuthType, c.Encrypt,
                Username = c.Username is not null ? "***" : null,
                Password = c.Password is not null ? "***" : null,
            })
            .WriteTo.File(
                path: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SQLExtractor", "logs", "extractor-.log"),
                rollingInterval: Serilog.RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.EventLog(
                source: "SQL CDC Extractor",
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error)
            .WriteTo.Sink(new IpcLogSink(logBroadcaster));
    });

    var host = builder.Build();

    // Initialize state store tables on startup
    var initializer = host.Services.GetRequiredService<StateStoreInitializer>();
    await initializer.InitializeAsync().ConfigureAwait(false);

    // Console mode support
    var isConsole = args.Contains("--console");
    if (isConsole)
    {
        await host.RunAsync().ConfigureAwait(false);
    }
    else
    {
        host.Run();
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Service terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
