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

            // F454: approval may append events (versions) for one request
            // but cannot overwrite earlier states or replay the same version.
            await using (var cn = new NpgsqlConnection(scratch.ConnectionString))
            {
                await cn.OpenAsync();
                var requestId = Guid.NewGuid();
                var actorId = Guid.NewGuid();
                async Task InsertEvent(int version, string estado)
                {
                    await using var cmd = new NpgsqlCommand("""
                        INSERT INTO catalog.rotaciones_ean
                            ("Id","SolicitudId","ProductoId","CodigoAnterior",
                             "CodigoNuevo","Motivo","SolicitadoPor","Version","Estado")
                        VALUES (@id,@request,@product,@previous,@next,@reason,@actor,@version,@state)
                        """, cn);
                    cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                    cmd.Parameters.AddWithValue("request", requestId);
                    cmd.Parameters.AddWithValue("product", p1.Id);
                    cmd.Parameters.AddWithValue("previous", actual);
                    cmd.Parameters.AddWithValue("next", extra);
                    cmd.Parameters.AddWithValue("reason", "Auditoría sintética F454");
                    cmd.Parameters.AddWithValue("actor", actorId);
                    cmd.Parameters.AddWithValue("version", version);
                    cmd.Parameters.AddWithValue("state", estado);
                    await cmd.ExecuteNonQueryAsync();
                }

                await InsertEvent(1, "Solicitada");
                await InsertEvent(2, "Aprobada");
                var duplicate = await Assert.ThrowsAsync<PostgresException>(
                    () => InsertEvent(2, "Aprobada"));
                Assert.Equal("23505", duplicate.SqlState);

                await using var forbidden = new NpgsqlCommand("""
                    UPDATE catalog.rotaciones_ean SET "Estado"='Rechazada'
                    WHERE "SolicitudId"=@request AND "Version"=1
                    """, cn);
                forbidden.Parameters.AddWithValue("request", requestId);
                var immutable = await Assert.ThrowsAsync<PostgresException>(
                    () => forbidden.ExecuteNonQueryAsync());
                Assert.Equal("23514", immutable.SqlState);

                await using var count = new NpgsqlCommand("""
                    SELECT count(*) FROM catalog.rotaciones_ean
                    WHERE "SolicitudId"=@request
                    """, cn);
                count.Parameters.AddWithValue("request", requestId);
                Assert.Equal(2, Convert.ToInt32(await count.ExecuteScalarAsync()));
            }

            // F457: synthetic alias read on disposable PostgreSQL only.
            // These rows simulate the future rotation output. No historical
            // product EAN is modified and no runtime rotation is enabled.
            var historicalAlias = CodigoBarras.GenerarInterno(74);
            await using (var cn = new NpgsqlConnection(scratch.ConnectionString))
            {
                await cn.OpenAsync();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO catalog.reservas_ean
                      ("CodigoEAN", "ProductoId", "Estado", "Origen")
                    VALUES (@code, @product, 'Alias', 'Emision')
                    """, cn);
                insert.Parameters.AddWithValue("code", historicalAlias);
                insert.Parameters.AddWithValue("product", p1.Id);
                Assert.Equal(1, await insert.ExecuteNonQueryAsync());
            }
            await using (var db = NewContext())
            {
                var disabled = await ConsultaCodigoEan.BuscarAsync(
                    db, historicalAlias, null, permitirHistoricos: false);
                Assert.Empty(disabled);

                var adminMatches = await ConsultaCodigoEan.BuscarAsync(
                    db, historicalAlias, null, permitirHistoricos: true);
                var match = Assert.Single(adminMatches);
                Assert.Equal(p1.Id, match.Id);
                Assert.Equal(actual, match.CodigoEAN);
                Assert.Equal(historicalAlias, match.CodigoLeido);
                Assert.True(match.EsAlias);

                var owner = await ConsultaCodigoEan.BuscarAsync(
                    db, historicalAlias, p1.VendedorId, permitirHistoricos: true);
                Assert.Single(owner);
                var other = await ConsultaCodigoEan.BuscarAsync(
                    db, historicalAlias, p2.VendedorId, permitirHistoricos: true);
                Assert.Empty(other);

                var current = await ConsultaCodigoEan.BuscarAsync(
                    db, actual, p1.VendedorId, permitirHistoricos: true);
                Assert.False(Assert.Single(current).EsAlias);
                Assert.Equal(actual,
                    (await db.Productos.SingleAsync(p => p.Id == p1.Id)).CodigoEAN);
            }

            // F460: WMS must preserve the EXACT old label scanned in the
            // same idempotent inventory operation, without changing product EAN.
            await using (var db = NewContext())
            {
                var location = new UbicacionAlmacen
                {
                    Id = Guid.NewGuid(),
                    Codigo = "F460-QA-UBICACION",
                    Pasillo = "F460-PASILLO",
                    Rack = "01", Nivel = 1, Posicion = "01",
                    CapacidadMaxima = 1000, EstaOcupada = true, Zona = "General"
                };
                db.UbicacionesAlmacen.Add(location);
                var stock = new StockFisico
                {
                    Id = Guid.NewGuid(), ProductoId = p1.Id,
                    UbicacionAlmacenId = location.Id, UbicacionCodigo = location.Codigo,
                    CantidadTotal = 2, PuntoReorden = 1
                };
                db.StockFisico.Add(stock);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                var conteos = new ConteoCiclicoService(
                    db,
                    new AjusteInventarioService(
                        db, Microsoft.Extensions.Logging.Abstractions.NullLogger<AjusteInventarioService>.Instance),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<ConteoCiclicoService>.Instance);
                var service = new SesionConteoService(
                    db, conteos, permitirHistoricos: true);
                var opened = await service.AbrirRecorridoAsync(location.Pasillo, "admin-qa");
                Assert.True(opened.Exitoso);

                var lecturaId = Guid.NewGuid();
                var result = await service.RegistrarLecturaAsync(
                    opened.Sesion!.Id, lecturaId, historicalAlias, 2,
                    stock.Id, "admin-qa");
                Assert.Equal(ResultadoLectura.Coincide, result.Resultado);
                var retry = await service.RegistrarLecturaAsync(
                    opened.Sesion.Id, lecturaId, historicalAlias, 2,
                    stock.Id, "admin-qa");
                Assert.Equal(result.Resultado, retry.Resultado);
                Assert.Equal(1, await db.LecturasConteo.CountAsync(l => l.Id == lecturaId));
                var recorded = await db.LecturasConteo.SingleAsync(l => l.Id == lecturaId);
                Assert.Equal(historicalAlias, recorded.CodigoLeido);
                Assert.Equal(p1.Id, recorded.ProductoId);
                Assert.Equal(actual,
                    (await db.Productos.SingleAsync(p => p.Id == p1.Id)).CodigoEAN);
            }

            // F470: a real two-actor, two-stage rotation on disposable PostgreSQL.
            // SQL functions must preserve A -> B -> C on the SAME ProductId.
            // No EAN changes are applied to Neon or Production.
            var second = CodigoBarras.GenerarInterno(75);
            var third = CodigoBarras.GenerarInterno(76);
            var requestOne = Guid.NewGuid();
            var requestTwo = Guid.NewGuid();
            var requester = Guid.NewGuid();
            var approver = Guid.NewGuid();
            const string justification = "Rotación de inventario verificada en CI";

            await using (var db = NewContext())
            {
                var svc = new RotacionEanService(db);
                Assert.Equal("Solicitada", await svc.SolicitarAsync(
                    requestOne, p1.Id, second, justification, requester));
                Assert.Equal("Solicitada", await svc.SolicitarAsync(
                    requestOne, p1.Id, second, justification, requester));
                Assert.Equal(actual,
                    (await db.Productos.SingleAsync(p => p.Id == p1.Id)).CodigoEAN);

                // The requesting administrator cannot approve their own EAN.
                var selfApproval = await Assert.ThrowsAsync<PostgresException>(
                    () => svc.AprobarAsync(requestOne, requester));
                Assert.Equal("23514", selfApproval.SqlState);
                Assert.Equal(actual,
                    (await db.Productos.SingleAsync(p => p.Id == p1.Id)).CodigoEAN);

                Assert.Equal("Ejecutada", await svc.AprobarAsync(requestOne, approver));
                Assert.Equal("Ejecutada", await svc.AprobarAsync(requestOne, approver));
                db.ChangeTracker.Clear(); // SQL function bypasses EF tracked instances.
                Assert.Equal(second,
                    (await db.Productos.SingleAsync(p => p.Id == p1.Id)).CodigoEAN);

                // Reusing the idempotency key for different target must fail.
                var reused = await Assert.ThrowsAsync<PostgresException>(
                    () => svc.SolicitarAsync(
                        requestOne, p1.Id, third, justification, requester));
                Assert.Equal("23505", reused.SqlState);

                Assert.Equal("Solicitada", await svc.SolicitarAsync(
                    requestTwo, p1.Id, third, justification, requester));
                Assert.Equal("Ejecutada", await svc.AprobarAsync(requestTwo, approver));
                db.ChangeTracker.Clear();
                Assert.Equal(third,
                    (await db.Productos.SingleAsync(p => p.Id == p1.Id)).CodigoEAN);

                foreach (var code in new[] { actual, second, third, historicalAlias })
                {
                    var found = await ConsultaCodigoEan.BuscarAsync(
                        db, code, p1.VendedorId, permitirHistoricos: true);
                    Assert.Equal(p1.Id, Assert.Single(found).Id);
                    Assert.Empty(await ConsultaCodigoEan.BuscarAsync(
                        db, code, p2.VendedorId, permitirHistoricos: true));
                }
                Assert.False(Assert.Single(await ConsultaCodigoEan.BuscarAsync(
                    db, third, null, permitirHistoricos: true)).EsAlias);
                Assert.Equal(2, await db.StockFisico
                    .Where(s => s.ProductoId == p1.Id).Select(s => s.CantidadTotal)
                    .SingleAsync());
                Assert.Equal(3, await db.Productos.CountAsync());

                // No Admin PUT/CSV or direct EF change can erase the last code.
                var tracked = await db.Productos.SingleAsync(p => p.Id == p1.Id);
                tracked.CodigoEAN = CodigoBarras.GenerarInterno(77);
                var blocked = await Assert.ThrowsAsync<DbUpdateException>(
                    () => db.SaveChangesAsync());
                Assert.Equal("23514",
                    Assert.IsType<PostgresException>(blocked.InnerException).SqlState);
            }

            // Append-only audit has exactly 3 immutable steps per request.
            await using (var cn = new NpgsqlConnection(scratch.ConnectionString))
            {
                await cn.OpenAsync();
                await using var audit = new NpgsqlCommand("""
                    SELECT "Version", "Estado" FROM catalog.rotaciones_ean
                    WHERE "SolicitudId" = @request ORDER BY "Version"
                    """, cn);
                audit.Parameters.AddWithValue("request", requestOne);
                await using var reader = await audit.ExecuteReaderAsync();
                var events = new List<string>();
                while (await reader.ReadAsync())
                    events.Add(reader.GetInt32(0) + ":" + reader.GetString(1));
                Assert.Equal(new[] { "1:Solicitada", "2:Aprobada", "3:Ejecutada" }, events);
            }
            await using (var db = NewContext())
            {
                var history = await new RotacionEanService(db).HistorialAsync(p1.Id);
                var events = history.Where(e => e.SolicitudId == requestOne)
                    .OrderBy(e => e.Version).ToArray();
                Assert.Equal(new[] { 1, 2, 3 },
                    events.Select(e => e.Version).ToArray());
                Assert.Equal("Ejecutada", events[2].Estado);
                Assert.Equal(second, events[2].CodigoNuevo);
            }

            // A previously printed label never becomes another product's code.
            await using (var db = NewContext())
            {
                db.Productos.Add(Nuevo("F470-REUSE", actual));
                var conflict = await Assert.ThrowsAsync<DbUpdateException>(
                    () => db.SaveChangesAsync());
                Assert.Equal("23505",
                    Assert.IsType<PostgresException>(conflict.InnerException).SqlState);
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
