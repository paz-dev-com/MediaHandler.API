using MediaHandler.Domain.Enums;

namespace MediaHandler.Application.Common.Models.Scanner;

/// <summary>
///     Parameters forwarded from the Application command to the coordinator.
/// </summary>
public record ScanStartParameters(
    Guid ScanRunId,
    Guid[] LibraryRootIds,
    ScanMode Mode,
    string? Language = null);

/// <summary>
///     Lightweight handle returned by <c>IScanRunCoordinator.StartAsync</c>.
///     Callers use it to subscribe to progress or request cancellation.
/// </summary>
public record ScanRunHandle(Guid ScanRunId);

/// <summary>
///     Progress snapshot emitted into the progress channel while a scan is running.
///     Consumed by SSE/WebSocket endpoints or by integration tests polling in-memory.
/// </summary>
public record ScanProgressDto(
    Guid ScanRunId,
    string Phase,
    int Processed,
    int Total,
    string? LastFilePath,
    string? LastDecision);