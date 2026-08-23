using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class ReviewItemConfiguration : IEntityTypeConfiguration<ReviewItem>
{
    public void Configure(EntityTypeBuilder<ReviewItem> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.FilePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(r => r.Reason)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(Domain.Enums.ReviewItemSource.Scan);

        builder.Property(r => r.ParsedTitle)
            .HasMaxLength(500);

        builder.Property(r => r.ResolvedBy)
            .HasMaxLength(200);

        builder.Property(r => r.ResolvedKind)
            .HasConversion<string>();

        // CandidatesJson stored as a JSON column
        builder.Property(r => r.CandidatesJson)
            .IsRequired()
            .HasDefaultValue("[]");

        // Indexes: status for queue queries, FilePath for per-path lookups
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.FilePath);

        // Filtered unique index: at most one Open item per FilePath
        builder.HasIndex(r => new { r.FilePath, r.Status })
            .HasFilter("[Status] = 'Open'")
            .IsUnique();
    }
}