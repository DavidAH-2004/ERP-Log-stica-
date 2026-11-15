using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// F360: logout invalidation persists across process/DbContext boundaries.
/// PostgreSQL test creates and drops ONLY a random database on localhost:5432.
/// </summary>
public sealed class JwtSessionGenerationTests
{
    private static AuthController Controller(CoreDbContext db, bool enabled)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Auth:EnableSessionVersionCheck"] = enabled ? "true" : "false"
            }).Build();
        return new AuthController(db,
            Options.Create(new JwtSettings
            {
                Key = "F360-LOCAL-TEST-KEY-NOT-FOR-PRODUCTION-00000000000",
                Issuer = "f360.invalid",
                Audience = "f360-client.invalid",
                ExpirationMinutes = 30
            }),
            NullLogger<AuthController>.Instance, config);
    }

    private static string TokenVersion(string jwt) =>
        new JwtSecurityTokenHandler().ReadJwtToken(jwt)
            .Claims.Single(c => c.Type == "sv").Value;

    // F366: Execute the REAL JWT authentication/authorization middleware against
    // an isolated PostgreSQL database, not a mocked claims principal.
    private static async Task<int> ProbeStrictBearerAsync(string connection, string jwt)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CoreDbContext>(o => o.UseNpgsql(connection,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", "core")));
        services.AddMarketplaceSecurity(new JwtSettings
        {
            Key = "F360-LOCAL-TEST-KEY-NOT-FOR-PRODUCTION-00000000000",
            Issuer = "f360.invalid",
            Audience = "f360-client.invalid",
            ExpirationMinutes = 30
        }, enableSessionVersionCheck: true);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Headers.Authorization = "Bearer " + jwt;
        http.Response.Body = new MemoryStream();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthorizeAttribute { Roles = "Admin" }),
            "f366-strict-revocation"));
        var app = new ApplicationBuilder(provider);
        app.UseAuthentication();
        app.UseAuthorization();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await app.Build()(http);
        return http.Response.StatusCode;
    }


    [Fact]
    public async Task LogoutWithFeatureFlagOff_Reports503NotFalseSuccess()
    {
        await using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("f360-flag-" + Guid.NewGuid().ToString("N")).Options);
        var response = Assert.IsType<ObjectResult>(
            await Controller(db, enabled: false).Logout(CancellationToken.None));
        Assert.Equal(503, response.StatusCode);
    }

    [Fact]
    public async Task LogoutIncrementsVersionAtomicallyAndReloginUsesNewGeneration()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                throw new InvalidOperationException("F360 requires the CI isolated PostgreSQL.");
            return;
        }

        var admin = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
            .Contains(admin.Host, StringComparer.OrdinalIgnoreCase) ||
            admin.Port != 5432 || admin.Database != "postgres" ||
            admin.Username != "postgres")
            throw new InvalidOperationException("F360 only permits postgres@localhost:5432/postgres.");

        var database = "f360_revocation_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(admin.ConnectionString))
        {
            await root.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE " + database, root);
            await create.ExecuteNonQueryAsync();
        }

        var local = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Database = database
        }.ConnectionString;

        try
        {
            Guid userId;
            await using (var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
                .UseNpgsql(local).Options))
            {
                await db.Database.MigrateAsync();
                var user = new Usuario
                {
                    Id = Guid.NewGuid(),
                    Email = "f360@erp.invalid",
                    Nombre = "F360 only",
                    Activo = true,
                    Rol = "Admin",
                    VersionSesion = 0
                };
                userId = user.Id;
                user.PasswordHash = new PasswordHasher<Usuario>()
                    .HashPassword(user, "f360-disposable-password");
                db.Usuarios.Add(user);
                await db.SaveChangesAsync();

                var controller = Controller(db, enabled: true);
                var first = Assert.IsType<OkObjectResult>(await controller.Login(
                    new LoginRequest(user.Email, "f360-disposable-password"),
                    CancellationToken.None));
                var bearer = Assert.IsType<LoginResponse>(first.Value).Token;
                Assert.Equal("0", TokenVersion(bearer));
                // The pre-logout JWT is accepted by actual strict middleware.
                Assert.Equal(200, await ProbeStrictBearerAsync(local, bearer));

                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                            new Claim(ClaimTypes.Role, "Admin"),
                            new Claim("sv", "0")
                        }, "f360-test"))
                    }
                };
                Assert.IsType<NoContentResult>(
                    await controller.Logout(CancellationToken.None));
                Assert.IsType<UnauthorizedObjectResult>(
                    await controller.Logout(CancellationToken.None));
                // After a shared PostgreSQL generation update, the old signed
                // JWT MUST be rejected by the same ASP.NET auth middleware.
                Assert.Equal(401, await ProbeStrictBearerAsync(local, bearer));
            }

            await using (var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
                .UseNpgsql(local).Options))
            {
                var user = await db.Usuarios.SingleAsync(x => x.Id == userId);
                Assert.Equal(1, user.VersionSesion);
                var nextLogin = Assert.IsType<OkObjectResult>(
                    await Controller(db, enabled: true).Login(
                        new LoginRequest(user.Email, "f360-disposable-password"),
                        CancellationToken.None));
                var nextBearer = Assert.IsType<LoginResponse>(nextLogin.Value).Token;
                Assert.Equal("1", TokenVersion(nextBearer));
                Assert.Equal(200, await ProbeStrictBearerAsync(local, nextBearer));

                // Revocation must also work if the account role changes or is
                // deactivated, independently of the signed JWT's expiry.
                user.Rol = "Vendedor";
                await db.SaveChangesAsync();
                Assert.Equal(401, await ProbeStrictBearerAsync(local, nextBearer));
                user.Rol = "Admin";
                user.Activo = false;
                await db.SaveChangesAsync();
                Assert.Equal(401, await ProbeStrictBearerAsync(local, nextBearer));
            }
        }
        finally
        {
            await using var root = new NpgsqlConnection(admin.ConnectionString);
            await root.OpenAsync();
            await using var command = new NpgsqlCommand(
                "DROP DATABASE IF EXISTS " + database + " WITH (FORCE)", root);
            await command.ExecuteNonQueryAsync();
        }
    }
}
