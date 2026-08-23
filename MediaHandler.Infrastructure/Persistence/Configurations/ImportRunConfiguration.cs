using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    public void Configure(EntityTypeBuilder<ImportRun> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Mode)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.SourceFileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(r => r.UploadedFilePath)
            .HasMaxLength(1000);

        builder.Property(r => r.FailureReason)
            .HasMaxLength(2000);

        builder.Property(r => r.PathMappingsJson)
            .IsRequired()
            .HasDefaultValue("[]");

        builder.Property(r => r.UnmatchedPrefixesJson)
            .IsRequired()
            .HasDefaultValue("[]");

        // Index on StartedAt for history queries
        builder.HasIndex(r => r.StartedAt);

        // Filtered unique index: at most one row may have Status = 'Running'
        // (single-active-import invariant, mirrors ScanRunConfiguration).
        builder.HasIndex(r => r.Status)
            .HasFilter("[Status] = 'Running'")
            .IsUnique();

        builder.HasMany(r => r.Outcomes)
            .WithOne(o => o.ImportRun)
            .HasForeignKey(o => o.ImportRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
