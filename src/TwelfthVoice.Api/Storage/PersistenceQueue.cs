using System.Threading.Channels;

namespace TwelfthVoice.Api.Storage;

public abstract record PersistenceItem;
public sealed record SaveMatchItem(MatchRecord Match) : PersistenceItem;
public sealed record SaveEventItem(EventRecord Event) : PersistenceItem;
public sealed record SaveInsightItem(InsightRecord Insight) : PersistenceItem;
public sealed record UpdateMatchStatusItem(string MatchId, string Status, int HomeScore, int AwayScore, bool Finished) : PersistenceItem;

/// <summary>In-process queue between live matches and the database writer.</summary>
public sealed class PersistenceQueue
{
    private readonly Channel<PersistenceItem> _channel = Channel.CreateUnbounded<PersistenceItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ChannelReader<PersistenceItem> Reader => _channel.Reader;

    public void Enqueue(PersistenceItem item) => _channel.Writer.TryWrite(item);
}

public enum StorageStatus
{
    Initializing,
    Ready,
    Unavailable,
    Disabled
}

/// <summary>Shared flag: once the database proves unreachable, writes are skipped and the API keeps running in memory.</summary>
public sealed class StorageState
{
    private volatile StorageStatus _status = StorageStatus.Initializing;

    public StorageStatus Status
    {
        get => _status;
        set => _status = value;
    }

    public string? LastError { get; set; }
}

public sealed class StorageOptions
{
    /// <summary>"SqlServer" (LocalDB locally, Azure SQL in the cloud) or "None" (memory only).</summary>
    public string Provider { get; set; } = "SqlServer";

    /// <summary>Create the schema on startup if it doesn't exist. Handy for the hackathon; use migrations in production.</summary>
    public bool EnsureCreated { get; set; } = true;
}
