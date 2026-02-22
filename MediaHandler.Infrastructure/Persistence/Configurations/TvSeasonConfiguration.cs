using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class TvSeasonConfiguration : IEntityTypeConfiguration<TvSeason>
{
    public void Configure(EntityTypeBuilder<TvSeason> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SeasonNumber)
            .IsRequired();

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Overview)
            .HasMaxLength(2000);

        builder.Property(s => s.PosterPath)
            .HasMaxLength(500);

        builder.HasOne(s => s.Media)
            .WithMany(m => m.TvSeasons)
            .HasForeignKey(s => s.MediaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.MediaId, s.SeasonNumber })
            .IsUnique();
    }
}
