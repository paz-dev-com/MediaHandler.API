using System.Threading.RateLimiting;
using MediaHandler.API.Identity;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Infrastructure.Options;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace MediaHandler.API.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            services.AddAuthentication(DevAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName,
                    null);
        }
        else
        {
            var okta = configuration.GetSection(OktaOptions.Section).Get<OktaOptions>()!;

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    // Auth0 requires a trailing slash on the authority URL for OIDC discovery
                    options.Authority = okta.Domain.TrimEnd('/') + '/';
                    options.Audience = okta.Audience;

                    // Preserve raw JWT claim names (sub, email, name…) instead of remapping
                    // them to the long XML-schema ClaimTypes equivalents.
                    // Without this, ASP.NET maps "sub" → ClaimTypes.NameIdentifier, so
                    // User.FindFirstValue("sub") returns null and /auth/me cannot look up
                    // the user by OktaId even though the user exists in the database.
                    options.MapInboundClaims = false;

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                        // Auth0 places roles in a namespaced custom claim
                        RoleClaimType = "https://mediahandler.com/roles",
                        // Keep "sub" as the name claim type to stay consistent with raw JWT names
                        NameClaimType = "sub"
                    };
                });
        }

        services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy("AdminOnly", policy => policy.AddRequirements(new AdminRequirement()));

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
                Description = "Enter your Auth0 JWT token"
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

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddHealthChecks()
            .AddDbContextCheck<MediaHandlerDbContext>("database");

        return services;
    }
}