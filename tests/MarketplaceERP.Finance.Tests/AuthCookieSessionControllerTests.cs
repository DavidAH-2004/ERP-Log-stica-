using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// F356: BFF identity endpoint tests with a disposable in-memory CoreDb.
/// The actual HTTP role/anonymous middleware is independently covered by
/// AutorizacionU2Tests. No external database or credentials are used.
/// </summary>
public sealed class AuthCookieSessionControllerTests
{
    private static (CoreDbContext Db, AuthController Controller, Usuario User) Create(
        string databaseRole = "Admin", string claimRole = "Admin",
        bool active = true, bool validExpiry = true, bool includeId = true)
    {
        var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("f356-session-" + Guid.NewGuid().ToString("N")).Options);
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "f356-fixture@erp.invalid",
            Nombre = "F356 Fixture",
            Rol = databaseRole,
            Activo = active,
            PasswordHash = "unused-for-session-probe",
            VendedorId = databaseRole == "Vendedor" ? Guid.NewGuid() : null
        };
        db.Usuarios.Add(user);
        db.SaveChanges();

        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, claimRole),
            new(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow
                .AddMinutes(validExpiry ? 10 : -10).ToUnixTimeSeconds().ToString())
        };
        if (includeId)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));

        var settings = Options.Create(new JwtSettings
        {
            Key = "F356-UNIT-TEST-ONLY-MOCK-KEY-NEVER-DEPLOY",
            Issuer = "f356.invalid",
            Audience = "f356.invalid",
            ExpirationMinutes = 30
        });

        var controller = new AuthController(db, settings, NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "f356-test"))
                }
            }
        };
        return (db, controller, user);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Vendedor")]
    [InlineData("Comprador")]
    public async Task ActiveMatchingRole_OnlyReturnsIdentityNotBearer(string role)
    {
        var (db, controller, user) = Create(role, role);
        await using (db)
        {
            var result = Assert.IsType<OkObjectResult>(await controller.Session(CancellationToken.None));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
            var identity = json.RootElement;
            Assert.Equal(role, identity.GetProperty("rol").GetString());
            Assert.Equal(role, identity.GetProperty("role").GetString());
            Assert.Equal(user.Nombre, identity.GetProperty("usuario").GetString());
            Assert.Equal(user.Email, identity.GetProperty("email").GetString());
            Assert.True(identity.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);
            Assert.False(identity.TryGetProperty("token", out _));
            Assert.False(identity.TryGetProperty("passwordHash", out _));
            if (role == "Vendedor")
                Assert.Equal(user.VendedorId.ToString(), identity.GetProperty("vendedorId").GetString());
            else
                Assert.Equal(JsonValueKind.Null, identity.GetProperty("vendedorId").ValueKind);
        }
    }

    [Theory]
    [InlineData(false, true, true, "Admin", "Admin")]
    [InlineData(true, false, true, "Admin", "Admin")]
    [InlineData(true, true, false, "Admin", "Admin")]
    [InlineData(true, true, true, "Vendedor", "Admin")]
    [InlineData(true, true, true, "Admin", "Comprador")]
    [InlineData(true, true, true, "UnknownRole", "UnknownRole")]
    public async Task DeactivatedExpiredMissingIdentityOrRoleChange_IsRejected(
        bool active, bool validExpiry, bool includeId, string dbRole, string claimRole)
    {
        var (db, controller, _) = Create(dbRole, claimRole, active, validExpiry, includeId);
        await using (db)
        {
            Assert.IsType<UnauthorizedObjectResult>(await controller.Session(CancellationToken.None));
        }
    }
}
