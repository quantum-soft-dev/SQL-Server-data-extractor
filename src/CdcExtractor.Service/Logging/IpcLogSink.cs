using CdcExtractor.Contracts.Ipc;
using CdcExtractor.Service.Ipc;
using Serilog.Core;
using Serilog.Events;

namespace CdcExtractor.Service.Logging;

/// <summary>
/// Custom Serilog sink that forwards log events to the <see cref="LogBroadcaster"/>
/// for live streaming to connected IPC clients. Converts each <see cref="LogEvent"/>
/// into a <see cref="LogEntryDto"/> and broadcasts it. Any exceptions thrown during
/// emission are caught and swallowed — a sink must never throw.
/// </summary>
public sealed class IpcLogSink : ILogEventSink
{
    /// <summary>
    /// Property names that are extracted into dedicated <see cref="LogEntryDto"/> fields
    /// and therefore excluded from the general-purpose Properties dictionary.
    /// </summary>
    private static readonly HashSet<string> KnownPropertyNames = new(StringComparer.Ordinal)
    {
        nameof(LogEntryDto.CorrelationId),
        nameof(LogEntryDto.BatchId),
        nameof(LogEntryDto.Table),
        nameof(LogEntryDto.DatasetId),
    };

    private readonly LogBroadcaster _broadcaster;

    public IpcLogSink(LogBroadcaster broadcaster)
    {
        _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        try
        {
            var entry = new LogEntryDto(
                Timestamp: logEvent.Timestamp,
                Level: logEvent.Level.ToString(),
                Message: logEvent.RenderMessage(),
                CorrelationId: ExtractStringProperty(logEvent, nameof(LogEntryDto.CorrelationId)),
                BatchId: ExtractStringProperty(logEvent, nameof(LogEntryDto.BatchId)),
                Table: ExtractStringProperty(logEvent, nameof(LogEntryDto.Table)),
                DatasetId: ExtractStringProperty(logEvent, nameof(LogEntryDto.DatasetId)),
                Properties: ExtractRemainingProperties(logEvent));

            _broadcaster.Broadcast(entry);
        }
        catch
        {
            // A sink must never throw. Swallow all exceptions to protect the logging pipeline.
        }
    }

    private static string? ExtractStringProperty(LogEvent logEvent, string propertyName)
    {
        if (logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue scalar)
        {
            return scalar.Value?.ToString();
        }

        return null;
    }

    private static Dictionary<string, object?>? ExtractRemainingProperties(LogEvent logEvent)
    {
        Dictionary<string, object?>? properties = null;

        foreach (var kvp in logEvent.Properties)
        {
            if (KnownPropertyNames.Contains(kvp.Key))
            {
                continue;
            }

            properties ??= new Dictionary<string, object?>(StringComparer.Ordinal);

            // Unwrap ScalarValue to its inner value; leave complex values as their rendered string.
            if (kvp.Value is ScalarValue scalar)
            {
                properties[kvp.Key] = scalar.Value;
            }
            else
            {
                properties[kvp.Key] = kvp.Value.ToString();
            }
        }

        return properties;
    }
}
