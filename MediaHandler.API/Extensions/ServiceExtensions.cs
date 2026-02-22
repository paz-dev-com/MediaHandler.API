using System.Threading.RateLimiting;
using MediaHandler.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace MediaHandler.API.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var okta = configuration.GetSection(OktaOptions.Section).Get<OktaOptions>()!;

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = okta.Domain;
                options.Audience = okta.Audience;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));

        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter("fixed", limiter =>
            {
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.PermitLimit = 100;
                limiter.QueueLimit = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });

        return services;
    }

    public static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "MediaHandler API",
                Version = "v1",
                Description = "Personal media management API for Freebox NAS"
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Enter your Okta JWT token"
            });

            options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer", doc),
                    new List<string>()
                }
            });
        });

        return services;
    }
}
