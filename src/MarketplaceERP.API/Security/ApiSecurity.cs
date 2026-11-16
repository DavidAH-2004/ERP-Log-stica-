using System.Security.Claims;
using System.Text;
using MarketplaceERP.Shared.Settings;
using MarketplaceERP.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace MarketplaceERP.API.Security;

public static class ApiSecurity
{
    // Shared by production registration and isolated middleware tests; no host or database startup.
    public static IServiceCollection AddMarketplaceSecurity(this IServiceCollection services, JwtSettings settings, bool enableSessionVersionCheck = false)
    {
        if (string.IsNullOrWhiteSpace(settings.Key) || settings.Key.Length < 32 ||
            settings.Key.Contains("CAMBIA", StringComparison.OrdinalIgnoreCase) ||
            settings.Key.Contains("DEV_ONLY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Jwt:Key requiere una clave externa privada de al menos 32 caracteres, sin placeholders.");
        if (string.IsNullOrWhiteSpace(settings.Issuer) || string.IsNullOrWhiteSpace(settings.Audience))
            throw new InvalidOperationException("Jwt:Issuer y Jwt:Audience son obligatorios.");
        if (settings.ExpirationMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Jwt:ExpirationMinutes debe estar entre 1 y 1440 minutos.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var principal = context.Principal;
                    var identities = principal?.FindAll(ClaimTypes.NameIdentifier)
                        .Select(c => c.Value).ToArray() ?? Array.Empty<string>();
                    if (identities.Length == 0 || identities.Any(value =>
                            !Guid.TryParse(value, out var id) || id == Guid.Empty) ||
                        identities.Select(Guid.Parse).Distinct().Count() != 1)
                    {
                        context.Fail("Identidad de usuario inválida o ambigua.");
                        return;
                    }
                    if (!enableSessionVersionCheck) return;
                    var versions = principal?.FindAll("sv").Select(c => c.Value).ToArray()
                        ?? Array.Empty<string>();
                    var roles = principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()
                        ?? Array.Empty<string>();
                    if (versions.Length != 1 ||
                        !int.TryParse(versions[0], out var tokenVersion) || tokenVersion < 0 ||
                        roles.Length != 1 || UsuarioRoles.Canonizar(roles[0]) != roles[0])
                    {
                        context.Fail("Generación de sesión inválida.");
                        return;
                    }
                    try
                    {
                        var userId = Guid.Parse(identities[0]);
                        var db = context.HttpContext.RequestServices.GetRequiredService<CoreDbContext>();
                        var current = await db.Usuarios.AsNoTracking()
                            .Where(u => u.Id == userId)
                            .Select(u => new { u.Activo, u.Rol, u.VersionSesion })
                            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                        if (current is null || !current.Activo ||
                            current.VersionSesion != tokenVersion ||
                            UsuarioRoles.Canonizar(current.Rol) != roles[0])
                            context.Fail("Sesión revocada o identidad desactualizada.");
                    }
                    catch (Exception ex) when (
                        ex is DbUpdateException || ex is InvalidOperationException ||
                        ex is System.Data.Common.DbException || ex is OperationCanceledException)
                    {
                        // Fail closed on outage/missing migrations.
                        context.Fail("No se puede verificar la sesión.");
                    }
                }
            };
        });
        services.AddAuthorization(options => options.FallbackPolicy =
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }
}
