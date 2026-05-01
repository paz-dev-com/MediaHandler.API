using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.HasKey(mf => mf.Id);

        builder.Property(mf => mf.FilePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(mf => mf.Format)
            .HasMaxLength(50);

        builder.Property(mf => mf.Resolution)
            .HasMaxLength(50);

        builder.HasOne(mf => mf.Media)
            .WithMany(m => m.MediaFiles)
            .HasForeignKey(mf => mf.MediaId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(mf => mf.FilePath)
            .IsUnique();

        builder.HasIndex(mf => mf.MediaId);

        // ── Scanner additions ────────────────────────────────────────────────

        builder.Property(mf => mf.Fingerprint)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(mf => mf.Role)
            .IsRequired()
            .HasConversion<string>();

        // Index on Fingerprint for fast incremental-scan dedup
        builder.HasIndex(mf => new { mf.LibraryRootId, mf.Fingerprint });

        // Indexes supporting removed-file detection and missing-file cleanup
        builder.HasIndex(mf => mf.LibraryRootId);
        builder.HasIndex(mf => mf.MissingSince);

        // LibraryRoot FK — nullable because a root can be deleted
        // Relationship managed by LibraryRootConfiguration; just add the index here.
    }
}