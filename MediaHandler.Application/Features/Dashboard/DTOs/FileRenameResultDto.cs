namespace MediaHandler.Application.Features.Dashboard.DTOs;

/// <summary>
///     Result of a single file rename operation, covering both preview and executed modes.
/// </summary>
public record FileRenameResultDto(
    Guid MediaFileId,
    string CurrentFileName,
    string ProposedFileName,
    string CurrentPath,
    string ProposedPath,
    bool Executed);

