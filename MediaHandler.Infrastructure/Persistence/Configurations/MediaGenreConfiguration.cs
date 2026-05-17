using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class MediaGenreConfiguration : IEntityTypeConfiguration<MediaGenre>
{
    public void Configure(EntityTypeBuilder<MediaGenre> builder)
    {
        builder.HasKey(g => new { g.MediaId, g.Name });

        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasOne(g => g.Media)
            .WithMany(m => m.Genres)
            .HasForeignKey(g => g.MediaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(g => g.Name);
    }
}