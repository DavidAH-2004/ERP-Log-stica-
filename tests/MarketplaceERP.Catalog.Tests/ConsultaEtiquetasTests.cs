using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

public class ConsultaEtiquetasTests
{
    private static Producto P(string sku, string ean, bool publicado = true) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Nombre = $"Producto {sku}",
        CodigoEAN = ean,
        EstaPublicado = publicado,
        EstadoProducto = publicado ? "Activo" : "Inactivo",
        VendedorId = Guid.NewGuid(),
        CategoriaId = Guid.NewGuid()
    };

    private static async Task<CatalogDbContext> BaseCon(params Producto[] productos)
    {
        var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"Etiquetas_{Guid.NewGuid()}").Options);
        db.Productos.AddRange(productos);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task SinIds_DevuelveSoloActivosConCodigo_OrdenadosPorSku()
    {
        using var db = await BaseCon(
            P("B-1", CodigoBarras.GenerarInterno(2)),
            P("A-1", CodigoBarras.GenerarInterno(1)),
            P("C-1", CodigoBarras.GenerarInterno(3), publicado: false),
            P("D-1", ""));

        var etiquetas = await ConsultaEtiquetas.ObtenerAsync(db, null);

        etiquetas.Select(e => e.Sku).Should().Equal("A-1", "B-1");
        etiquetas[0].Should().Be(new EtiquetaProducto(etiquetas[0].Id, "A-1", "Producto A-1", CodigoBarras.GenerarInterno(1)));
    }

    [Fact]
    public async Task ConIds_DevuelveEsosProductos_IncluidosInactivosYSinCodigo()
    {
        var inactivo = P("C-1", CodigoBarras.GenerarInterno(3), publicado: false);
        var sinCodigo = P("D-1", "");
        using var db = await BaseCon(P("A-1", CodigoBarras.GenerarInterno(1)), inactivo, sinCodigo);

        var etiquetas = await ConsultaEtiquetas.ObtenerAsync(db, new[] { sinCodigo.Id, inactivo.Id });

        etiquetas.Select(e => e.Sku).Should().Equal("C-1", "D-1");
    }

    [Fact]
    public async Task ConIdsInexistentes_LosIgnora()
    {
        var a = P("A-1", CodigoBarras.GenerarInterno(1));
        using var db = await BaseCon(a);

        var etiquetas = await ConsultaEtiquetas.ObtenerAsync(db, new[] { a.Id, Guid.NewGuid() });

        etiquetas.Should().ContainSingle().Which.Sku.Should().Be("A-1");
    }
}
