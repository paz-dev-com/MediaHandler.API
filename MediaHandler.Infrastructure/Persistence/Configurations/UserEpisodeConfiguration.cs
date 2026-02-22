using MediaHandler.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaHandler.Infrastructure.Persistence.Configurations;

public class UserEpisodeConfiguration : IEntityTypeConfiguration<UserEpisode>
{
    public void Configure(EntityTypeBuilder<UserEpisode> builder)
    {
        builder.HasKey(ue => ue.Id);

        builder.HasOne(ue => ue.User)
            .WithMany(u => u.UserEpisodes)
            .HasForeignKey(ue => ue.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ue => ue.Episode)
            .WithMany(e => e.UserEpisodes)
            .HasForeignKey(ue => ue.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ue => new { ue.UserId, ue.EpisodeId })
            .IsUnique();
    }
}
