using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class EnrichmentRunConfiguration : IEntityTypeConfiguration<EnrichmentRun>
{
    public void Configure(EntityTypeBuilder<EnrichmentRun> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.FailureReason)
            .HasMaxLength(1000);

        builder.Property(r => r.StartedAt)
            .IsRequired();

        builder.Property(r => r.CurrentItem)
            .HasMaxLength(500);

        // ErrorDetailsJson stored as nvarchar(max) — no max length constraint
        builder.Property(r => r.ErrorDetailsJson);

        // ── Indexes ─────────────────────────────────────────────────────────

        builder.HasIndex(r => r.Status)
            .HasDatabaseName("IX_EnrichmentRuns_Status");

        // Filtered unique index: at most one row may have Status = 'Running'.
        // Follows the same pattern as ScanRunConfiguration for ScanStatus.
        builder.HasIndex(r => r.Status)
            .HasFilter("[Status] = 'Running'")
            .IsUnique()
            .HasDatabaseName("UX_EnrichmentRuns_Running");
    }
}

