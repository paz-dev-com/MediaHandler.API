using System.Text.Json;
using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class LibraryRootConfiguration : IEntityTypeConfiguration<LibraryRoot>
{
    public void Configure(EntityTypeBuilder<LibraryRoot> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Path)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(r => r.Kind)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.Label)
            .HasMaxLength(200);

        // SearchLanguages: stored as a JSON array string.
        // No explicit column type — EF Core picks the appropriate type for the provider
        // (nvarchar(max) for SQL Server, text for PostgreSQL).
        // Null when not configured — the scanner falls back to the global default ("en-US").
        builder.Property(r => r.SearchLanguages)
            .HasMaxLength(2000)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : (IReadOnlyList<string>?)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()))
            .IsRequired(false);

        // Unique index on Path — enforces no duplicate roots
        builder.HasIndex(r => r.Path)
            .IsUnique();

        builder.HasMany(r => r.MediaFiles)
            .WithOne(mf => mf.LibraryRoot)
            .HasForeignKey(mf => mf.LibraryRootId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}