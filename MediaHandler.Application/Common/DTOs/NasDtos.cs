namespace MediaHandler.Application.Common.DTOs;

public record NasFileInfo(
    string FilePath,
    string FileName,
    long SizeBytes,
    string? Format,
    DateTime CreatedAt,
    DateTime ModifiedAt);
