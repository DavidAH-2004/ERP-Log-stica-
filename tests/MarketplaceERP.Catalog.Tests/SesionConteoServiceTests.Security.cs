using FluentAssertions;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

public partial class SesionConteoServiceTests
{
    [Fact]
    public async Task F404_ReintentoMismoIdOtraCantidad_RechazaSinDuplicar()
    {
        using var db = Db();
        var (producto, stock) = await UnProducto(db);
        var sesion = await Recorrido(db);
        var lecturaId = Guid.NewGuid();

        (await Leer(db, sesion, producto.CodigoEAN, 5, lecturaId: lecturaId))
            .Resultado.Should().Be(ResultadoLectura.Coincide);
        var alterada = await Leer(db, sesion, producto.CodigoEAN, 0, lecturaId: lecturaId);
        alterada.Resultado.Should().Be(ResultadoLectura.Rechazado);
        alterada.Sku.Should().BeNull();
        (await db.LecturasConteo.CountAsync()).Should().Be(1);
        (await db.ConteosCiclicos.CountAsync(c => c.StockFisicoId == stock.Id)).Should().Be(1);
    }

    [Fact]
    public async Task F404_ProductoFueraDeLugar_ReintentoNoGeneraHallazgoDoble()
    {
        using var db = Db();
        await UnProducto(db, "Pasillo A");
        var (fuera, _) = await UnProducto(db, "Pasillo B");
        var sesion = await Recorrido(db, "Pasillo A");
        var lecturaId = Guid.NewGuid();

        (await Leer(db, sesion, fuera.CodigoEAN, 4, lecturaId: lecturaId))
            .Resultado.Should().Be(ResultadoLectura.FueraDeLugar);
        (await Leer(db, sesion, fuera.CodigoEAN, 4, lecturaId: lecturaId))
            .Resultado.Should().Be(ResultadoLectura.FueraDeLugar);
        var alterada = await Leer(db, sesion, fuera.CodigoEAN, 9, lecturaId: lecturaId);
        alterada.Resultado.Should().Be(ResultadoLectura.Rechazado);
        alterada.Sku.Should().BeNull();

        (await db.HallazgosConteo.CountAsync()).Should().Be(1);
        (await db.LecturasConteo.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task F404_ReintentoMismoIdOtroStockFisico_RechazaSinModificar()
    {
        using var db = Db();
        var producto = Producto(db, "F404-DOS");
        var primera = Stock(db, producto, Ubicacion(db, "Pasillo A", "01"), 5);
        var segunda = Stock(db, producto, Ubicacion(db, "Pasillo A", "02"), 2);
        await Guardar(db);
        var sesion = await Recorrido(db);
        var lecturaId = Guid.NewGuid();

        (await Leer(db, sesion, producto.CodigoEAN, 5, stockFisicoId: primera.Id, lecturaId: lecturaId))
            .Resultado.Should().Be(ResultadoLectura.Coincide);
        var alterada = await Leer(db, sesion, producto.CodigoEAN, 5,
            stockFisicoId: segunda.Id, lecturaId: lecturaId);
        alterada.Resultado.Should().Be(ResultadoLectura.Rechazado);
        alterada.Sku.Should().BeNull();
        (await db.LecturasConteo.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task F404_CodigoNormalizado_ReintentoMantieneIdempotencia()
    {
        using var db = Db();
        var (p, _) = await UnProducto(db);
        var sesion = await Recorrido(db);
        var lecturaId = Guid.NewGuid();

        var first = await Leer(db, sesion, p.CodigoEAN, 5, lecturaId: lecturaId);
        var retry = await Leer(db, sesion, $" \t{p.CodigoEAN}\n", 5, lecturaId: lecturaId);

        retry.Should().BeEquivalentTo(first);
        (await db.LecturasConteo.CountAsync()).Should().Be(1);
    }
}
