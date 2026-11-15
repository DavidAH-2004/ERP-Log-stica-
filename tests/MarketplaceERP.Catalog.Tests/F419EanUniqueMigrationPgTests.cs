using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// F419: ensayo de migración fallando cerrado con duplicados históricos.
/// Exclusivamente PostgreSQL localhost desechable de GitHub Actions.
/// </summary>
public sealed class F419EanUniqueMigrationPgTests
{
    [Fact]
    public async Task PreflightDuplicadosDetieneUpgradeYPermiteConciliarSinCambiarDatos()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("CI") != "true")
            return;

        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(raw));
        var admin = new NpgsqlConnectionStringBuilder(raw);
        Assert.Contains(admin.Host, new[] { "localhost", "127.0.0.1", "::1" });
        Assert.Equal(5432, admin.Port);
        Assert.Equal("postgres", admin.Database);
        Assert.Equal("postgres", admin.Username);
        Assert.False(string.IsNullOrEmpty(admin.Password));

        var name = "f419_unique_ean_" + Guid.NewGuid().ToString("N");
        var scratch = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
        await using (var cn = new NpgsqlConnection(admin.ConnectionString))
        {
            await cn.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE \"" + name + "\"", cn);
            await create.ExecuteNonQueryAsync();
        }

        CatalogDbContext NewContext() => new(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(scratch.ConnectionString, o =>
                    o.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")).Options);
        const string previous = "20261115075500_SesionesConteoEscaner";
        const string newMigration = "20261115183000_EanNoVacioUnico";
        try
        {
            await using (var db = NewContext())
            {
                await db.GetService<IMigrator>().MigrateAsync(previous);
                // Códigos heredados duplicados en el escenario de ensayo.
                db.Productos.AddRange(
                    NewProduct("F419-HIST-A", "HIST-OTRO-CODIGO"),
                    NewProduct("F419-HIST-B", "HIST-OTRO-CODIGO"));
                await db.SaveChangesAsync();
                await Assert.ThrowsAnyAsync<Exception>(() => db.Database.MigrateAsync());
                Assert.DoesNotContain(newMigration, await db.Database.GetAppliedMigrationsAsync());
                Assert.Equal(2, await db.Productos.CountAsync(p => p.CodigoEAN == "HIST-OTRO-CODIGO"));
            }

            await using (var db = NewContext())
            {
                var extra = await db.Productos.SingleAsync(p => p.Sku == "F419-HIST-B");
                db.Productos.Remove(extra); // solo ensayo descartable, NO operación cloud
                await db.SaveChangesAsync();
                await db.Database.MigrateAsync();
                Assert.Contains(newMigration, await db.Database.GetAppliedMigrationsAsync());
                Assert.Equal("HIST-OTRO-CODIGO", (await db.Productos.SingleAsync()).CodigoEAN);
            }

            // Un índice único, no solo una consulta de aplicación, debe impedir
            // la carrera entre importación CSV, Admin y Vendedor.
            await using (var db = NewContext())
            {
                db.Productos.Add(NewProduct("F419-CHECK", "HIST-OTRO-CODIGO"));
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                var pg = Assert.IsType<PostgresException>(error.InnerException);
                Assert.Equal("23505", pg.SqlState);
                Assert.Equal("UX_productos_CodigoEAN_no_vacio", pg.ConstraintName);
            }

            await using (var db = NewContext())
            {
                Assert.Equal(1, await db.Productos.CountAsync());
                await db.GetService<IMigrator>().MigrateAsync(previous);
                Assert.DoesNotContain(newMigration, await db.Database.GetAppliedMigrationsAsync());
                Assert.Equal("HIST-OTRO-CODIGO", (await db.Productos.SingleAsync()).CodigoEAN);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cn = new NpgsqlConnection(admin.ConnectionString);
            await cn.OpenAsync();
            await using var drop = new NpgsqlCommand(
                "DROP DATABASE IF EXISTS \"" + name + "\" WITH (FORCE)", cn);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static Producto NewProduct(string sku, string code) => new()
    {
        Id = Guid.NewGuid(), Sku = sku, Nombre = sku,
        CodigoEAN = code, CategoriaNombre = "QA", CategoriaId = Guid.NewGuid(),
        VendedorId = Guid.NewGuid(), EstadoProducto = "Activo", EstaPublicado = true
    };
}
