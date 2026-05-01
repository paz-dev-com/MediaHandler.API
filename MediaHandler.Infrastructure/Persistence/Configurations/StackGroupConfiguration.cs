using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class StackGroupConfiguration : IEntityTypeConfiguration<StackGroup>
{
    public void Configure(EntityTypeBuilder<StackGroup> builder)
    {
        builder.HasKey(g => g.Id);

        // At most one StackGroup per Media
        builder.HasIndex(g => g.MediaId)
            .IsUnique();

        builder.HasOne(g => g.Media)
            .WithOne()
            .HasForeignKey<StackGroup>(g => g.MediaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.Parts)
            .WithOne(mf => mf.StackGroup)
            .HasForeignKey(mf => mf.StackGroupId)
            .IsRequired(false)
            // ClientSetNull avoids SQL Server "multiple cascade paths" error:
            // EF sets the FK to null in memory before DELETE; no DB-level cascade needed.
            .OnDelete(DeleteBehavior.ClientSetNull);
    }
}