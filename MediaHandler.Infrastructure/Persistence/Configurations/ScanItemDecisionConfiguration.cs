using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class ScanItemDecisionConfiguration : IEntityTypeConfiguration<ScanItemDecision>
{
    public void Configure(EntityTypeBuilder<ScanItemDecision> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.FilePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(d => d.Kind)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(d => d.Reason)
            .HasMaxLength(500);

        builder.Property(d => d.RuleId)
            .HasMaxLength(100);

        // ── Dashboard API additions ─────────────────────────────────────────

        builder.Property(d => d.AssignedTmdbKind)
            .HasConversion<string>();

        builder.Property(d => d.CandidatesJson)
            .HasDefaultValue("[]");

        builder.Property(d => d.ParsedTitle)
            .HasMaxLength(500);

        builder.Property(d => d.ParsedMediaType)
            .HasConversion<string>();

        // ── Indexes ─────────────────────────────────────────────────────────

        // Composite index for per-run path lookups
        builder.HasIndex(d => new { d.ScanRunId, d.FilePath });
        builder.HasIndex(d => d.FilePath);

        // New dashboard indexes
        builder.HasIndex(d => new { d.ScanRunId, d.Kind })
            .HasDatabaseName("IX_ScanItemDecisions_ScanRunId_Kind");

        builder.HasIndex(d => new { d.ScanRunId, d.ParsedMediaType })
            .HasDatabaseName("IX_ScanItemDecisions_ScanRunId_ParsedMediaType");

        builder.HasIndex(d => d.LibraryRootId)
            .HasDatabaseName("IX_ScanItemDecisions_LibraryRootId");

        builder.HasIndex(d => new { d.ScanRunId, d.ParsedTitle })
            .HasDatabaseName("IX_ScanItemDecisions_ScanRunId_ParsedTitle");

        // ── Relationships ────────────────────────────────────────────────────

        builder.HasOne(d => d.MediaFile)
            .WithMany()
            .HasForeignKey(d => d.MediaFileId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.ReviewItem)
            .WithMany()
            .HasForeignKey(d => d.ReviewItemId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.LibraryRoot)
            .WithMany()
            .HasForeignKey(d => d.LibraryRootId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}