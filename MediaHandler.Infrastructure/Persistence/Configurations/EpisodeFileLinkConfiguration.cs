using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class EpisodeFileLinkConfiguration : IEntityTypeConfiguration<EpisodeFileLink>
{
    public void Configure(EntityTypeBuilder<EpisodeFileLink> builder)
    {
        builder.HasKey(l => l.Id);

        // Composite unique constraint: prevents duplicate links
        builder.HasIndex(l => new { l.TvEpisodeId, l.MediaFileId, l.OrderInFile })
            .IsUnique();

        builder.HasOne(l => l.TvEpisode)
            .WithMany(e => e.EpisodeFileLinks)
            .HasForeignKey(l => l.TvEpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.MediaFile)
            .WithMany(mf => mf.EpisodeLinks)
            .HasForeignKey(l => l.MediaFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

