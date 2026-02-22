using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.HasKey(wi => wi.Id);

        builder.Property(wi => wi.TmdbId)
            .IsRequired();

        builder.Property(wi => wi.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(wi => wi.PosterPath)
            .HasMaxLength(500);

        builder.Property(wi => wi.Notes)
            .HasMaxLength(1000);

        builder.HasOne(wi => wi.User)
            .WithMany(u => u.WishlistItems)
            .HasForeignKey(wi => wi.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(wi => new { wi.UserId, wi.TmdbId });
    }
}
