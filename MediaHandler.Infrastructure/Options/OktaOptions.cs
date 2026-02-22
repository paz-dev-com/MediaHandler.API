namespace MediaHandler.Infrastructure.Options;

public class OktaOptions
{
    public const string Section = "Okta";

    public required string Domain { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public string Audience { get; set; } = "api://mediahandler";
}
