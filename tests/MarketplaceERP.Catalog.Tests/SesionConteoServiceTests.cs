using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

public partial class SesionConteoServiceTests
{
    private const string Usuario = "operario@erp.local";
    private static long _consecutivo;

    private static CatalogDbContext Db() => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseInMemoryDatabase($"Sesiones_{Guid.NewGuid()}").Options);

    private static IConteoCiclicoService Conteos(CatalogDbContext db) =>
        new ConteoCiclicoService(db, new AjusteInventarioService(db, NullLogger<AjusteInventarioService>.Instance),
            NullLogger<ConteoCiclicoService>.Instance);

    private static SesionConteoService Sut(CatalogDbContext db) =>
        new(db, Conteos(db));

    private static UbicacionAlmacen Ubicacion(CatalogDbContext db, string pasillo, string posicion = "01")
    {
        var u = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(), Codigo = $"{pasillo.Replace(" ", "")}-{posicion}-{Guid.NewGuid():N}"[..20],
            Pasillo = pasillo, Rack = "01", Nivel = 1, Posicion = posicion,
            CapacidadMaxima = 1000, EstaOcupada = true, Zona = "General"
        };
        db.UbicacionesAlmacen.Add(u);
        return u;
    }

    private static Producto Producto(CatalogDbContext db, string sku)
    {
        var p = new Producto
        {
            Id = Guid.NewGuid(), Sku = sku, Nombre = $"Producto {sku}", CategoriaNombre = "Pruebas",
            PrecioB2C = 10, PrecioB2B = 8, StockMinimo = 2, VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid(),
            EstadoProducto = "Activo", EstaPublicado = true,
            CodigoEAN = CodigoBarras.GenerarInterno(Interlocked.Increment(ref _consecutivo))
        };
        db.Productos.Add(p);
        return p;
    }

    private static StockFisico Stock(CatalogDbContext db, Producto p, UbicacionAlmacen u, int total)
    {
        var s = new StockFisico
        {
            Id = Guid.NewGuid(), ProductoId = p.Id, CantidadTotal = total, PuntoReorden = 2,
            UbicacionAlmacenId = u.Id, UbicacionCodigo = u.Codigo
        };
        db.StockFisico.Add(s);
        return s;
    }

    private static async Task Guardar(CatalogDbContext db)
    {
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // ── Task 2: apertura, listado, resumen ──────────────────────────────────

    [Fact]
    public async Task AbrirRecorrido_CreaSesionNumeradaYEsperadosDelPasillo()
    {
        using var db = Db();
        var a = Ubicacion(db, "Pasillo A");
        var a2 = Ubicacion(db, "Pasillo A", "02");
        var b = Ubicacion(db, "Pasillo B");
        Stock(db, Producto(db, "P-1"), a, 5);
        Stock(db, Producto(db, "P-2"), a2, 0);
        Stock(db, Producto(db, "P-3"), b, 5);
        await Guardar(db);

        var r = await Sut(db).AbrirRecorridoAsync("Pasillo A", Usuario);

        r.Exitoso.Should().BeTrue();
        r.Sesion!.Numero.Should().StartWith($"SC-{DateTime.UtcNow.Year}-");
        r.Sesion.Modo.Should().Be(ModoSesionConteo.Recorrido);
        r.Sesion.Estado.Should().Be(EstadoSesionConteo.Abierta);
        r.Sesion.CreadaPor.Should().Be(Usuario);
        r.Sesion.Esperados.Should().Be(1);
    }

    [Fact]
    public async Task AbrirRecorrido_PasilloYaAbierto_DevuelveNumeroExistente()
    {
        using var db = Db();
        Stock(db, Producto(db, "P-1"), Ubicacion(db, "Pasillo A"), 5);
        await Guardar(db);
        var primera = await Sut(db).AbrirRecorridoAsync("Pasillo A", Usuario);

        var segunda = await Sut(db).AbrirRecorridoAsync("Pasillo A", "otro@erp.local");

        segunda.Exitoso.Should().BeFalse();
        segunda.Mensaje.Should().Be("Ya hay un recorrido abierto en este pasillo.");
        segunda.NumeroSesionExistente.Should().Be(primera.Sesion!.Numero);
    }

    [Fact]
    public async Task AbrirRecorrido_PasilloInexistente_Falla()
    {
        using var db = Db();
        Ubicacion(db, "Pasillo A");
        await Guardar(db);

        var r = await Sut(db).AbrirRecorridoAsync("Pasillo Z", Usuario);

        r.Exitoso.Should().BeFalse();
        r.Mensaje.Should().Be("El pasillo no existe.");
    }

    [Fact]
    public async Task AbrirPlanificada_VinculaConteosPendientesSinSesion()
    {
        using var db = Db();
        var sA = Stock(db, Producto(db, "P-1"), Ubicacion(db, "Pasillo A"), 5);
        var sB = Stock(db, Producto(db, "P-2"), Ubicacion(db, "Pasillo B"), 5);
        await Guardar(db);
        var conteos = Conteos(db);
        await conteos.PlanificarAsync(sA.Id, null, "supervisor");
        await conteos.PlanificarAsync(sB.Id, null, "supervisor");
        db.ChangeTracker.Clear();

        var r = await Sut(db).AbrirPlanificadaAsync("Pasillo A", Usuario);

        r.Exitoso.Should().BeTrue();
        r.Sesion!.Modo.Should().Be(ModoSesionConteo.Planificado);
        r.Sesion.Esperados.Should().Be(1);
        db.ChangeTracker.Clear();
        (await db.ConteosCiclicos.SingleAsync(c => c.StockFisicoId == sA.Id)).SesionConteoId.Should().Be(r.Sesion.Id);
        (await db.ConteosCiclicos.SingleAsync(c => c.StockFisicoId == sB.Id)).SesionConteoId.Should().BeNull();
    }

    [Fact]
    public async Task AbrirPlanificada_SinPendientes_Falla()
    {
        using var db = Db();
        Stock(db, Producto(db, "P-1"), Ubicacion(db, "Pasillo A"), 5);
        await Guardar(db);

        var r = await Sut(db).AbrirPlanificadaAsync(null, Usuario);

        r.Exitoso.Should().BeFalse();
        r.Mensaje.Should().Be("No hay conteos planificados pendientes.");
    }

    [Fact]
    public async Task Numero_EsConsecutivoDentroDelAnio()
    {
        using var db = Db();
        Stock(db, Producto(db, "P-1"), Ubicacion(db, "Pasillo A"), 5);
        Stock(db, Producto(db, "P-2"), Ubicacion(db, "Pasillo B"), 5);
        await Guardar(db);

        var uno = await Sut(db).AbrirRecorridoAsync("Pasillo A", Usuario);
        var dos = await Sut(db).AbrirRecorridoAsync("Pasillo B", Usuario);

        uno.Sesion!.Numero.Should().Be($"SC-{DateTime.UtcNow.Year}-0001");
        dos.Sesion!.Numero.Should().Be($"SC-{DateTime.UtcNow.Year}-0002");
    }

    [Fact]
    public async Task Resumen_RecorridoNuevo_TodoEnFaltan_SinCantidadesDelSistema()
    {
        using var db = Db();
        var a = Ubicacion(db, "Pasillo A");
        Stock(db, Producto(db, "P-1"), a, 5);
        Stock(db, Producto(db, "P-2"), Ubicacion(db, "Pasillo A", "02"), 3);
        await Guardar(db);
        var sesion = (await Sut(db).AbrirRecorridoAsync("Pasillo A", Usuario)).Sesion!;

        var resumen = await Sut(db).ObtenerResumenAsync(sesion.Id);

        resumen!.Faltan.Select(f => f.Sku).Should().BeEquivalentTo("P-1", "P-2");
        resumen.Contados.Should().BeEmpty();
        resumen.Reconteo.Should().BeEmpty();
        resumen.FueraDeLugar.Should().BeEmpty();
        typeof(ItemSesionDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Cantidad"));
    }

    [Fact]
    public async Task Resumen_Planificada_ConteoEnReconteo_NoApareceEnFaltan()
    {
        using var db = Db();
        var s = Stock(db, Producto(db, "P-1"), Ubicacion(db, "Pasillo A"), 5);
        await Guardar(db);
        var conteos = Conteos(db);
        var id = (await conteos.PlanificarAsync(s.Id, null, "supervisor")).Conteo!.Id;
        await conteos.IniciarAsync(id, "supervisor");
        await conteos.RegistrarPrimerConteoAsync(id, 2, "supervisor"); // → RequiereReconteo
        db.ChangeTracker.Clear();

        var sesion = (await Sut(db).AbrirPlanificadaAsync(null, Usuario)).Sesion!;
        var resumen = await Sut(db).ObtenerResumenAsync(sesion.Id);

        resumen!.Reconteo.Select(r => r.Sku).Should().Equal("P-1");
        resumen.Faltan.Should().BeEmpty();
    }

    [Fact]
    public async Task Resumen_SesionInexistente_Null()
    {
        using var db = Db();
        (await Sut(db).ObtenerResumenAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ListarPasillos_DevuelveDistintosOrdenados()
    {
        using var db = Db();
        Ubicacion(db, "Pasillo B");
        Ubicacion(db, "Pasillo A");
        Ubicacion(db, "Pasillo A", "02");
        await Guardar(db);

        (await Sut(db).ListarPasillosAsync()).Should().Equal("Pasillo A", "Pasillo B");
    }
}
