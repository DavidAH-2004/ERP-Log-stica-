using FluentAssertions;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>Casos encontrados en la revisión final (I2, I3, I4).</summary>
public partial class SesionConteoServiceTests
{
    [Fact]
    public async Task Lectura_ReconteoConAjusteImposible_DevuelveRechazadoSinError()
    {
        // I2: total 10 con 4 reservadas (disponible 6); contar 2 y 2 exige un ajuste de −8 que la regla rechaza.
        using var db = Db();
        var p = Producto(db, "P-RES");
        var s = Stock(db, p, Ubicacion(db, "Pasillo A"), 10);
        s.CantidadReservada = 4;
        await Guardar(db);
        var sesion = await Recorrido(db);
        await Leer(db, sesion, p.CodigoEAN, 2);

        var r = await Leer(db, sesion, p.CodigoEAN, 2);

        r.Resultado.Should().Be(ResultadoLectura.Rechazado);
        r.Mensaje.Should().Contain("excede");
        (await ConteoActual(db, s.Id)).Estado.Should().Be(EstadoConteoCiclico.RequiereReconteo);
        (await db.AjustesInventario.AnyAsync()).Should().BeFalse();
        (await db.LecturasConteo.CountAsync(l => l.Resultado == ResultadoLectura.Rechazado)).Should().Be(1);
    }

    [Fact]
    public async Task Resumen_Planificada_ConteoCompletadoFueraDeLaSesion_NoEstaEnFaltan()
    {
        // I3: el conteo de la sesión se completó en la página clásica.
        using var db = Db();
        var (_, s) = await UnProducto(db, total: 5);
        var conteos = Conteos(db);
        var id = (await conteos.PlanificarAsync(s.Id, null, "supervisor")).Conteo!.Id;
        db.ChangeTracker.Clear();
        var sesion = (await Sut(db).AbrirPlanificadaAsync(null, Usuario)).Sesion!.Id;
        db.ChangeTracker.Clear();
        await conteos.IniciarAsync(id, "otro");
        await conteos.RegistrarPrimerConteoAsync(id, 5, "otro");
        db.ChangeTracker.Clear();

        var resumen = await Sut(db).ObtenerResumenAsync(sesion);

        resumen!.Faltan.Should().BeEmpty();
        resumen.Contados.Should().ContainSingle(c => c.StockFisicoId == s.Id);
        resumen.Sesion.Contados.Should().Be(1);
    }

    [Fact]
    public async Task Resumen_DiferenciaLuegoStockCambio_VuelveAFaltan()
    {
        // I4: tras Diferencia + StockCambio el conteo vuelve a EnConteo; debe reaparecer en Faltan.
        using var db = Db();
        var (p, s) = await UnProducto(db, total: 5);
        var sesion = await Recorrido(db);
        await Leer(db, sesion, p.CodigoEAN, 3);
        db.ChangeTracker.Clear();
        (await db.StockFisico.SingleAsync(x => x.Id == s.Id)).CantidadTotal = 8;
        await Guardar(db);
        (await Leer(db, sesion, p.CodigoEAN, 8)).Resultado.Should().Be(ResultadoLectura.StockCambio);
        db.ChangeTracker.Clear();

        var resumen = await Sut(db).ObtenerResumenAsync(sesion);

        resumen!.Faltan.Should().ContainSingle(f => f.StockFisicoId == s.Id);
    }

    [Fact]
    public async Task Cerrar_DecisionRepetida_Falla()
    {
        using var db = Db();
        var (_, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        db.ChangeTracker.Clear();

        var r = await Sut(db).CerrarAsync(sesion, new[]
        {
            new DecisionCierre(s.Id, AccionCierre.NoHay),
            new DecisionCierre(s.Id, AccionCierre.NoHay),
        }, Usuario);

        r.Exitoso.Should().BeFalse();
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }
}
