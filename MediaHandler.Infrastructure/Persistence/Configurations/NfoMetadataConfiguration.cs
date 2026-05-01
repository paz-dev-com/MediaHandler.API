using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class NfoMetadataConfiguration : IEntityTypeConfiguration<NfoMetadata>
{
    public void Configure(EntityTypeBuilder<NfoMetadata> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.SourcePath)
            .IsRequired()
            .HasMaxLength(1024);

        // RawContent capped: store first 32 KB for diagnostics only
        builder.Property(n => n.RawContent)
            .IsRequired()
            .HasMaxLength(32768);

        builder.Property(n => n.Title)
            .HasMaxLength(500);

        builder.Property(n => n.ImdbId)
            .HasMaxLength(20);

        builder.Property(n => n.ParseError)
            .HasMaxLength(1000);

        // Unique index on SourcePath prevents duplicate NFO rows across incremental scans
        builder.HasIndex(n => n.SourcePath)
            .IsUnique();
    }
}