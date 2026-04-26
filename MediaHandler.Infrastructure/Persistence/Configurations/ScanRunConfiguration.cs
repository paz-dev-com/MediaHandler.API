using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class ScanRunConfiguration : IEntityTypeConfiguration<ScanRun>
{
    public void Configure(EntityTypeBuilder<ScanRun> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Mode)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.FailureReason)
            .HasMaxLength(2000);

        builder.Property(r => r.LibraryRootIdsJson)
            .IsRequired()
            .HasDefaultValue("[]");

        // Index on StartedAt for history queries
        builder.HasIndex(r => r.StartedAt);

        // Filtered unique index: at most one row may have Status = 'Running'.
        // EF Core does not natively support SQL Server filtered indexes with a string
        // conversion, so we use a raw-SQL annotation and add a regular index for EF.
        builder.HasIndex(r => r.Status)
            .HasFilter("[Status] = 'Running'")
            .IsUnique();

        builder.HasMany(r => r.Decisions)
            .WithOne(d => d.ScanRun)
            .HasForeignKey(d => d.ScanRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

