using System.ComponentModel.DataAnnotations;

namespace MediaHandler.Infrastructure.Options;

public class OktaOptions
{
    public const string Section = "Okta";

    [Required] public required string Domain { get; set; }

    [Required] public required string ClientId { get; set; }

    [Required] public required string ClientSecret { get; set; }

    public string Audience { get; set; } = "api://mediahandler";
}