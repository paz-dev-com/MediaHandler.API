namespace MediaHandler.Domain.Entities;

public class MediaGenre
{
    public Guid MediaId { get; set; }
    public required string Name { get; set; }

    public Media Media { get; set; } = null!;
}