using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// F397: prueba EF real, solamente contra PostgreSQL local desechable del CI.
/// No permite Render/Neon ni bases existentes; crea y destruye su propia DB.
/// </summary>
public sealed class F397BarcodeMigrationPgTests
{
    [Fact]
    public async Task UpgradePreservesHistoricalEans_AndRollbackGuardsSessions()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.Ordinal) ||
            !string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.Ordinal))
            return;

        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(raw));
        var admin = new NpgsqlConnectionStringBuilder(raw);
        Assert.Contains(admin.Host, new[] { "localhost", "127.0.0.1", "::1" });
        Assert.Equal(5432, admin.Port);
        Assert.Equal("postgres", admin.Database);

        var dbName = "f397_barcode_" + Guid.NewGuid().ToString("N");
        var scratch = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = dbName };
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE \"" + dbName + "\"", conn);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(scratch.ConnectionString, o =>
                    o.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
                .Options;
            await using var db = new CatalogDbContext(options);
            var migrations = db.GetService<IMigrator>();
            const string beforeBarcodes = "20261110013000_WmsReferenciasExternasUnicas";
            const string afterBarcodes = "20261115072000_CodigosBarrasInternosSeguros";

            // Install the historical schema, then add old identifiers BEFORE upgrade.
            await migrations.MigrateAsync(beforeBarcodes);
            var oldValid = new Producto
            {
                Id = Guid.NewGuid(), Sku = "F397-VALID", Nombre = "Legacy valid",
                CodigoEAN = "4006381333931", CategoriaNombre = "QA",
                CategoriaId = Guid.NewGuid(), VendedorId = Guid.NewGuid(),
                EstadoProducto = "Activo", EstaPublicado = true
            };
            var oldNonstandard = new Producto
            {
                Id = Guid.NewGuid(), Sku = "F397-LEGACY", Nombre = "Legacy invalid",
                CodigoEAN = "7410000000191", CategoriaNombre = "QA",
                CategoriaId = Guid.NewGuid(), VendedorId = Guid.NewGuid(),
                EstadoProducto = "Activo", EstaPublicado = true
            };
            db.Productos.AddRange(oldValid, oldNonstandard);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            // Migrate through F402 and F399 without wiping previous codes.
            await migrations.MigrateAsync();
            var actual = await db.Productos.AsNoTracking().OrderBy(p => p.Sku)
                .Select(p => p.CodigoEAN).ToListAsync();
            Assert.Equal(new[] { "7410000000191", "4006381333931" }, actual);
            Assert.Contains("20261115072000_CodigosBarrasInternosSeguros",
                await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261115075500_SesionesConteoEscaner",
                await db.Database.GetAppliedMigrationsAsync());

            // An open audit session must prevent dropping the session tables.
            var session = new SesionConteo
            {
                Id = Guid.NewGuid(), Numero = "SC-2026-000001",
                Estado = EstadoSesionConteo.Abierta, Modo = ModoSesionConteo.Planificado,
                CreadaPor = "F397-CI-ONLY", FechaInicio = DateTime.UtcNow
            };
            db.SesionesConteo.Add(session);
            await db.SaveChangesAsync();
            await Assert.ThrowsAnyAsync<Exception>(
                () => migrations.MigrateAsync(afterBarcodes));
            Assert.Equal(1, await db.SesionesConteo.CountAsync());
            db.SesionesConteo.Remove(session);
            await db.SaveChangesAsync();

            // Empty session tables permit a clean rollback without changing legacy EAN.
            await migrations.MigrateAsync(afterBarcodes);
            await migrations.MigrateAsync(beforeBarcodes);
            var rolledBack = await db.Productos.AsNoTracking().OrderBy(p => p.Sku)
                .Select(p => p.CodigoEAN).ToListAsync();
            Assert.Equal(new[] { "7410000000191", "4006381333931" }, rolledBack);
        }
        finally
        {
            // The connection to scratch is disposed before DROP DATABASE FORCE.
            await using var cleanup = new NpgsqlConnection(admin.ConnectionString);
            await cleanup.OpenAsync();
            await using var drop = new NpgsqlCommand(
                "DROP DATABASE IF EXISTS \"" + dbName + "\" WITH (FORCE)", cleanup);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
