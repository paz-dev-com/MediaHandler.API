using MediaHandler.API.Extensions;
using MediaHandler.API.Middleware;
using MediaHandler.API.Services;
using MediaHandler.Application;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Infrastructure.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Serilog;
using static MediaHandler.Infrastructure.DependencyInjection;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .WriteTo.Console()
        .WriteTo.File("logs/mediahandler-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7));

    builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddScoped<IWebRootProvider, WebRootProvider>();

    builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddApiBehavior();
    builder.Services.AddApiRateLimiting();
    builder.Services.AddApiSwagger();
    builder.Services.AddApiHealthChecks();

    builder.Services.AddCors(options =>
    {
        var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        options.AddPolicy("AllowFrontend", policy =>
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
    });

    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 524_288_000;
    });

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 524_288_000;
    });

    var app = builder.Build();
    var useHttpsRedirection = builder.Configuration.GetValue("App:UseHttpsRedirection", true);

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MediaHandler API v1"));
    }

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();
    if (useHttpsRedirection)
        app.UseHttpsRedirection();
    app.UseCors("AllowFrontend");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health");

    await app.InitialiseDatabaseAsync();

    // Ensure the profile picture upload directory exists
    var env = app.Services.GetRequiredService<IWebHostEnvironment>();
    var uploadsDir = Path.Combine(
        env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
        "uploads",
        "profile-pictures");
    Directory.CreateDirectory(uploadsDir);

    // Ensure multipart buffering and Kodi import staging both use writable temp directories.
    var aspNetTempDir = Environment.GetEnvironmentVariable("ASPNETCORE_TEMP");
    if (!string.IsNullOrWhiteSpace(aspNetTempDir))
        Directory.CreateDirectory(aspNetTempDir);

    var kodiImportOptions = app.Services.GetRequiredService<IOptions<KodiImportOptions>>().Value;
    Directory.CreateDirectory(kodiImportOptions.EffectiveTempDirectory);

    // Recover any ScanRun rows stuck in Running status after a crash/restart
    await ApplyScanRunRecoveryAsync(app.Services);

    // Recover any ImportRun rows stuck in Pending/Running status and purge orphaned uploads
    await ApplyImportRunRecoveryAsync(app.Services);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Expose the auto-generated Program class for WebApplicationFactory in integration tests
public partial class Program
{
}