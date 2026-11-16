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

/// <summary>
/// El orden del catálogo se resuelve EN LA API antes de Skip/Take.
/// Estas pruebas no acceden a PostgreSQL ni alteran datos reales.
/// </summary>
public class ProductosOrdenCronologicoTests
{
    private static CatalogDbContext Db() => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseInMemoryDatabase("catalog-orden-" + Guid.NewGuid()).Options);

    private static ProductosController Controller(CatalogDbContext db, string rol = "Admin", Guid? vendedor = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, rol), new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (vendedor.HasValue) claims.Add(new Claim("VendedorId", vendedor.Value.ToString()));
        return new ProductosController(db, new Mock<ICatalogImportService>().Object,
            NullLogger<ProductosController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"))
                }
            }
        };
    }

    private static Producto Producto(string sku, DateTime fecha, Guid vendedor, string categoria = "Alimentos",
        string nombre = "Prueba") => new()
    {
        Id = Guid.NewGuid(),
        VendedorId = vendedor,
        CategoriaId = Guid.NewGuid(),
        Sku = sku,
        Nombre = nombre,
        Descripcion = "Descripción de pruebas",
        CategoriaNombre = categoria,
        Subcategoria = "General",
        PrecioB2C = 25m,
        PrecioB2B = 20m,
        StockMinimo = 2,
        ProveedorNombre = "Proveedor de pruebas",
        PaisOrigen = "El Salvador",
        PesoKg = 1m,
        DimensionesCm = "10x10x10",
        UnidadMedida = "Unidad",
        EstadoProducto = "Activo",
        EstaPublicado = true,
        FechaCreacion = fecha
    };

    private static string[] Skus(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        return doc.RootElement.GetProperty("Items").EnumerateArray()
            .Select(item => item.GetProperty("Sku").GetString()!)
            .ToArray();
    }

    [Fact]
    public async Task FechaAntesDePaginacion_EvitaQueNuevosQuedenEnPaginasPosteriores()
    {
        await using var db = Db();
        var vendedor = Guid.NewGuid();
        var inicio = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Productos.AddRange(
            Producto("Z-ANTIGUO", inicio, vendedor),
            Producto("M-INTERMEDIO", inicio.AddDays(1), vendedor),
            Producto("B-NUEVO", inicio.AddDays(2), vendedor),
            Producto("A-NUEVO", inicio.AddDays(2), vendedor));
        await db.SaveChangesAsync();
        var controller = Controller(db);

        Assert.Equal(new[] { "A-NUEVO", "B-NUEVO" }, Skus(await controller.GetPublicados(1, 2, "recientes")));
        Assert.Equal(new[] { "M-INTERMEDIO", "Z-ANTIGUO" }, Skus(await controller.GetPublicados(2, 2, "recientes")));
        Assert.Equal(new[] { "Z-ANTIGUO", "M-INTERMEDIO" }, Skus(await controller.GetPublicados(1, 2, "antiguos")));
        Assert.Equal(new[] { "A-NUEVO", "B-NUEVO" }, Skus(await controller.GetPublicados(2, 2, "antiguos")));
        Assert.Equal(new[] { "A-NUEVO", "B-NUEVO", "M-INTERMEDIO", "Z-ANTIGUO" },
            Skus(await controller.GetPublicados(1, 50))); // Valor predeterminado preserva SKU.
    }

    [Fact]
    public async Task Buscar_RespetaFiltrosYOrdenCronologico()
    {
        await using var db = Db();
        var vendedor = Guid.NewGuid();
        var fecha = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Productos.AddRange(
            Producto("SKU-OLD", fecha, vendedor, "Alimentos", "Coincidente antiguo"),
            Producto("SKU-NEW", fecha.AddDays(3), vendedor, "Alimentos", "Coincidente nuevo"),
            Producto("SKU-OTHER", fecha.AddDays(4), vendedor, "Ferretería", "Coincidente otro"),
            Producto("SKU-NO", fecha.AddDays(5), vendedor, "Alimentos", "No coincide"));
        await db.SaveChangesAsync();

        var controller = Controller(db);
        var result = await controller.Buscar("Coincidente", "Alimentos", null, null, null, null, 1, 50, "recientes");
        Assert.Equal(new[] { "SKU-NEW", "SKU-OLD" }, Skus(result));
        result = await controller.Buscar("Coincidente", "Alimentos", null, null, null, null, 1, 50, "antiguos");
        Assert.Equal(new[] { "SKU-OLD", "SKU-NEW" }, Skus(result));
    }

    [Fact]
    public async Task Vendedor_NoPuedeVerProductosAjenosAunqueCambieOrden()
    {
        await using var db = Db();
        var propio = Guid.NewGuid();
        var ajeno = Guid.NewGuid();
        var fecha = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Productos.AddRange(
            Producto("OWN-A", fecha, propio),
            Producto("FOREIGN-LATEST", fecha.AddDays(10), ajeno));
        await db.SaveChangesAsync();

        var controller = Controller(db, "Vendedor", propio);
        Assert.Equal(new[] { "OWN-A" }, Skus(await controller.GetPublicados(1, 50, "recientes")));
        Assert.Equal(new[] { "OWN-A" },
            Skus(await controller.Buscar(null, null, null, null, null, null, 1, 50, "antiguos")));
    }

    [Theory]
    [InlineData("edad")]
    [InlineData("sku;DROP TABLE productos")]
    public async Task OrdenNoSoportado_Devuelve400(string orden)
    {
        await using var db = Db();
        var controller = Controller(db);
        Assert.IsType<BadRequestObjectResult>(await controller.GetPublicados(1, 50, orden));
        Assert.IsType<BadRequestObjectResult>(await controller.Buscar(null, null, null, null, null, null, 1, 50, orden));
    }
}
