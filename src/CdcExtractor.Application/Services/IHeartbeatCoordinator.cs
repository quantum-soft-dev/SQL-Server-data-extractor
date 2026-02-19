namespace CdcExtractor.Application.Services;

/// <summary>
/// Coordinates heartbeat signals during batch execution to prevent
/// downstream lease TTL expiration. Implemented by HeartbeatWorker in Service layer.
/// </summary>
public interface IHeartbeatCoordinator
{
    void StartHeartbeat(string batchId, string leaseToken);
    void StopHeartbeat();
}
