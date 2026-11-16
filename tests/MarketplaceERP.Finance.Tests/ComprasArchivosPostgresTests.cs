using System.Security.Cryptography;
using MarketplaceERP.API.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// F613: real PostgreSQL integration checks, ONLY on a disposable localhost database.
/// This test never connects to Neon/Render or touches production data.
/// </summary>
public sealed class ComprasArchivosPostgresTests : IAsyncLifetime
{
    private const string BeforeArchive = "20261116060000_F483InboxWebhooksPagosDiferidos";
    private readonly string _database = "ci_archivos_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _testConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("F613 requiere PostgreSQL local en CI.");
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(builder.Host, StringComparer.OrdinalIgnoreCase)
            || builder.Port != 5432
            || !string.Equals(builder.Database, "postgres", StringComparison.Ordinal)
            || !string.Equals(builder.Username, "postgres", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(builder.Password))
            throw new InvalidOperationException("F613 solo admite postgres@localhost:5432/postgres.");

        _adminConnection = builder.ConnectionString;
        builder.Database = _database;
        _testConnection = builder.ConnectionString;

        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var create = admin.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{_database}\"";
        await create.ExecuteNonQueryAsync();
        _created = true;
        await using var finance = NewContext();
        await finance.Database.MigrateAsync();
    }

    private FinanceDbContext NewContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!, x =>
                x.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
            .Options);

    [Fact]
    public async Task PdfPersiste_NoDuplica_YRollbackConDatosQuedaBloqueado()
    {
        if (!_created) return;

        var poId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var po = new OrdenCompra
            {
                Id = poId,
                NumeroPO = "PO-ARCH-DB-" + Guid.NewGuid().ToString("N")[..8],
                ProveedorId = proveedorId,
                ProveedorNombre = "Proveedor de prueba",
                Estado = EstadoOrdenCompra.Borrador,
                SubTotal = 100m,
                ImpuestoIva = 13m,
                MontoTotal = 113m,
                FechaEmision = DateTime.UtcNow,
                TerminosPago = "Contado",
                Items = new List<ItemOrdenCompra>
                {
                    new() {
                        Id = Guid.NewGuid(), Sku = "F613-ITEM",
                        NombreProducto = "Producto F613", CantidadSolicitada = 1,
                        PrecioUnitarioAcordado = 100m
                    }
                }
            };
            db.OrdenesCompra.Add(po);
            await new ComprasArchivoPdfService(db).PrepararAsync(po, null, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        Guid originalFileId;
        byte[] firstPdf;
        await using (var reopened = NewContext())
        {
            var stored = await reopened.ArchivosCompras.AsNoTracking()
                .SingleAsync(x => x.OrdenCompraId == poId);
            originalFileId = stored.Id;
            firstPdf = stored.PdfContenido;
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(firstPdf[..8]));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(firstPdf)), stored.PdfSha256);
            Assert.Equal(1, stored.CantidadItems);
            Assert.Equal(113m, stored.MontoTotal);
        }

        await using (var duplicate = NewContext())
        {
            duplicate.ArchivosCompras.Add(new ArchivoCompra
            {
                Id = Guid.NewGuid(),
                OrdenCompraId = poId,
                NumeroPO = "PO-DUPLICADA-INVALIDA",
                ProveedorId = proveedorId,
                ProveedorNombre = "Duplicada",
                FechaCreacionUtc = DateTime.UtcNow,
                FechaActualizacionUtc = DateTime.UtcNow,
                CantidadItems = 1,
                Estado = "Borrador",
                MontoTotal = 113m,
                PdfContenido = firstPdf,
                PdfSha256 = Convert.ToHexString(SHA256.HashData(firstPdf))
            });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => duplicate.SaveChangesAsync());
            var pg = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
            Assert.Equal("IX_archivos_compras_OrdenCompraId", pg.ConstraintName);
        }

        await using (var downgrade = NewContext())
        {
            // A downgrade cannot silently destroy archival evidence.
            var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
                downgrade.GetService<IMigrator>().MigrateAsync(BeforeArchive));
            Assert.Contains("F601_ARCHIVOS_COMPRAS_CON_DATOS", exception.ToString());
        }

        await using (var check = NewContext())
        {
            var unchanged = await check.ArchivosCompras.AsNoTracking()
                .SingleAsync(x => x.OrdenCompraId == poId);
            Assert.Equal(originalFileId, unchanged.Id);
            Assert.Equal(firstPdf, unchanged.PdfContenido);
            Assert.Contains("20261116062428_F601ComprasArchivosDurable",
                await check.Database.GetAppliedMigrationsAsync());
        }
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null || !_database.StartsWith("ci_archivos_", StringComparison.Ordinal))
            return;
        if (_testConnection is not null)
        {
            using var pool = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(pool);
        }
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }
}
