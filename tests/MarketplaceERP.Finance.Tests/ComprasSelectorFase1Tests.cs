using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class ComprasSelectorFase1Tests
{
    private static (CoreDbContext core, FinanceDbContext finance, CatalogDbContext catalog,
        ComprasController controller, Guid proveedorId) Crear()
    {
        var unico = Guid.NewGuid().ToString("N");
        var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("selector-core-" + unico).Options);
        var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("selector-finance-" + unico).Options);
        var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("selector-catalog-" + unico).Options);
        var proveedorId = Guid.NewGuid();
        core.Proveedores.Add(new Proveedor
        {
            Id = proveedorId, RazonSocial = "Proveedor registrado",
            NombreComercial = "Proveedor registrado", Activo = true
        });
        core.SaveChanges();
        var controller = new ComprasController(finance,
            Mock.Of<ITresWayMatchingService>(), Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(), core, NullLogger<ComprasController>.Instance, catalog);
        return (core, finance, catalog, controller, proveedorId);
    }

    private static Producto Producto(string sku, string nombre, string estado = "Activo") => new()
    {
        Id = Guid.NewGuid(), Sku = sku, Nombre = nombre, EstadoProducto = estado,
        CategoriaNombre = "Alimentos", Subcategoria = "Pruebas", PrecioB2C = 20m,
        PrecioB2B = 18m, VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid(),
        EstaPublicado = false // Se puede comprar un producto activo aún no publicado.
    };

    [Fact]
    public async Task Selector_BuscaProductosActivos_YPrecioHistoricoSinConfundirConVenta()
    {
        var (core, finance, catalog, controller, proveedorId) = Crear();
        await using var _1 = core;
        await using var _2 = finance;
        await using var _3 = catalog;

        var activo = Producto("ALM-9001", "Arroz maestro");
        var sinCompras = Producto("ALM-9002", "Arroz recién registrado");
        var inactivo = Producto("ALM-9003", "Arroz inactivo", "Inactivo");
        catalog.Productos.AddRange(activo, sinCompras, inactivo);
        await catalog.SaveChangesAsync();

        var antigua = new OrdenCompra
        {
            Id = Guid.NewGuid(), NumeroPO = "PO-ANTIGUA", ProveedorId = proveedorId,
            ProveedorNombre = "Proveedor registrado", Estado = EstadoOrdenCompra.Enviada,
            FechaEmision = DateTime.UtcNow.AddDays(-3)
        };
        antigua.Items.Add(new ItemOrdenCompra
        {
            Id = Guid.NewGuid(), ProductoId = activo.Id, Sku = activo.Sku,
            NombreProducto = activo.Nombre, CantidadSolicitada = 1, PrecioUnitarioAcordado = 6.50m
        });
        var reciente = new OrdenCompra
        {
            Id = Guid.NewGuid(), NumeroPO = "PO-RECIENTE", ProveedorId = proveedorId,
            ProveedorNombre = "Proveedor registrado", Estado = EstadoOrdenCompra.Enviada,
            FechaEmision = DateTime.UtcNow.AddDays(-1)
        };
        reciente.Items.Add(new ItemOrdenCompra
        {
            Id = Guid.NewGuid(), ProductoId = activo.Id, Sku = activo.Sku,
            NombreProducto = activo.Nombre, CantidadSolicitada = 1, PrecioUnitarioAcordado = 7.25m
        });
        var cancelada = new OrdenCompra
        {
            Id = Guid.NewGuid(), NumeroPO = "PO-CANCELADA", ProveedorId = proveedorId,
            ProveedorNombre = "Proveedor registrado", Estado = EstadoOrdenCompra.Cancelada,
            FechaEmision = DateTime.UtcNow
        };
        cancelada.Items.Add(new ItemOrdenCompra
        {
            Id = Guid.NewGuid(), ProductoId = activo.Id, Sku = activo.Sku,
            NombreProducto = activo.Nombre, CantidadSolicitada = 1, PrecioUnitarioAcordado = 99.00m
        });
        finance.OrdenesCompra.AddRange(antigua, reciente, cancelada);
        await finance.SaveChangesAsync();

        var result = await controller.ProductosDisponibles("arroz", 1, 25, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var respuesta = Assert.IsType<ProductosParaCompraResponse>(ok.Value);
        Assert.Equal(2, respuesta.Total);
        Assert.Equal(2, respuesta.Items.Count);
        Assert.Equal(7.25m, respuesta.Items.Single(x => x.Sku == activo.Sku).PrecioCompraReferencia);
        Assert.Null(respuesta.Items.Single(x => x.Sku == sinCompras.Sku).PrecioCompraReferencia);
        Assert.DoesNotContain(respuesta.Items, x => x.Sku == inactivo.Sku);
    }

    [Fact]
    public async Task CrearPO_UsaNombreMaestro_YConservaPrecioEditado()
    {
        var (core, finance, catalog, controller, proveedorId) = Crear();
        await using var _1 = core;
        await using var _2 = finance;
        await using var _3 = catalog;

        var producto = Producto("SKU-OFICIAL", "Nombre maestro del producto");
        catalog.Productos.Add(producto);
        await catalog.SaveChangesAsync();
        var solicitud = new CrearPORequest(proveedorId, "Nombre externo",
            null, null, null, false,
            new List<ItemPORequest> { new(producto.Sku, "Nombre falso introducido", 3, 4.50m) });

        var result = await controller.CrearPO(solicitud, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(result);
        var item = await finance.ItemsOrdenCompra.SingleAsync();
        Assert.Equal(producto.Id, item.ProductoId);
        Assert.Equal(producto.Nombre, item.NombreProducto);
        Assert.Equal(4.50m, item.PrecioUnitarioAcordado);
        Assert.Equal(3, item.CantidadSolicitada);
    }

    [Fact]
    public async Task CrearPO_RechazaProductoInactivoAunqueEnVioSkuManual()
    {
        var (core, finance, catalog, controller, proveedorId) = Crear();
        await using var _1 = core;
        await using var _2 = finance;
        await using var _3 = catalog;
        var producto = Producto("SKU-BAJA", "No comprable", "Inactivo");
        catalog.Productos.Add(producto);
        await catalog.SaveChangesAsync();

        var result = await controller.CrearPO(new CrearPORequest(proveedorId,
            "Proveedor", null, null, null, false,
            new List<ItemPORequest> { new(producto.Sku, producto.Nombre, 1, 10m) }),
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await finance.OrdenesCompra.ToListAsync());
    }

    [Fact]
    public async Task Selector_PaginaResultadosSinLimitarLaBusquedaAlPrimerLote()
    {
        var (core, finance, catalog, controller, _) = Crear();
        await using var _1 = core;
        await using var _2 = finance;
        await using var _3 = catalog;
        for (var i = 1; i <= 54; i++)
            catalog.Productos.Add(Producto($"SKU-{i:000}", "Producto " + i));
        await catalog.SaveChangesAsync();

        var result = await controller.ProductosDisponibles("SKU-", 3, 25, CancellationToken.None);
        var datos = Assert.IsType<ProductosParaCompraResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(54, datos.Total);
        Assert.Equal(4, datos.Items.Count);
        Assert.Equal("SKU-051", datos.Items[0].Sku);
    }
}
