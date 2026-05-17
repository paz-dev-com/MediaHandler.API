using System.ComponentModel.DataAnnotations;

namespace MediaHandler.Infrastructure.Options;

public class NasOptions
{
    public const string Section = "Nas";

    public List<string> BasePaths { get; set; } = [];
    public string FreeboxUrl { get; set; } = "http://mafreebox.freebox.fr";

    [Required] public required string AppId { get; set; }

    [Required] public required string AppToken { get; set; }

    public string ApiVersion { get; set; } = "v8";
}