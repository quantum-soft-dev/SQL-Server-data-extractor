using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace CdcExtractor.Service.Ipc;

/// <summary>
/// Hosts a Named Pipe server for JSON-RPC communication with the WPF management app.
/// Listens on pipe "SQLExtractorIPC" and dispatches calls to <see cref="ExtractorServiceRpc"/>.
/// Pipe access is restricted via ACL to the service account and the interactive user group.
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
        var pipeSecurity = CreatePipeSecurity();

        var pipeServer = NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);

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

    /// <summary>
    /// Creates a <see cref="PipeSecurity"/> ACL that restricts pipe access to:
    /// 1. The service account (current process identity) — full control
    /// 2. The Interactive Users group — read/write (allows the WPF app to connect)
    /// All other accounts are denied access.
    /// </summary>
    private PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();

        // Grant the service process identity full control
        var serviceIdentity = WindowsIdentity.GetCurrent().Owner
            ?? WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(
            serviceIdentity,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Grant interactive users (local logon sessions) read/write access
        // so the WPF management app can connect when run by an operator
        var interactiveUsers = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);
        security.AddAccessRule(new PipeAccessRule(
            interactiveUsers,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return security;
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
