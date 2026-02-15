using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace CdcExtractor.Service.Ipc;

/// <summary>
/// Hosts a Named Pipe server for JSON-RPC communication with the WPF management app.
/// Listens on pipe "SQLExtractorIPC" and dispatches calls to <see cref="ExtractorServiceRpc"/>.
/// </summary>
public sealed class IpcServer : BackgroundService
{
    private const string PipeName = "SQLExtractorIPC";
    private readonly ExtractorServiceRpc _rpc;
    private readonly ILogger<IpcServer> _logger;

    public IpcServer(ExtractorServiceRpc rpc, ILogger<IpcServer> logger)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(logger);

        _rpc = rpc;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IPC server starting on pipe '{PipeName}'", PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AcceptClientAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IPC server accept loop");
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("IPC server stopped");
    }

    private async Task AcceptClientAsync(CancellationToken ct)
    {
        var pipeServer = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            await pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("IPC client connected");

            // Handle client in background — don't block the accept loop
            _ = HandleClientAsync(pipeServer, ct);
        }
        catch
        {
            await pipeServer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken ct)
    {
        try
        {
            await using var _ = pipeServer;
            var jsonRpc = JsonRpc.Attach(pipeServer, _rpc);

            await jsonRpc.Completion.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC client disconnected with error");
        }
        finally
        {
            _logger.LogInformation("IPC client disconnected");
        }
    }
}
