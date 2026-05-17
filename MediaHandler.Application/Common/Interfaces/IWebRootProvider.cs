namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
///     Provides the web root path for file storage operations.
///     Abstracts <c>IWebHostEnvironment.WebRootPath</c> to keep the Application layer
///     free of ASP.NET Core hosting dependencies.
/// </summary>
public interface IWebRootProvider
{
    string WebRootPath { get; }
}

