using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class ExclusionRuleConfiguration : IEntityTypeConfiguration<ExclusionRule>
{
    public void Configure(EntityTypeBuilder<ExclusionRule> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Pattern)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(r => r.Scope)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.RuleId)
            .IsRequired()
            .HasMaxLength(100);

        // Stable identifier is unique
        builder.HasIndex(r => r.RuleId)
            .IsUnique();
    }
}