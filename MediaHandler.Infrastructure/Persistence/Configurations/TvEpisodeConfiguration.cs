using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class TvEpisodeConfiguration : IEntityTypeConfiguration<TvEpisode>
{
    public void Configure(EntityTypeBuilder<TvEpisode> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EpisodeNumber)
            .IsRequired();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Overview)
            .HasMaxLength(int.MaxValue);

        builder.Property(e => e.StillPath)
            .HasMaxLength(500);

        builder.HasOne(e => e.Season)
            .WithMany(s => s.TvEpisodes)
            .HasForeignKey(e => e.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.SeasonId, e.EpisodeNumber })
            .IsUnique();
    }
}