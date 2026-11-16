using System.Security.Claims;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class ProductosSkuUnicidadTests
{
    private static CatalogDbContext Db() => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseInMemoryDatabase("sku-unicidad-" + Guid.NewGuid()).Options);

    private static ProductosController Controller(CatalogDbContext db, string role = "Admin", Guid? seller = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (seller.HasValue) claims.Add(new Claim("VendedorId", seller.Value.ToString()));
        return new ProductosController(db, Mock.Of<ICatalogImportService>(),
            NullLogger<ProductosController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
                }
            }
        };
    }

    private static Producto ProductoConSku(string sku, Guid? vendedorId = null) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Nombre = "Producto de prueba",
        CategoriaNombre = "Categoría de prueba",
        Subcategoria = "General",
        PrecioB2C = 10m,
        PrecioB2B = 8m,
        ProveedorNombre = "Proveedor de prueba",
        VendedorId = vendedorId ?? Guid.NewGuid(),
        CategoriaId = Guid.NewGuid(),
        CodigoEAN = null,
        EstaPublicado = true
    };

    private static ActualizarProductoRequest Actualizacion(Producto p, string sku) => new(
        sku, p.Nombre, p.Descripcion, p.CategoriaNombre, p.Subcategoria,
        p.PrecioB2C, p.PrecioB2B, p.StockMinimo, p.ProveedorNombre,
        p.PaisOrigen, p.PesoKg, p.DimensionesCm, p.UnidadMedida,
        p.EstadoProducto, p.EstaPublicado);

    [Fact]
    public async Task Disponible_ConsultaTodoElCatalogo_SinImportarVendedorNiPublicacion()
    {
        await using var db = Db();
        var ajeno = ProductoConSku(" ALM-0001 ");
        ajeno.EstaPublicado = false;
        db.Productos.Add(ajeno);
        await db.SaveChangesAsync();
        var ctl = Controller(db);

        var ocupado = Assert.IsType<OkObjectResult>(await ctl.SkuDisponible("alm-0001"));
        Assert.False(JsonDocument.Parse(JsonSerializer.Serialize(ocupado.Value))
            .RootElement.GetProperty("disponible").GetBoolean());
        var libre = Assert.IsType<OkObjectResult>(await ctl.SkuDisponible("ALM-0002"));
        Assert.True(JsonDocument.Parse(JsonSerializer.Serialize(libre.Value))
            .RootElement.GetProperty("disponible").GetBoolean());
        Assert.IsType<BadRequestObjectResult>(await ctl.SkuDisponible("  "));
    }

    [Fact]
    public async Task Disponible_ExcluyeElPropioYNoPermiteExclusionesAjenas()
    {
        await using var db = Db();
        var vendedor = Guid.NewGuid();
        var producto = ProductoConSku("SKU-EXISTENTE", vendedor);
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var ctl = Controller(db, "Vendedor", vendedor);
        var propio = Assert.IsType<OkObjectResult>(
            await ctl.SkuDisponible("SKU-EXISTENTE", producto.Id));
        Assert.True(JsonDocument.Parse(JsonSerializer.Serialize(propio.Value))
            .RootElement.GetProperty("disponible").GetBoolean());
        Assert.IsType<ForbidResult>(await Controller(db, "Vendedor", Guid.NewGuid())
            .SkuDisponible("SKU-EXISTENTE", producto.Id));
    }

    [Fact]
    public async Task Alta_RechazaDuplicadoNormalizado_YConservaDatoOriginal()
    {
        await using var db = Db();
        var existente = ProductoConSku("ALM-0001");
        db.Productos.Add(existente);
        await db.SaveChangesAsync();

        var ctl = Controller(db);
        var nuevo = ProductoConSku(" alm-0001 ");
        Assert.IsType<ConflictObjectResult>(await ctl.Create(nuevo));
        Assert.Equal(1, await db.Productos.CountAsync());
        Assert.Equal("ALM-0001", existente.Sku);
    }

    [Fact]
    public async Task Alta_AdmiteCodigoNuevoNormalizado()
    {
        await using var db = Db();
        var ctl = Controller(db);
        var nuevo = ProductoConSku(" NUEVO-0001 ");
        Assert.IsType<CreatedAtActionResult>(await ctl.Create(nuevo));
        var persistido = Assert.Single(await db.Productos.ToListAsync());
        Assert.Equal("NUEVO-0001", persistido.Sku);
        Assert.Null(persistido.CodigoEAN);
    }

    [Fact]
    public async Task Edicion_PermiteCodigoPropioIntacto_AunConHistoricoDuplicado()
    {
        await using var db = Db();
        var producto = ProductoConSku("ALM-0001");
        var historico = ProductoConSku("alm-0001");
        db.Productos.AddRange(producto, historico);
        await db.SaveChangesAsync();
        var ctl = Controller(db);

        Assert.IsType<OkObjectResult>(await ctl.Update(producto.Id,
            Actualizacion(producto, "ALM-0001")));
        Assert.Equal("ALM-0001", producto.Sku);
        Assert.Equal("alm-0001", historico.Sku);
    }

    [Fact]
    public async Task Edicion_RechazaSkuAjeno_YNoModificaNingunProducto()
    {
        await using var db = Db();
        var uno = ProductoConSku("SKU-0001");
        var otro = ProductoConSku("SKU-0002");
        db.Productos.AddRange(uno, otro);
        await db.SaveChangesAsync();
        var ctl = Controller(db);

        Assert.IsType<ConflictObjectResult>(await ctl.Update(uno.Id,
            Actualizacion(uno, " sku-0002 ")));
        Assert.Equal("SKU-0001", uno.Sku);
        Assert.Equal("SKU-0002", otro.Sku);
    }
}
