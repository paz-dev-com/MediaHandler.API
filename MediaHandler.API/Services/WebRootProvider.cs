using MediaHandler.Application.Common.Interfaces;

namespace MediaHandler.API.Services;

/// <summary>
///     Adapts <see cref="IWebHostEnvironment" /> to the application-layer <see cref="IWebRootProvider" />
///     interface, keeping the Application project free of ASP.NET Core hosting dependencies.
/// </summary>
public sealed class WebRootProvider(IWebHostEnvironment env) : IWebRootProvider
{
    public string WebRootPath =>
        env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
}

