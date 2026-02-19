using System.IO.Pipes;
using CdcExtractor.Contracts.Ipc;
using StreamJsonRpc;

namespace CdcExtractor.App.Services;

public sealed class IpcClient : IDisposable
{
    private const string PipeName = "SQLExtractorIPC";
    private NamedPipeClientStream? _pipeClient;
    private JsonRpc? _jsonRpc;
    private IExtractorService? _proxy;

    public bool IsConnected => _pipeClient?.IsConnected ?? false;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipeClient.ConnectAsync(5000, ct).ConfigureAwait(false);
        _jsonRpc = JsonRpc.Attach(_pipeClient);
        _proxy = _jsonRpc.Attach<IExtractorService>();
    }

    public IExtractorService GetProxy() =>
        _proxy ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

    public void Disconnect()
    {
        _jsonRpc?.Dispose();
        _pipeClient?.Dispose();
        _jsonRpc = null;
        _pipeClient = null;
        _proxy = null;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
