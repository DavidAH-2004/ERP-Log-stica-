using MarketplaceERP.API.Controllers;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// F369: shared HMAC identifier throttle (same result on each API instance).
/// This test accepts ONLY localhost temporary PostgreSQL, never Neon or prod.
/// </summary>
public sealed class DistributedLoginThrottleTests
{
    private const string KnownEmail = "f369-account@erp.invalid";
    private const string CorrectPassword = "f369-disposable-correct-only";
    private const string BadPassword = "f369-invalid-password-only";

    private static AuthController Controller(
        CoreDbContext db, bool enabled, bool peerLimiter = false,
        bool verifiedIngress = false, string? remoteIp = null,
        string? spoofedHeader = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Auth:EnableDistributedAccountLoginThrottle"] = enabled ? "true" : "false",
                ["Auth:EnableDistributedPeerLoginThrottle"] = peerLimiter ? "true" : "false",
                ["Auth:VerifiedIngressPeerIdentity"] = verifiedIngress ? "true" : "false"
            }).Build();
        var context = new DefaultHttpContext();
        if (remoteIp is not null)
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        if (spoofedHeader is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = spoofedHeader;
            context.Request.Headers["CF-Connecting-IP"] = spoofedHeader;
        }
        return new AuthController(db,
            Options.Create(new JwtSettings
            {
                Key = "F369-TEST-ONLY-PRIVATE-HMAC-AND-JWT-KEY-0000000000",
                Issuer = "f369.invalid",
                Audience = "f369-clients.invalid",
                ExpirationMinutes = 20
            }),
            NullLogger<AuthController>.Instance, config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = context
            }
        };
    }

    [Fact]
    public async Task FeatureFlagOff_PreservesExistingLoginContract()
    {
        await using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("f369-disabled-" + Guid.NewGuid().ToString("N")).Options);
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = KnownEmail, Nombre = "QA",
            Rol = "Admin", Activo = true
        };
        user.PasswordHash = new PasswordHasher<Usuario>().HashPassword(user, CorrectPassword);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();
        var controller = Controller(db, false);
        Assert.IsType<UnauthorizedObjectResult>(
            await controller.Login(new LoginRequest(KnownEmail, BadPassword), CancellationToken.None));
        Assert.IsType<OkObjectResult>(
            await controller.Login(new LoginRequest(KnownEmail, CorrectPassword), CancellationToken.None));
    }

    [Fact]
    public async Task PostgreSql_AtomicThrottlingAcrossContexts_NoPlaintextStored()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                throw new InvalidOperationException("F369 requires disposable CI PostgreSQL.");
            return;
        }
        var admin = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(admin.Host, StringComparer.OrdinalIgnoreCase) ||
            admin.Port != 5432 || admin.Database != "postgres" ||
            admin.Username != "postgres")
            throw new InvalidOperationException("F369 only permits postgres@localhost:5432/postgres.");

        var name = "f369_throttle_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(admin.ConnectionString))
        {
            await root.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE " + name, root);
            await create.ExecuteNonQueryAsync();
        }
        var scoped = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Database = name
        }.ConnectionString;

        CoreDbContext OpenDb() => new(new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(scoped, opts => opts.MigrationsHistoryTable("__EFMigrationsHistory", "core"))
            .Options);

        async Task<IActionResult> Attempt(string email, string password)
        {
            await using var db = OpenDb();
            return await Controller(db, true).Login(new LoginRequest(email, password), CancellationToken.None);
        }

        try
        {
            await using (var db = OpenDb())
            {
                await db.Database.MigrateAsync();
                var user = new Usuario
                {
                    Id = Guid.NewGuid(), Email = KnownEmail,
                    Nombre = "F369 Test Fixture", Rol = "Admin", Activo = true
                };
                user.PasswordHash = new PasswordHasher<Usuario>()
                    .HashPassword(user, CorrectPassword);
                db.Usuarios.Add(user);
                await db.SaveChangesAsync();
            }

            // Account exists: attempts 1–5 look like normal failures, #6
            // returns 429. Each call gets a separate scoped DbContext.
            for (var i = 0; i < 5; i++)
                Assert.IsType<UnauthorizedObjectResult>(await Attempt(KnownEmail, BadPassword));
            var blocked = Assert.IsType<ObjectResult>(
                await Attempt(KnownEmail, BadPassword));
            Assert.Equal(429, blocked.StatusCode);
            Assert.Equal(429, (await Attempt(KnownEmail, CorrectPassword)
                as ObjectResult)?.StatusCode);

            // Unknown email: identical rate limiting, no user enumeration.
            var missing = "f369-unknown@erp.invalid";
            for (var i = 0; i < 5; i++)
                Assert.IsType<UnauthorizedObjectResult>(await Attempt(missing, BadPassword));
            Assert.Equal(429, (await Attempt(missing, BadPassword)
                as ObjectResult)?.StatusCode);

            // 20 concurrent requests for another unknown email must never
            // allow more than five even across independent DB contexts.
            var other = "f369-concurrent@erp.invalid";
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 20)
                .Select(_ => Attempt(other, BadPassword)));
            Assert.Equal(5, outcomes.Count(x => x is UnauthorizedObjectResult));
            Assert.Equal(15, outcomes.Count(x => x is ObjectResult obj &&
                obj.StatusCode == 429));

            await using (var db = OpenDb())
            {
                var rows = await db.LoginThrottles.AsNoTracking().ToListAsync();
                Assert.Equal(3, rows.Count);
                Assert.All(rows, row =>
                {
                    Assert.Matches("^[0-9A-F]{64}$", row.AccountKey);
                    Assert.DoesNotContain("@", row.AccountKey);
                    Assert.DoesNotContain("f369", row.AccountKey, StringComparison.OrdinalIgnoreCase);
                });
                Assert.Equal(new[] { 20, 7, 6 },
                    rows.Select(x => x.Requests).OrderBy(x => x).Reverse().ToArray());
                // Shift known account outside the window to prove reset.
                var knownHash = rows.Single(x => x.Requests == 7).AccountKey;
                await db.LoginThrottles.Where(x => x.AccountKey == knownHash)
                    .ExecuteUpdateAsync(x => x.SetProperty(
                        v => v.WindowStartUtc, DateTime.UtcNow.AddMinutes(-2)));
            }

            Assert.IsType<OkObjectResult>(await Attempt(KnownEmail, CorrectPassword));
            await using (var db = OpenDb())
            {
                var rows = await db.LoginThrottles.AsNoTracking().ToListAsync();
                Assert.Contains(rows, row => row.Requests == 1);
            }

            // F492: 21 requests from one TCP peer with 21 DIFFERENT spoofed
            // XFF/CF headers, crossing contexts and sharing one DB window.
            async Task<IActionResult> PeerAttempt(string? peer, string? spoof,
                bool certified = true)
            {
                await using var db = OpenDb();
                return await Controller(db, false, peerLimiter: true,
                    verifiedIngress: certified, remoteIp: peer,
                    spoofedHeader: spoof).Login(
                        new LoginRequest("f492-unknown@erp.invalid", BadPassword),
                        CancellationToken.None);
            }

            var samePeer = Enumerable.Range(0, 21)
                .Select(i => PeerAttempt("203.0.113.12", "9.9.9." + i));
            var limited = await Task.WhenAll(samePeer);
            Assert.Equal(20, limited.Count(x => x is UnauthorizedObjectResult));
            Assert.Equal(1, limited.Count(x => x is ObjectResult o && o.StatusCode == 429));
            Assert.IsType<UnauthorizedObjectResult>(
                await PeerAttempt("203.0.113.13", "9.9.9.99"));
            Assert.Equal(503, (await PeerAttempt("203.0.113.13", null, false)
                as ObjectResult)?.StatusCode);
            Assert.Equal(503, (await PeerAttempt(null, null)
                as ObjectResult)?.StatusCode);

            await using (var db = OpenDb())
            {
                var rows = await db.LoginThrottles.AsNoTracking().ToListAsync();
                Assert.Equal(5, rows.Count);
                Assert.All(rows, row =>
                {
                    Assert.Matches("^[0-9A-F]{64}$", row.AccountKey);
                    Assert.DoesNotContain("203.0.113.", row.AccountKey);
                    Assert.DoesNotContain("f492", row.AccountKey);
                });
            }
        }
        finally
        {
            await using var root = new NpgsqlConnection(admin.ConnectionString);
            await root.OpenAsync();
            await using var delete = new NpgsqlCommand(
                "DROP DATABASE IF EXISTS " + name + " WITH (FORCE)", root);
            await delete.ExecuteNonQueryAsync();
        }
    }
}
