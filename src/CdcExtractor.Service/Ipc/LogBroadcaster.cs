using System.Collections.Concurrent;
using CdcExtractor.Contracts.Ipc;
using Serilog.Events;

namespace CdcExtractor.Service.Ipc;

/// <summary>
/// Manages IPC client subscriptions for live log streaming.
/// Maintains a thread-safe collection of subscribers and a circular buffer of recent log entries.
/// Called by the IPC log sink on every log event to broadcast entries to connected clients.
/// </summary>
public sealed class LogBroadcaster
{
    private const int DefaultBufferCapacity = 500;

    private readonly ConcurrentDictionary<string, (LogEventLevel MinLevel, Action<LogEntryDto> Callback)> _subscribers = new();
    private readonly ConcurrentQueue<LogEntryDto> _recentLogs = new();
    private readonly int _bufferCapacity;

    public LogBroadcaster()
        : this(DefaultBufferCapacity)
    {
    }

    public LogBroadcaster(int bufferCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bufferCapacity, 0);
        _bufferCapacity = bufferCapacity;
    }

    /// <summary>
    /// Registers a subscriber to receive log entries at or above the specified minimum level.
    /// </summary>
    /// <param name="subscriberId">Unique identifier for the subscriber (typically an IPC session ID).</param>
    /// <param name="minLevel">Minimum log level filter as a string (e.g., "Information", "Warning").</param>
    /// <param name="callback">Callback invoked for each matching log entry.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="minLevel"/> cannot be parsed.</exception>
    public void Subscribe(string subscriberId, string minLevel, Action<LogEntryDto> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        ArgumentException.ThrowIfNullOrWhiteSpace(minLevel);
        ArgumentNullException.ThrowIfNull(callback);

        if (!Enum.TryParse<LogEventLevel>(minLevel, ignoreCase: true, out var level))
        {
            throw new ArgumentException($"Invalid log level: '{minLevel}'.", nameof(minLevel));
        }

        _subscribers[subscriberId] = (level, callback);
    }

    /// <summary>
    /// Removes a subscriber so it no longer receives log entries.
    /// </summary>
    /// <param name="subscriberId">The subscriber ID to remove.</param>
    /// <returns><c>true</c> if the subscriber was found and removed; otherwise <c>false</c>.</returns>
    public bool Unsubscribe(string subscriberId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        return _subscribers.TryRemove(subscriberId, out _);
    }

    /// <summary>
    /// Broadcasts a log entry to all subscribers whose minimum level is met,
    /// and appends the entry to the recent log buffer.
    /// Exceptions from individual subscriber callbacks are caught and swallowed
    /// so that one failing subscriber does not affect others.
    /// </summary>
    /// <param name="entry">The log entry to broadcast.</param>
    public void Broadcast(LogEntryDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        EnqueueToBuffer(entry);

        if (_subscribers.IsEmpty)
        {
            return;
        }

        if (!Enum.TryParse<LogEventLevel>(entry.Level, ignoreCase: true, out var entryLevel))
        {
            return;
        }

        foreach (var kvp in _subscribers)
        {
            var (minLevel, callback) = kvp.Value;

            if (entryLevel < minLevel)
            {
                continue;
            }

            try
            {
                callback(entry);
            }
            catch
            {
                // Swallow per-subscriber exceptions to protect other subscribers.
            }
        }
    }

    /// <summary>
    /// Returns the most recent log entries from the circular buffer,
    /// filtered to the specified minimum level.
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <param name="minLevel">Minimum log level filter as a string.</param>
    /// <returns>A list of matching log entries, most recent last.</returns>
    public IReadOnlyList<LogEntryDto> GetRecentLogs(int count, string minLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
        ArgumentException.ThrowIfNullOrWhiteSpace(minLevel);

        if (!Enum.TryParse<LogEventLevel>(minLevel, ignoreCase: true, out var level))
        {
            throw new ArgumentException($"Invalid log level: '{minLevel}'.", nameof(minLevel));
        }

        // Snapshot the queue and filter by level, then take the last N entries.
        var snapshot = _recentLogs.ToArray();

        var filtered = new List<LogEntryDto>();
        for (var i = snapshot.Length - 1; i >= 0 && filtered.Count < count; i--)
        {
            if (Enum.TryParse<LogEventLevel>(snapshot[i].Level, ignoreCase: true, out var itemLevel)
                && itemLevel >= level)
            {
                filtered.Add(snapshot[i]);
            }
        }

        // Reverse so entries are returned in chronological order (oldest first).
        filtered.Reverse();
        return filtered;
    }

    /// <summary>
    /// Returns the current number of active subscribers.
    /// </summary>
    public int SubscriberCount => _subscribers.Count;

    private void EnqueueToBuffer(LogEntryDto entry)
    {
        _recentLogs.Enqueue(entry);

        // Trim the queue to the configured capacity.
        while (_recentLogs.Count > _bufferCapacity)
        {
            _recentLogs.TryDequeue(out _);
        }
    }
}
