using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class ImportItemOutcomeConfiguration : IEntityTypeConfiguration<ImportItemOutcome>
{
    public void Configure(EntityTypeBuilder<ImportItemOutcome> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.KodiItemKind)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(o => o.MediaKind)
            .HasConversion<string>();

        builder.Property(o => o.Outcome)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(o => o.LinkOutcome)
            .HasConversion<string>();

        builder.Property(o => o.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(o => o.Reason)
            .HasMaxLength(1000);

        builder.Property(o => o.KodiPathPrefix)
            .HasMaxLength(500);

        builder.HasIndex(o => o.ImportRunId);
        builder.HasIndex(o => new { o.ImportRunId, o.Outcome });

        // Baseline lookups: find an item from a previous run by its Kodi identity
        builder.HasIndex(o => new { o.KodiItemKind, o.KodiItemId });
    }
}
