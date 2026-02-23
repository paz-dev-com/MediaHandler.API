using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

public class Media : BaseEntity
{
    public required int TmdbId { get; set; }
    public required string Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public MediaType Type { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public int? Runtime { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public decimal? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public string? Language { get; set; }

    public ICollection<MediaGenre> Genres { get; set; } = new List<MediaGenre>();
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
    public ICollection<UserMedia> UserMedias { get; set; } = new List<UserMedia>();
    public ICollection<TvSeason> TvSeasons { get; set; } = new List<TvSeason>();
}
