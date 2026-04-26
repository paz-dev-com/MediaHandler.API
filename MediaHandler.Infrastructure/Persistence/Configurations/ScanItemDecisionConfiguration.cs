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

        // Composite index for per-run path lookups
        builder.HasIndex(d => new { d.ScanRunId, d.FilePath });
        builder.HasIndex(d => d.FilePath);

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
    }
}

