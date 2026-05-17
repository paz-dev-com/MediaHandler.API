using MediaHandler.Application.Features.Dashboard.DTOs;
using MediaHandler.Application.Common.Models;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Service responsible for computing proposed rename targets for media files
///     and optionally executing the filesystem rename and updating the database.
///     <para>
///         Naming conventions:
///         <list type="bullet">
///             <item><term>Film</term><description><c>{Title} ({Year}).{ext}</c></description></item>
///             <item><term>TV episode</term><description><c>{ShowName} - S{s:D2}E{e:D2} - {EpisodeName}.{ext}</c></description></item>
///         </list>
///     </para>
/// </summary>
public interface IFileRenameService
{
    /// <summary>
    ///     Computes the proposed rename target for the media file identified by
    ///     <paramref name="mediaFileId" /> without writing any changes to disk or database.
    /// </summary>
    /// <param name="mediaFileId">Primary key of the <c>MediaFile</c> to rename.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     A <see cref="Result{T}" /> containing a <see cref="FileRenameResultDto" /> with
    ///     <c>Executed = false</c>, or a failure result with an error code if the operation
    ///     cannot proceed (e.g., <c>TMDB_ASSIGNMENT_REQUIRED</c>, <c>FILE_NOT_FOUND</c>).
    /// </returns>
    Task<Result<FileRenameResultDto>> PreviewRenameAsync(Guid mediaFileId, CancellationToken ct = default);

    /// <summary>
    ///     Executes an atomic <c>File.Move</c> rename for the media file identified by
    ///     <paramref name="mediaFileId" />, then updates <c>MediaFile.FilePath</c> in the database.
    ///     If the database save fails, the filesystem rename is compensated by moving the file back.
    /// </summary>
    /// <param name="mediaFileId">Primary key of the <c>MediaFile</c> to rename.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     A <see cref="Result{T}" /> containing a <see cref="FileRenameResultDto" /> with
    ///     <c>Executed = true</c>, or a failure result with an error code if the rename fails
    ///     (e.g., <c>FILE_CONFLICT</c>, <c>FILE_NOT_FOUND</c>).
    /// </returns>
    Task<Result<FileRenameResultDto>> ExecuteRenameAsync(Guid mediaFileId, CancellationToken ct = default);
}

