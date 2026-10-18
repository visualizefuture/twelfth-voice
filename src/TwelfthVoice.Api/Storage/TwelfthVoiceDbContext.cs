using Microsoft.EntityFrameworkCore;

namespace TwelfthVoice.Api.Storage;

public sealed class TwelfthVoiceDbContext : DbContext
{
    public TwelfthVoiceDbContext(DbContextOptions<TwelfthVoiceDbContext> options) : base(options)
    {
    }

    public DbSet<MatchRecord> Matches => Set<MatchRecord>();
    public DbSet<EventRecord> Events => Set<EventRecord>();
    public DbSet<InsightRecord> Insights => Set<InsightRecord>();
    public DbSet<ViewerProfileRecord> ViewerProfiles => Set<ViewerProfileRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchRecord>(e =>
        {
            e.ToTable("Matches");
            e.HasKey(x => x.MatchId);
            e.Property(x => x.MatchId).HasMaxLength(64);
            e.Property(x => x.HomeTeamId).HasMaxLength(8);
            e.Property(x => x.AwayTeamId).HasMaxLength(8);
            e.Property(x => x.Competition).HasMaxLength(128);
            e.Property(x => x.Venue).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => x.CreatedUtc);
        });

        modelBuilder.Entity<EventRecord>(e =>
        {
            e.ToTable("MatchEvents");
            e.HasKey(x => new { x.MatchId, x.Sequence });
            e.Property(x => x.MatchId).HasMaxLength(64);
            e.Property(x => x.Type).HasMaxLength(32);
            e.Property(x => x.TeamId).HasMaxLength(8);
            e.Property(x => x.PlayerId).HasMaxLength(16);
            e.Property(x => x.RelatedPlayerId).HasMaxLength(16);
            e.Property(x => x.Outcome).HasMaxLength(16);
            e.Property(x => x.Qualifiers).HasMaxLength(256);
            e.HasIndex(x => new { x.MatchId, x.Type });
            e.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<InsightRecord>(e =>
        {
            e.ToTable("Insights");
            e.HasKey(x => x.InsightId);
            e.Property(x => x.InsightId).HasMaxLength(96);
            e.Property(x => x.MatchId).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.TeamId).HasMaxLength(8);
            e.Property(x => x.Headline).HasMaxLength(256);
            e.HasIndex(x => new { x.MatchId, x.MatchSecond });
        });

        modelBuilder.Entity<ViewerProfileRecord>(e =>
        {
            e.ToTable("ViewerProfiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(64);
            e.Property(x => x.Mode).HasMaxLength(16);
            e.Property(x => x.FavoriteTeamId).HasMaxLength(8);
            e.Property(x => x.FocusPlayerId).HasMaxLength(16);
            e.Property(x => x.Language).HasMaxLength(16);
        });
    }
}
