using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class UserMediaConfiguration : IEntityTypeConfiguration<UserMedia>
{
    public void Configure(EntityTypeBuilder<UserMedia> builder)
    {
        builder.HasKey(um => um.Id);

        builder.Property(um => um.PersonalRating)
            .HasPrecision(3, 1);

        builder.Property(um => um.Notes)
            .HasMaxLength(1000);

        builder.HasOne(um => um.User)
            .WithMany(u => u.UserMedias)
            .HasForeignKey(um => um.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(um => um.Media)
            .WithMany(m => m.UserMedias)
            .HasForeignKey(um => um.MediaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(um => new { um.UserId, um.MediaId })
            .IsUnique();
    }
}