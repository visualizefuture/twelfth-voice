using Microsoft.EntityFrameworkCore;

namespace TwelfthVoice.Api.Storage;

/// <summary>
/// Background writer: prepares the database on startup, then drains the queue in batches.
/// If the database can't be reached it logs once, marks storage unavailable and keeps
/// draining (discarding) so the live API carries on in memory.
/// </summary>
public sealed class PersistenceWorker : BackgroundService
{
    private const int BatchSize = 500;

    private readonly PersistenceQueue _queue;
    private readonly IDbContextFactory<TwelfthVoiceDbContext> _factory;
    private readonly StorageState _state;
    private readonly StorageOptions _options;
    private readonly ILogger<PersistenceWorker> _logger;

    public PersistenceWorker(
        PersistenceQueue queue,
        IDbContextFactory<TwelfthVoiceDbContext> factory,
        StorageState state,
        StorageOptions options,
        ILogger<PersistenceWorker> logger)
    {
        _queue = queue;
        _factory = factory;
        _state = state;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeAsync(stoppingToken);

        var batch = new List<PersistenceItem>(BatchSize);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                batch.Clear();
                while (batch.Count < BatchSize && _queue.Reader.TryRead(out var item)) batch.Add(item);

                if (_state.Status != StorageStatus.Ready) continue;

                try
                {
                    await WriteAsync(batch, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to write {Count} items to the database", batch.Count);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            if (_options.EnsureCreated)
                await db.Database.EnsureCreatedAsync(cancellationToken);
            else if (!await db.Database.CanConnectAsync(cancellationToken))
                throw new InvalidOperationException("Cannot connect to the database.");

            _state.Status = StorageStatus.Ready;
            _logger.LogInformation("Storage ready ({Provider})", db.Database.ProviderName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _state.Status = StorageStatus.Unavailable;
            _state.LastError = ex.Message;
            _logger.LogWarning(ex, "Database unavailable — Twelfth Voice will keep running in memory only.");
        }
    }

    private async Task WriteAsync(List<PersistenceItem> batch, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        var statusUpdates = new List<UpdateMatchStatusItem>();
        foreach (var item in batch)
        {
            switch (item)
            {
                case SaveMatchItem m:
                    db.Matches.Add(m.Match);
                    break;
                case SaveEventItem e:
                    db.Events.Add(e.Event);
                    break;
                case SaveInsightItem i:
                    db.Insights.Add(i.Insight);
                    break;
                case UpdateMatchStatusItem s:
                    statusUpdates.Add(s);
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (statusUpdates.Count == 0) return;

        foreach (var update in statusUpdates)
        {
            var match = await db.Matches.FindAsync(new object[] { update.MatchId }, cancellationToken);
            if (match is null) continue;
            match.Status = update.Status;
            match.HomeScore = update.HomeScore;
            match.AwayScore = update.AwayScore;
            if (update.Finished) match.FinishedUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
