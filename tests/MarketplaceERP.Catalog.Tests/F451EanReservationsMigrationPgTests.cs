using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>F451: only GitHub Actions localhost PostgreSQL, disposable database.</summary>
public sealed class F451EanReservationsMigrationPgTests
{
    [Fact]
    public async Task BackfillYReservaNoReutilizable_CodigoOriginalIntacto_RollbackProtegido()
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

        var name = "f451_ean_" + Guid.NewGuid().ToString("N");
        var scratch = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
        await using (var cn = new NpgsqlConnection(admin.ConnectionString))
        {
            await cn.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE \"" + name + "\"", cn);
            await create.ExecuteNonQueryAsync();
        }

        CatalogDbContext NewContext() => new(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(scratch.ConnectionString,
                    o => o.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")).Options);
        const string previous = "20261115183000_EanNoVacioUnico";
        const string migration = "20261115231000_F451ReservasEanHistoricas";
        var actual = CodigoBarras.GenerarInterno(71);
        var emitted = CodigoBarras.GenerarInterno(72);
        var extra = CodigoBarras.GenerarInterno(73);
        var p1 = Nuevo("F451-A", actual);
        var p2 = Nuevo("F451-B", "HISTORICO-INVALIDO");
        var p3 = Nuevo("F451-C", null);
        try
        {
            await using (var db = NewContext())
            {
                await db.GetService<IMigrator>().MigrateAsync(previous);
                db.Productos.AddRange(p1, p2, p3);
                await db.SaveChangesAsync();
                await db.Database.MigrateAsync();
                Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
                Assert.Equal(actual, (await db.Productos.SingleAsync(x => x.Id == p1.Id)).CodigoEAN);
                Assert.Equal("HISTORICO-INVALIDO",
                    (await db.Productos.SingleAsync(x => x.Id == p2.Id)).CodigoEAN);
            }

            async Task<int> CountReservations()
            {
                await using var cn = new NpgsqlConnection(scratch.ConnectionString);
                await cn.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "SELECT count(*) FROM catalog.reservas_ean", cn);
                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            Assert.Equal(1, await CountReservations());

            // Down remains possible BEFORE any new issuance or rotation.
            await using (var db = NewContext())
            {
                await db.GetService<IMigrator>().MigrateAsync(previous);
                Assert.DoesNotContain(migration, await db.Database.GetAppliedMigrationsAsync());
                await db.Database.MigrateAsync();
            }
            Assert.Equal(1, await CountReservations());

            // The existing issuance path reserves the new EAN atomically.
            await using (var db = NewContext())
            {
                var unassigned = await db.Productos.SingleAsync(x => x.Id == p3.Id);
                unassigned.CodigoEAN = emitted;
                await db.SaveChangesAsync();
            }
            Assert.Equal(2, await CountReservations());

            // A legacy code cannot be silently changed/erased via direct SQL
            // or an EF update, including by another writer.
            await using (var db = NewContext())
            {
                var original = await db.Productos.SingleAsync(x => x.Id == p1.Id);
                original.CodigoEAN = extra;
                var error = await Assert.ThrowsAsync<DbUpdateException>(
                    () => db.SaveChangesAsync());
                Assert.Equal("23514", Assert.IsType<PostgresException>(error.InnerException).SqlState);
            }
            await using (var db = NewContext())
            {
                Assert.Equal(actual, (await db.Productos.SingleAsync(x => x.Id == p1.Id)).CodigoEAN);
                Assert.Equal(emitted, (await db.Productos.SingleAsync(x => x.Id == p3.Id)).CodigoEAN);
                Assert.Equal(3, await db.Productos.CountAsync());
                await Assert.ThrowsAnyAsync<Exception>(
                    () => db.GetService<IMigrator>().MigrateAsync(previous));
                Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
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

    private static Producto Nuevo(string sku, string? ean) => new()
    {
        Id = Guid.NewGuid(), Sku = sku, Nombre = sku,
        CodigoEAN = ean, CategoriaNombre = "QA", CategoriaId = Guid.NewGuid(),
        VendedorId = Guid.NewGuid(), EstadoProducto = "Activo", EstaPublicado = true
    };
}
