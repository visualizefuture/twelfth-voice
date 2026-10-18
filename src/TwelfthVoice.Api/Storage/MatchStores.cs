using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Storage;

/// <summary>SQL Server / Azure SQL store. Writes go through the background queue.</summary>
public sealed class SqlMatchStore : IMatchStore
{
    private readonly PersistenceQueue _queue;
    private readonly IDbContextFactory<TwelfthVoiceDbContext> _factory;
    private readonly StorageState _state;
    private readonly InMemoryMatchStore _fallback = new();

    public SqlMatchStore(PersistenceQueue queue, IDbContextFactory<TwelfthVoiceDbContext> factory, StorageState state)
    {
        _queue = queue;
        _factory = factory;
        _state = state;
    }

    public string Mode => _state.Status switch
    {
        StorageStatus.Ready => "Sql",
        StorageStatus.Initializing => "Sql (starting)",
        _ => "Unavailable (memory only)"
    };

    private bool Writable => _state.Status is StorageStatus.Ready or StorageStatus.Initializing;
    private bool Readable => _state.Status == StorageStatus.Ready;

    public void SaveMatch(MatchInfo info, string status)
    {
        if (Writable) _queue.Enqueue(new SaveMatchItem(RecordMapper.ToRecord(info, status)));
    }

    public void AppendEvent(MatchEvent matchEvent)
    {
        if (Writable) _queue.Enqueue(new SaveEventItem(RecordMapper.ToRecord(matchEvent)));
    }

    public void AppendInsight(Insight insight)
    {
        if (Writable) _queue.Enqueue(new SaveInsightItem(RecordMapper.ToRecord(insight)));
    }

    public void UpdateMatchStatus(string matchId, string status, int homeScore, int awayScore, bool finished)
    {
        if (Writable) _queue.Enqueue(new UpdateMatchStatusItem(matchId, status, homeScore, awayScore, finished));
    }

    public async Task<IReadOnlyList<StoredMatch>> RecentMatchesAsync(int take, CancellationToken cancellationToken)
    {
        if (!Readable) return Array.Empty<StoredMatch>();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var records = await db.Matches.AsNoTracking()
            .OrderByDescending(m => m.CreatedUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        return records.Select(RecordMapper.ToStored).ToList();
    }

    public async Task<StoredProfile?> GetProfileAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!Readable) return await _fallback.GetProfileAsync(id, cancellationToken);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var record = await db.ViewerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return record is null ? null : RecordMapper.ToStored(record);
    }

    public async Task<StoredProfile> SaveProfileAsync(Guid? id, ViewerProfile profile, CancellationToken cancellationToken)
    {
        if (!Readable) return await _fallback.SaveProfileAsync(id, profile, cancellationToken);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var record = RecordMapper.ToRecord(id ?? Guid.NewGuid(), profile);
        var existing = await db.ViewerProfiles.FirstOrDefaultAsync(p => p.Id == record.Id, cancellationToken);
        if (existing is null)
        {
            db.ViewerProfiles.Add(record);
        }
        else
        {
            existing.DisplayName = record.DisplayName;
            existing.Mode = record.Mode;
            existing.FavoriteTeamId = record.FavoriteTeamId;
            existing.FocusPlayerId = record.FocusPlayerId;
            existing.Language = record.Language;
            existing.UpdatedUtc = record.UpdatedUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
        return RecordMapper.ToStored(existing ?? record);
    }
}

/// <summary>Used when storage is turned off: nothing is persisted, profiles live in memory.</summary>
public sealed class InMemoryMatchStore : IMatchStore
{
    private readonly ConcurrentDictionary<Guid, StoredProfile> _profiles = new();

    public string Mode => "Memory";

    public void SaveMatch(MatchInfo info, string status) { }
    public void AppendEvent(MatchEvent matchEvent) { }
    public void AppendInsight(Insight insight) { }
    public void UpdateMatchStatus(string matchId, string status, int homeScore, int awayScore, bool finished) { }

    public Task<IReadOnlyList<StoredMatch>> RecentMatchesAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredMatch>>(Array.Empty<StoredMatch>());

    public Task<StoredProfile?> GetProfileAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_profiles.TryGetValue(id, out var profile) ? profile : null);

    public Task<StoredProfile> SaveProfileAsync(Guid? id, ViewerProfile profile, CancellationToken cancellationToken)
    {
        var stored = new StoredProfile(id ?? Guid.NewGuid(), profile, DateTimeOffset.UtcNow);
        _profiles[stored.Id] = stored;
        return Task.FromResult(stored);
    }
}

public static class StorageSetup
{
    /// <summary>
    /// Registers SQL storage when a connection string is configured and Storage:Provider is
    /// "SqlServer"; otherwise registers the in-memory store.
    /// </summary>
    public static IServiceCollection AddTwelfthVoiceStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
        string? connectionString = configuration.GetConnectionString("TwelfthVoice");
        var state = new StorageState();

        services.AddSingleton(options);
        services.AddSingleton(state);

        bool useSql = string.Equals(options.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase)
                      && !string.IsNullOrWhiteSpace(connectionString);

        if (!useSql)
        {
            state.Status = StorageStatus.Disabled;
            services.AddSingleton<IMatchStore, InMemoryMatchStore>();
            return services;
        }

        services.AddDbContextFactory<TwelfthVoiceDbContext>(db =>
            db.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));
        services.AddSingleton<PersistenceQueue>();
        services.AddSingleton<IMatchStore, SqlMatchStore>();
        services.AddHostedService<PersistenceWorker>();
        return services;
    }
}
