using System.ComponentModel.DataAnnotations;

namespace MediaHandler.Infrastructure.Options;

public class TmdbOptions
{
    public const string Section = "Tmdb";

    public string BaseUrl { get; set; } = "https://api.themoviedb.org/3";
    public string ImageBaseUrl { get; set; } = "https://image.tmdb.org/t/p";

    [Required] public required string ReadAccessToken { get; set; }
}