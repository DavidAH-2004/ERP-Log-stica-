using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Ejercita los tres contextos y sus historiales EF en un ÚNICO PostgreSQL real,
/// pero en una base efímera del runner. No admite Neon ni hosts remotos.
/// Valida específicamente el upgrade de CodigoEAN opcional con datos preexistentes.
/// </summary>
public sealed class PostgresSchemaMigrationSmokeTests : IAsyncLifetime
{
    private const string LastCatalogMigrationBeforeNullableEan =
        "20261027163015_Fase6CAlertasInventario";
    private const string NullableEanMigration = "20261106005614_CodigoEanOpcional";

    private readonly string _databaseName = "ci_schema_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _ephemeralConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "CI requiere TEST_PG_CONNECTION local para verificar las tres migraciones.");
            return; // Ejecución local sin PostgreSQL: el test es optativo, no en CI.
        }

        NpgsqlConnectionStringBuilder builder;
        try { builder = new NpgsqlConnectionStringBuilder(raw); }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("La conexión PostgreSQL de pruebas es inválida.");
        }

        // No aceptar Neon, URL arbitraria, otro puerto/usuario/base ni failover multi-host.
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(builder.Host, StringComparer.OrdinalIgnoreCase) ||
            builder.Port != 5432 ||
            !string.Equals(builder.Database, "postgres", StringComparison.Ordinal) ||
            !string.Equals(builder.Username, "postgres", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(builder.Password))
            throw new InvalidOperationException(
                "El smoke test solo admite PostgreSQL local: postgres@localhost:5432/postgres.");

        _adminConnection = builder.ConnectionString;
        builder.Database = _databaseName;
        _ephemeralConnection = builder.ConnectionString;

        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        // _databaseName lo genera el test, no procede de entradas externas.
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
        await command.ExecuteNonQueryAsync();
        _created = true;
    }

    [Fact]
    public async Task TresEsquemas_MigranEnMismaBase_YUpgradeEanConservaDatos()
    {
        if (!_created) return;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _ephemeralConnection,
                ["Payments:Provider"] = "Disabled",
                ["Payments:EnableMock"] = "false"
            }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreServices(configuration);
        services.AddCatalogServices(configuration);
        services.AddFinanceServices(configuration, isDevelopment: false);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        await core.Database.MigrateAsync();
        await finance.Database.MigrateAsync();

        // Simular un esquema anterior con EAN obligatorio y un producto real persistido.
        var migrator = catalog.GetService<IMigrator>();
        await migrator.MigrateAsync(LastCatalogMigrationBeforeNullableEan);
        Assert.Equal("NO", await CodigoEanEsNullableAsync());

        var before = new Producto
        {
            Id = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid(),
            Sku = "SMOKE-EAN-ANTERIOR-" + Guid.NewGuid().ToString("N")[..8],
            Nombre = "Producto EAN original", Descripcion = "Smoke migraciones",
            CategoriaNombre = "Tecnología", Subcategoria = "Prueba", PrecioB2C = 20m,
            PrecioB2B = 15m, StockMinimo = 1, ProveedorNombre = "Proveedor CI",
            PaisOrigen = "El Salvador", PesoKg = 1m, DimensionesCm = "1x1x1",
            UnidadMedida = "Unidad", EstadoProducto = "Activo",
            CodigoEAN = "7891234567895", EstaPublicado = true,
            FechaCreacion = DateTime.UtcNow
        };
        catalog.Productos.Add(before);
        await catalog.SaveChangesAsync();
        catalog.ChangeTracker.Clear();

        await catalog.Database.MigrateAsync();
        Assert.Equal("YES", await CodigoEanEsNullableAsync());

        var existing = await catalog.Productos.AsNoTracking().SingleAsync(x => x.Id == before.Id);
        Assert.Equal(before.CodigoEAN, existing.CodigoEAN);

        var withoutEan = new Producto
        {
            Id = Guid.NewGuid(), VendedorId = before.VendedorId,
            CategoriaId = before.CategoriaId, Sku = "SMOKE-EAN-NULO-" + Guid.NewGuid().ToString("N")[..8],
            Nombre = "Producto sin EAN", Descripcion = "Acepta EAN NULL",
            CategoriaNombre = "Tecnología", Subcategoria = "Prueba", PrecioB2C = 10m,
            PrecioB2B = 9m, StockMinimo = 1, ProveedorNombre = "Proveedor CI",
            PaisOrigen = "El Salvador", PesoKg = 1m, DimensionesCm = "1x1x1",
            UnidadMedida = "Unidad", EstadoProducto = "Activo",
            CodigoEAN = null, EstaPublicado = true, FechaCreacion = DateTime.UtcNow
        };
        catalog.Productos.Add(withoutEan);
        await catalog.SaveChangesAsync();
        catalog.ChangeTracker.Clear();
        Assert.Null((await catalog.Productos.AsNoTracking()
            .SingleAsync(x => x.Id == withoutEan.Id)).CodigoEAN);

        await AssertMigrationStateAsync(core);
        await AssertMigrationStateAsync(catalog, NullableEanMigration);
        await AssertMigrationStateAsync(finance);

        // Idempotencia: segundo arranque no debe ejecutar migrations ni borrar registros.
        await core.Database.MigrateAsync();
        await catalog.Database.MigrateAsync();
        await finance.Database.MigrateAsync();
        await AssertMigrationStateAsync(core);
        await AssertMigrationStateAsync(catalog, NullableEanMigration);
        await AssertMigrationStateAsync(finance);
        Assert.Equal(2, await catalog.Productos.CountAsync());

        // F483: real ephemeral PostgreSQL UNIQUE/immutable provider inbox.
        // A valid HMAC only allows a durable "requires lookup" entry. It
        // NEVER changes orders, refunds, stock or financial state.
        var accountId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        const string mockSecret = "f483-test-only-no-merchant-secret";
        string Body(bool productive) => System.Text.Json.JsonSerializer.Serialize(new
        {
            IdCuenta = accountId, IdTransaccion = transactionId,
            Aplicativo = new { Id = applicationId }, Monto = 10.25m,
            EsProductiva = productive, ResultadoTransaccion = "ExitosaAprobada"
        });
        static byte[] Bytes(string body) => System.Text.Encoding.UTF8.GetBytes(body);
        static string Signature(byte[] bytes) => Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(mockSecret), bytes));
        var normal = Bytes(Body(true));
        var signature = Signature(normal);
        var inbox = new MarketplaceERP.Finance.Services.WompiSvWebhookInbox(finance);
        Assert.Equal(MarketplaceERP.Finance.Services.ResultadoInboxWompiSv.NuevoRequiereConsulta,
            await inbox.RegistrarAsync(normal, signature, mockSecret,
                accountId, applicationId, transactionId, 10.25m));
        Assert.Equal(MarketplaceERP.Finance.Services.ResultadoInboxWompiSv.RepetidoSinMutacion,
            await inbox.RegistrarAsync(normal, signature, mockSecret,
                accountId, applicationId, transactionId, 10.25m));
        var altered = Bytes(Body(true) + "\n");
        Assert.Equal(MarketplaceERP.Finance.Services.ResultadoInboxWompiSv.ConflictoBloqueado,
            await inbox.RegistrarAsync(altered, Signature(altered), mockSecret,
                accountId, applicationId, transactionId, 10.25m));
        var sandbox = Bytes(Body(false));
        Assert.Equal(MarketplaceERP.Finance.Services.ResultadoInboxWompiSv.SandboxSinCargo,
            await inbox.RegistrarAsync(sandbox, Signature(sandbox), mockSecret,
                accountId, applicationId, transactionId, 10.25m));
        Assert.Equal(MarketplaceERP.Finance.Services.ResultadoInboxWompiSv.Rechazado,
            await inbox.RegistrarAsync(normal, new string('0', 64), mockSecret,
                accountId, applicationId, transactionId, 10.25m));
        // F494: independent DbContexts/connections race with one signed
        // transaction identity. A unique constraint, not in-memory locks,
        // must decide which request can append. No double settlement path.
        var concurrentTxn = Guid.NewGuid();
        var concurrentBody = Bytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            IdCuenta = accountId, IdTransaccion = concurrentTxn,
            Aplicativo = new { Id = applicationId }, Monto = 10.25m,
            EsProductiva = true, ResultadoTransaccion = "ExitosaAprobada"
        }));
        var concurrentSignature = Signature(concurrentBody);
        async Task<MarketplaceERP.Finance.Services.ResultadoInboxWompiSv> Race()
        {
            await using var fresh = provider.CreateAsyncScope();
            var separateContext = fresh.ServiceProvider.GetRequiredService<FinanceDbContext>();
            return await new MarketplaceERP.Finance.Services.WompiSvWebhookInbox(
                separateContext).RegistrarAsync(
                    concurrentBody, concurrentSignature, mockSecret,
                    accountId, applicationId, concurrentTxn, 10.25m);
        }
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Race()));
        Assert.Single(concurrent, outcome =>
            outcome == MarketplaceERP.Finance.Services.ResultadoInboxWompiSv.NuevoRequiereConsulta);
        Assert.Equal(15, concurrent.Count(outcome =>
            outcome == MarketplaceERP.Finance.Services.ResultadoInboxWompiSv.RepetidoSinMutacion));
        var tamperedSameIdentity = Bytes(System.Text.Encoding.UTF8.GetString(concurrentBody) + "\n");
        Assert.Equal(MarketplaceERP.Finance.Services.ResultadoInboxWompiSv.ConflictoBloqueado,
            await inbox.RegistrarAsync(tamperedSameIdentity, Signature(tamperedSameIdentity),
                mockSecret, accountId, applicationId, concurrentTxn, 10.25m));

        await using (var sql = new NpgsqlConnection(_ephemeralConnection))
        {
            await sql.OpenAsync();
            await using var countInbox = new NpgsqlCommand(
                "SELECT count(*) FROM finance.webhooks_pago_inbox", sql);
            Assert.Equal(2L, (long)(await countInbox.ExecuteScalarAsync())!);
            await using var mutate = new NpgsqlCommand("""
                UPDATE finance.webhooks_pago_inbox SET "Estado"='RequiereConsulta'
                WHERE "TransaccionId"=@id
                """, sql);
            mutate.Parameters.AddWithValue("id", transactionId);
            var blocked = await Assert.ThrowsAsync<PostgresException>(
                () => mutate.ExecuteNonQueryAsync());
            Assert.Equal("23514", blocked.SqlState);
        }
    }

    private async Task<string?> CodigoEanEsNullableAsync()
    {
        await using var connection = new NpgsqlConnection(_ephemeralConnection!);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_schema = 'catalog' AND table_name = 'productos' AND column_name = 'CodigoEAN'
            """;
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task AssertMigrationStateAsync<TContext>(
        TContext context, string? requiredMigration = null) where TContext : DbContext
    {
        var known = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(known);
        Assert.Equal(known.OrderBy(x => x, StringComparer.Ordinal),
            applied.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.True(await ReadinessSchemaProbe.CurrentMigrationsAppliedAsync(context));
        if (requiredMigration is not null) Assert.Contains(requiredMigration, applied);
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null ||
            !_databaseName.StartsWith("ci_schema_", StringComparison.Ordinal)) return;
        if (_ephemeralConnection is not null)
        {
            using var pool = new NpgsqlConnection(_ephemeralConnection);
            NpgsqlConnection.ClearPool(pool);
        }
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
        _created = false;
    }
}
