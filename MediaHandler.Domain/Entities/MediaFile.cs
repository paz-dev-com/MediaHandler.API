using MediaHandler.Domain.Common;

namespace MediaHandler.Domain.Entities;

public class MediaFile : BaseEntity
{
    public Guid? MediaId { get; set; }
    public required string FilePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? Format { get; set; }
    public string? Resolution { get; set; }

    public Media? Media { get; set; }
}
