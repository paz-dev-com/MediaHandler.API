using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class MediaConfiguration : IEntityTypeConfiguration<Media>
{
    public void Configure(EntityTypeBuilder<Media> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.TmdbId)
            .IsRequired();

        builder.Property(m => m.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(m => m.OriginalTitle)
            .HasMaxLength(500);

        builder.Property(m => m.Overview)
            .HasMaxLength(2000);

        builder.Property(m => m.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(m => m.PosterPath)
            .HasMaxLength(500);

        builder.Property(m => m.BackdropPath)
            .HasMaxLength(500);

        builder.Property(m => m.VoteAverage)
            .HasPrecision(3, 1);

        builder.Property(m => m.Genres)
            .HasMaxLength(500);

        builder.Property(m => m.Language)
            .HasMaxLength(10);

        builder.HasIndex(m => m.TmdbId);
        builder.HasIndex(m => m.Title);
        builder.HasIndex(m => m.Type);
    }
}
