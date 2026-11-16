using System.Security.Claims;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>Referencias reales para selecciones; no conecta servidores ni procesa pagos.</summary>
public sealed class SelectoresReferenciasFase2Tests
{
    private static FinanceDbContext Finance() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("f2-fin-" + Guid.NewGuid()).Options);

    private static CatalogDbContext Catalog() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("f2-cat-" + Guid.NewGuid()).Options);

    private static CoreDbContext Core() => new(
        new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("f2-core-" + Guid.NewGuid()).Options);

    private static JsonElement ComoJson(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public async Task ProveedoresDisponibles_NoIncluyeInactivos()
    {
        await using var catalog = Catalog();
        await using var core = Core();
        core.Proveedores.AddRange(
            new Proveedor { Id = Guid.NewGuid(), NombreComercial = "Activo", Activo = true },
            new Proveedor { Id = Guid.NewGuid(), NombreComercial = "Inactivo", Activo = false });
        await core.SaveChangesAsync();

        var controller = new ProductosController(catalog, Mock.Of<ICatalogImportService>(),
            NullLogger<ProductosController>.Instance, core);
        var respuesta = ComoJson(await controller.ProveedoresDisponibles(CancellationToken.None));
        var nombres = respuesta.EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(new[] { "Activo" }, nombres);
    }

    [Fact]
    public async Task OrdenesParaEmpaque_SoloPickingCompletoYSinPaquete()
    {
        await using var finance = Finance();
        var lista = new Orden { Id = Guid.NewGuid(), NumeroOrden = "ORD-ELEGIBLE", Estado = EstadoOrden.EnProceso };
        var pendiente = new Orden { Id = Guid.NewGuid(), NumeroOrden = "ORD-PENDIENTE", Estado = EstadoOrden.EnProceso };
        var empacada = new Orden { Id = Guid.NewGuid(), NumeroOrden = "ORD-EMPACADA", Estado = EstadoOrden.EnProceso };
        finance.Ordenes.AddRange(lista, pendiente, empacada);
        await finance.SaveChangesAsync();
        var wms = new Mock<IFulfillmentService>();
        wms.Setup(s => s.ListarPickListsAsync(EstadoPicking.Completado, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] {
                new PickList { OrdenId = lista.Id, Estado = EstadoPicking.Completado },
                new PickList { OrdenId = empacada.Id, Estado = EstadoPicking.Completado }
            });
        wms.Setup(s => s.ListarPaquetesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new PaqueteDespacho { OrdenId = empacada.Id } });
        var controller = new FulfillmentController(wms.Object, finance);
        var respuesta = ComoJson(await controller.OrdenesParaEmpaque(null, 1, 25, CancellationToken.None));
        Assert.Equal(1, respuesta.GetProperty("total").GetInt32());
        Assert.Equal("ORD-ELEGIBLE", respuesta.GetProperty("items")[0].GetProperty("numeroOrden").GetString());
    }

    [Fact]
    public async Task Rma_SoloOrdenEntregadaYSaldoDevolvible()
    {
        await using var finance = Finance();
        await using var catalog = Catalog();
        var productoId = Guid.NewGuid();
        var entregada = new Orden { Id = Guid.NewGuid(), NumeroOrden = "ORD-ENTREGADA", Estado = EstadoOrden.Entregada };
        var pendiente = new Orden { Id = Guid.NewGuid(), NumeroOrden = "ORD-NO-ENTREGADA", Estado = EstadoOrden.Pendiente };
        entregada.Items.Add(new ItemOrden { Id = Guid.NewGuid(), OrdenId = entregada.Id,
            ProductoId = productoId, SkuReferencia = "SKU-1", NombreProducto = "Producto real", Cantidad = 3 });
        finance.Ordenes.AddRange(entregada, pendiente);
        await finance.SaveChangesAsync();
        catalog.DevolucionesRMA.Add(new DevolucionRMA { Id = Guid.NewGuid(), NumeroRMA = "RMA-TEST",
            OrdenId = entregada.Id, ProductoId = productoId, Cantidad = 2, Estado = EstadoRMA.Solicitado });
        await catalog.SaveChangesAsync();
        var controller = new RmaController(Mock.Of<IRmaService>(),
            NullLogger<RmaController>.Instance, finance, catalog);
        var ordenes = ComoJson(await controller.OrdenesElegibles(null, 1, 25, CancellationToken.None));
        Assert.Equal(1, ordenes.GetProperty("total").GetInt32());
        var resultado = ComoJson(await controller.ProductosDevolvibles(entregada.Id, CancellationToken.None));
        Assert.Equal(1, resultado.GetProperty("items")[0].GetProperty("cantidadDisponible").GetInt32());
        Assert.IsType<NotFoundObjectResult>(
            await controller.ProductosDevolvibles(pendiente.Id, CancellationToken.None));
    }
}
