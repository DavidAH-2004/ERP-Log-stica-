using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

public partial class SesionConteoServiceTests
{
    // ── Task 3: registrar lectura ────────────────────────────────────────────

    private static async Task<Guid> Recorrido(CatalogDbContext db, string pasillo = "Pasillo A") =>
        (await Sut(db).AbrirRecorridoAsync(pasillo, Usuario)).Sesion!.Id;

    private static Task<ResultadoLecturaDto> Leer(CatalogDbContext db, Guid sesion, string? codigo, int cantidad,
        Guid? stockFisicoId = null, Guid? lecturaId = null)
    {
        db.ChangeTracker.Clear();
        return Sut(db).RegistrarLecturaAsync(sesion, lecturaId ?? Guid.NewGuid(), codigo ?? string.Empty, cantidad, stockFisicoId, Usuario);
    }

    private static async Task<ConteoCiclico> ConteoActual(CatalogDbContext db, Guid stockId)
    {
        db.ChangeTracker.Clear();
        return await db.ConteosCiclicos.OrderByDescending(c => c.FechaCreacion).ThenByDescending(c => c.Estado)
            .FirstAsync(c => c.StockFisicoId == stockId && c.Estado != EstadoConteoCiclico.Cancelado);
    }

    private static async Task<(Producto p, StockFisico s)> UnProducto(CatalogDbContext db, string pasillo = "Pasillo A", int total = 5)
    {
        var p = Producto(db, $"P-{Guid.NewGuid():N}"[..8]);
        var s = Stock(db, p, Ubicacion(db, pasillo), total);
        await Guardar(db);
        return (p, s);
    }

    [Fact]
    public async Task Lectura_CantidadIgual_Coincide_ConteoCompletadoYVinculado()
    {
        using var db = Db();
        var (p, s) = await UnProducto(db);
        var sesion = await Recorrido(db);

        var r = await Leer(db, sesion, p.CodigoEAN, 5);

        r.Resultado.Should().Be(ResultadoLectura.Coincide);
        r.Mensaje.Should().Be("Coincide.");
        r.Sku.Should().Be(p.Sku);
        r.UbicacionCodigo.Should().Be(s.UbicacionCodigo);
        var conteo = await ConteoActual(db, s.Id);
        conteo.Estado.Should().Be(EstadoConteoCiclico.Completado);
        conteo.SesionConteoId.Should().Be(sesion);
    }

    [Fact]
    public async Task Lectura_CantidadDistinta_Diferencia_RequiereReconteo()
    {
        using var db = Db();
        var (p, s) = await UnProducto(db);
        var sesion = await Recorrido(db);

        var r = await Leer(db, sesion, p.CodigoEAN, 3);

        r.Resultado.Should().Be(ResultadoLectura.Diferencia);
        r.Mensaje.Should().Be("Hay diferencia: vuelve a contarlo.");
        (await ConteoActual(db, s.Id)).Estado.Should().Be(EstadoConteoCiclico.RequiereReconteo);
    }

    [Fact]
    public async Task Lectura_ReconteoDistinto_AjusteCreado_AjustePendienteAprobacion()
    {
        using var db = Db();
        var (p, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        await Leer(db, sesion, p.CodigoEAN, 3);

        var r = await Leer(db, sesion, p.CodigoEAN, 3);

        r.Resultado.Should().Be(ResultadoLectura.AjusteCreado);
        r.Mensaje.Should().Be("Diferencia confirmada: se creó un ajuste para aprobación.");
        (await ConteoActual(db, s.Id)).Estado.Should().Be(EstadoConteoCiclico.CompletadoConAjuste);
        (await db.AjustesInventario.SingleAsync(a => a.StockFisicoId == s.Id)).Estado
            .Should().Be(EstadoAjusteInventario.PendienteAprobacion);
    }

    [Fact]
    public async Task Lectura_ReconteoIgual_Coincide()
    {
        using var db = Db();
        var (p, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        await Leer(db, sesion, p.CodigoEAN, 3);

        (await Leer(db, sesion, p.CodigoEAN, 5)).Resultado.Should().Be(ResultadoLectura.Coincide);
        (await ConteoActual(db, s.Id)).Estado.Should().Be(EstadoConteoCiclico.Completado);
    }

    [Fact]
    public async Task Lectura_ProductoSinStockEnElPasillo_FueraDeLugar_GuardaHallazgo()
    {
        using var db = Db();
        await UnProducto(db, "Pasillo A");
        var (enB, _) = await UnProducto(db, "Pasillo B");
        var sesion = await Recorrido(db, "Pasillo A");

        var r = await Leer(db, sesion, enB.CodigoEAN, 4);

        r.Resultado.Should().Be(ResultadoLectura.FueraDeLugar);
        r.Mensaje.Should().Be("Encontrado fuera de lugar: quedó registrado para el supervisor.");
        var h = await db.HallazgosConteo.SingleAsync();
        (h.Sku, h.Pasillo, h.Cantidad, h.Usuario).Should().Be((enB.Sku, "Pasillo A", 4, Usuario));
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Lectura_DosPosicionesEnElPasillo_ElegirUbicacion_SinLecturaGuardada()
    {
        using var db = Db();
        var p = Producto(db, "P-DOS");
        Stock(db, p, Ubicacion(db, "Pasillo A", "02"), 5);
        Stock(db, p, Ubicacion(db, "Pasillo A", "01"), 2);
        await Guardar(db);
        var sesion = await Recorrido(db);

        var r = await Leer(db, sesion, p.CodigoEAN, 5);

        r.Resultado.Should().Be(ResultadoLectura.ElegirUbicacion);
        r.Mensaje.Should().Be("Este producto está en varias posiciones: elige cuál estás contando.");
        r.Opciones.Should().HaveCount(2);
        r.Opciones!.Select(o => o.UbicacionCodigo).Should().BeInAscendingOrder();
        (await db.LecturasConteo.AnyAsync()).Should().BeFalse();
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Lectura_ConStockFisicoElegido_Cuenta()
    {
        using var db = Db();
        var p = Producto(db, "P-DOS");
        var s1 = Stock(db, p, Ubicacion(db, "Pasillo A", "01"), 2);
        Stock(db, p, Ubicacion(db, "Pasillo A", "02"), 5);
        await Guardar(db);
        var sesion = await Recorrido(db);

        var r = await Leer(db, sesion, p.CodigoEAN, 2, s1.Id);

        r.Resultado.Should().Be(ResultadoLectura.Coincide);
        (await ConteoActual(db, s1.Id)).Estado.Should().Be(EstadoConteoCiclico.Completado);
    }

    [Fact]
    public async Task Lectura_StockFisicoFueraDeAlcance_PideUbicacion()
    {
        using var db = Db();
        var p = Producto(db, "P-TRES");
        Stock(db, p, Ubicacion(db, "Pasillo A", "01"), 2);
        Stock(db, p, Ubicacion(db, "Pasillo A", "02"), 5);
        var enB = Stock(db, p, Ubicacion(db, "Pasillo B"), 9);
        await Guardar(db);
        var sesion = await Recorrido(db);

        var r = await Leer(db, sesion, p.CodigoEAN, 9, enB.Id);

        r.Resultado.Should().Be(ResultadoLectura.ElegirUbicacion);
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Lectura_AjustePendiente_BloqueadoPorAjuste_NoCreaConteo()
    {
        using var db = Db();
        var (p, s) = await UnProducto(db);
        db.AjustesInventario.Add(new AjusteInventario
        {
            Id = Guid.NewGuid(), StockFisicoId = s.Id, ProductoId = p.Id, Tipo = TipoAjusteInventario.Negativo,
            Cantidad = 1, Motivo = "Prueba", Estado = EstadoAjusteInventario.PendienteAprobacion
        });
        await Guardar(db);
        var sesion = await Recorrido(db);

        var r = await Leer(db, sesion, p.CodigoEAN, 5);

        r.Resultado.Should().Be(ResultadoLectura.BloqueadoPorAjuste);
        r.Mensaje.Should().Be("Tiene un ajuste pendiente de aprobación; no se puede contar.");
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Lectura_YaCoincidio_YaContado()
    {
        using var db = Db();
        var (p, _) = await UnProducto(db);
        var sesion = await Recorrido(db);
        await Leer(db, sesion, p.CodigoEAN, 5);

        var r = await Leer(db, sesion, p.CodigoEAN, 7);

        r.Resultado.Should().Be(ResultadoLectura.YaContado);
        r.Mensaje.Should().Be("Ya contado en esta sesión.");
    }

    [Fact]
    public async Task Lectura_StockCambio_CancelaYReiniciaConNuevaFoto()
    {
        using var db = Db();
        var (p, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        await Leer(db, sesion, p.CodigoEAN, 3); // RequiereReconteo con foto 5
        db.ChangeTracker.Clear();
        var stock = await db.StockFisico.SingleAsync(x => x.Id == s.Id);
        stock.CantidadTotal = 8; // hubo un movimiento mientras se contaba
        await Guardar(db);

        var r = await Leer(db, sesion, p.CodigoEAN, 8);

        r.Resultado.Should().Be(ResultadoLectura.StockCambio);
        r.Mensaje.Should().Be("El stock cambió mientras contabas: vuelve a escanearlo.");
        db.ChangeTracker.Clear();
        var conteos = await db.ConteosCiclicos.Where(c => c.StockFisicoId == s.Id).ToListAsync();
        conteos.Should().ContainSingle(c => c.Estado == EstadoConteoCiclico.Cancelado);
        var nuevo = conteos.Single(c => c.Estado == EstadoConteoCiclico.EnConteo);
        nuevo.CantidadSistema.Should().Be(8);
        nuevo.SesionConteoId.Should().Be(sesion);
    }

    [Fact]
    public async Task Lectura_CodigoInvalido_YProductoNoEncontrado_NoGuardanLectura()
    {
        using var db = Db();
        await UnProducto(db);
        var sesion = await Recorrido(db);

        var invalido = await Leer(db, sesion, "7410000000191", 5);
        var inexistente = await Leer(db, sesion, CodigoBarras.GenerarInterno(9_000_000_000), 5);

        invalido.Resultado.Should().Be(ResultadoLectura.CodigoInvalido);
        invalido.Mensaje.Should().Be("Código inválido: vuelve a escanear.");
        inexistente.Resultado.Should().Be(ResultadoLectura.ProductoNoEncontrado);
        inexistente.Mensaje.Should().Be("No existe un producto con ese código.");
        (await db.LecturasConteo.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Lectura_CodigoConEspaciosYSaltoDeLinea_SeRecorta()
    {
        using var db = Db();
        var (p, _) = await UnProducto(db);
        var sesion = await Recorrido(db);

        (await Leer(db, sesion, $" {p.CodigoEAN}\r\n", 5)).Resultado.Should().Be(ResultadoLectura.Coincide);
    }

    [Fact]
    public async Task Lectura_SesionCerrada_SesionNoAbierta()
    {
        using var db = Db();
        var (p, _) = await UnProducto(db);
        var sesion = await Recorrido(db);
        db.ChangeTracker.Clear();
        (await db.SesionesConteo.SingleAsync()).Estado = EstadoSesionConteo.Cerrada;
        await db.SaveChangesAsync();

        var r = await Leer(db, sesion, p.CodigoEAN, 5);

        r.Resultado.Should().Be(ResultadoLectura.SesionNoAbierta);
        r.Mensaje.Should().Be("La sesión ya no está abierta.");
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Lectura_SesionInexistente_Lanza()
    {
        using var db = Db();
        var (p, _) = await UnProducto(db);
        await FluentActions.Invoking(() => Leer(db, Guid.NewGuid(), p.CodigoEAN, 5))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Lectura_MismoLecturaId_NoCuentaDoble()
    {
        using var db = Db();
        var (p, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        var id = Guid.NewGuid();

        var primera = await Leer(db, sesion, p.CodigoEAN, 3, lecturaId: id);
        var reintento = await Leer(db, sesion, p.CodigoEAN, 3, lecturaId: id);

        primera.Resultado.Should().Be(ResultadoLectura.Diferencia);
        reintento.Should().BeEquivalentTo(primera);
        (await ConteoActual(db, s.Id)).Estado.Should().Be(EstadoConteoCiclico.RequiereReconteo);
        (await db.AjustesInventario.AnyAsync()).Should().BeFalse();
        (await db.LecturasConteo.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Lectura_MismoIdOtroCodigo_RechazadoSinFiltrarProductoOriginal()
    {
        using var db = Db();
        var (a, _) = await UnProducto(db);
        var (b, _) = await UnProducto(db);
        var sesion = await Recorrido(db);
        var id = Guid.NewGuid();
        await Leer(db, sesion, a.CodigoEAN, 5, lecturaId: id);

        var r = await Leer(db, sesion, b.CodigoEAN, 1, lecturaId: id);

        r.Resultado.Should().Be(ResultadoLectura.Rechazado);
        r.Sku.Should().BeNull(); // un UUID reusado no puede exponer la lectura original
        (await db.LecturasConteo.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Lectura_CantidadNegativa_Lanza()
    {
        using var db = Db();
        var (p, _) = await UnProducto(db);
        var sesion = await Recorrido(db);

        await FluentActions.Invoking(() => Leer(db, sesion, p.CodigoEAN, -1))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Lectura_Planificada_ProductoFueraDeLaLista_FueraDeLugar()
    {
        using var db = Db();
        var (planificado, s) = await UnProducto(db);
        var (otro, _) = await UnProducto(db);
        await Conteos(db).PlanificarAsync(s.Id, null, "supervisor");
        db.ChangeTracker.Clear();
        var sesion = (await Sut(db).AbrirPlanificadaAsync(null, Usuario)).Sesion!.Id;

        (await Leer(db, sesion, otro.CodigoEAN, 1)).Resultado.Should().Be(ResultadoLectura.FueraDeLugar);
        (await Leer(db, sesion, planificado.CodigoEAN, 5)).Resultado.Should().Be(ResultadoLectura.Coincide);
        (await db.HallazgosConteo.SingleAsync()).Pasillo.Should().Be("(planificado)");
    }

    [Fact]
    public void Lectura_Resultado_NoExponeCantidadDelSistema() =>
        typeof(ResultadoLecturaDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Cantidad") || n.Contains("Stock"));
}
