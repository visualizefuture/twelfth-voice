using System.Text.Json;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Storage;

internal static class RecordMapper
{
    public static MatchRecord ToRecord(MatchInfo info, string status) => new()
    {
        MatchId = info.MatchId,
        HomeTeamId = info.Home.Id,
        AwayTeamId = info.Away.Id,
        Seed = info.Seed,
        Competition = info.Competition,
        Venue = info.Venue,
        KickOff = info.KickOff,
        Status = status,
        CreatedUtc = DateTimeOffset.UtcNow,
        InfoJson = JsonSerializer.Serialize(info, MatchJson.Compact)
    };

    public static EventRecord ToRecord(MatchEvent e) => new()
    {
        MatchId = e.MatchId,
        Sequence = e.Sequence,
        Type = e.Type.ToString(),
        Period = e.Period,
        MatchSecond = e.MatchSecond,
        TeamId = e.TeamId,
        PlayerId = e.PlayerId,
        RelatedPlayerId = e.RelatedPlayerId,
        Outcome = e.Outcome?.ToString(),
        StartX = e.Start?.X,
        StartY = e.Start?.Y,
        EndX = e.End?.X,
        EndY = e.End?.Y,
        DistanceM = e.DistanceM,
        BallSpeedKmh = e.BallSpeedKmh,
        PlayerSpeedKmh = e.PlayerSpeedKmh,
        Xg = e.Xg,
        PassDifficulty = e.PassDifficulty,
        Qualifiers = e.Qualifiers.Count == 0 ? null : Truncate(string.Join("|", e.Qualifiers), 256),
        PayloadJson = JsonSerializer.Serialize(e, MatchJson.Compact)
    };

    public static InsightRecord ToRecord(Insight i) => new()
    {
        InsightId = i.Id,
        MatchId = i.MatchId,
        Kind = i.Kind.ToString(),
        Period = i.Period,
        MatchSecond = i.MatchSecond,
        Importance = i.Importance,
        TeamId = i.TeamId,
        Headline = Truncate(i.Headline, 256),
        WhyItMatters = i.WhyItMatters,
        AnalystNote = i.AnalystNote,
        PayloadJson = JsonSerializer.Serialize(i, MatchJson.Compact)
    };

    public static ViewerProfileRecord ToRecord(Guid id, ViewerProfile p) => new()
    {
        Id = id,
        DisplayName = p.DisplayName,
        Mode = p.Mode.ToString(),
        FavoriteTeamId = p.FavoriteTeamId,
        FocusPlayerId = p.FocusPlayerId,
        Language = p.Language,
        UpdatedUtc = DateTimeOffset.UtcNow
    };

    public static StoredProfile ToStored(ViewerProfileRecord r) => new(
        r.Id,
        new ViewerProfile
        {
            DisplayName = r.DisplayName,
            Mode = Enum.TryParse<ViewerMode>(r.Mode, out var mode) ? mode : ViewerMode.Casual,
            FavoriteTeamId = r.FavoriteTeamId,
            FocusPlayerId = r.FocusPlayerId,
            Language = r.Language
        },
        r.UpdatedUtc);

    public static StoredMatch ToStored(MatchRecord m) => new(
        m.MatchId, m.HomeTeamId, m.AwayTeamId, m.Seed, m.Status, m.HomeScore, m.AwayScore, m.CreatedUtc, m.FinishedUtc);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
