using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class KodiPathMappingConfiguration : IEntityTypeConfiguration<KodiPathMapping>
{
    public void Configure(EntityTypeBuilder<KodiPathMapping> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.KodiPrefix)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(m => m.NasPrefix)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasIndex(m => m.KodiPrefix)
            .IsUnique();

        builder.HasIndex(m => m.SortOrder);
    }
}
