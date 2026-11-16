using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class ProductosCodigoBarrasControllerTests
{
    private static Producto ProductoDe(string sku, Guid vendedor, string ean, bool publicado = true) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Nombre = sku,
        CodigoEAN = ean,
        VendedorId = vendedor,
        CategoriaId = Guid.NewGuid(),
        CategoriaNombre = "QA",
        EstadoProducto = publicado ? "Activo" : "Inactivo",
        EstaPublicado = publicado,
    };

    private static CatalogDbContext CrearDb(params Producto[] productos)
    {
        var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("F397-" + Guid.NewGuid()).Options);
        db.Productos.AddRange(productos);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static ProductosCodigoBarrasController CrearController(
        CatalogDbContext db, string rol, params Claim[] extra)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "F397"),
            new(ClaimTypes.Role, rol) };
        claims.AddRange(extra);
        return new ProductosCodigoBarrasController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "F397"))
                }
            }
        };
    }

    [Fact]
    public async Task Vendedor_SoloPuedeEtiquetarYBuscarSusPropiosProductos()
    {
        var mio = Guid.NewGuid();
        var ajeno = Guid.NewGuid();
        var codigoMio = CodigoBarras.GenerarInterno(20);
        var codigoAjeno = CodigoBarras.GenerarInterno(21);
        var pMio = ProductoDe("MIO", mio, codigoMio);
        var pAjeno = ProductoDe("AJENO", ajeno, codigoAjeno);
        using var db = CrearDb(pMio, pAjeno);
        var api = CrearController(db, "Vendedor", new Claim("VendedorId", mio.ToString()));

        var resultado = Assert.IsType<OkObjectResult>(await api.Etiquetas(null, default));
        var etiquetas = Assert.IsType<List<EtiquetaProducto>>(resultado.Value);
        Assert.Single(etiquetas);
        Assert.Equal("MIO", etiquetas[0].Sku);

        // La selección por IDs no debe abrir una vía para ver un producto ajeno.
        var ajenas = Assert.IsType<OkObjectResult>(await api.Etiquetas(pAjeno.Id.ToString(), default));
        Assert.Empty(Assert.IsType<List<EtiquetaProducto>>(ajenas.Value));
        Assert.IsType<NotFoundObjectResult>(await api.BuscarPorCodigo(codigoAjeno, default));
        Assert.IsType<OkObjectResult>(await api.BuscarPorCodigo(codigoMio, default));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Vendedor_SinClaimOConClaimsContradictorios_SeRechaza()
    {
        using var db = CrearDb(ProductoDe("X", Guid.NewGuid(), CodigoBarras.GenerarInterno(1)));
        var sinClaim = CrearController(db, "Vendedor");
        Assert.IsType<ForbidResult>(await sinClaim.Etiquetas(null, default));
        Assert.IsType<ForbidResult>(await sinClaim.BuscarPorCodigo(CodigoBarras.GenerarInterno(1), default));

        var contradictorios = CrearController(db, "Vendedor",
            new Claim("VendedorId", Guid.NewGuid().ToString()),
            new Claim("VendedorId", Guid.NewGuid().ToString()));
        Assert.IsType<ForbidResult>(await contradictorios.Etiquetas(null, default));
    }

    [Fact]
    public async Task Admin_PuedeConsultarVariosYDetectaCodigoDuplicado()
    {
        var duplicado = CodigoBarras.GenerarInterno(42);
        var p1 = ProductoDe("A", Guid.NewGuid(), duplicado);
        var p2 = ProductoDe("B", Guid.NewGuid(), duplicado);
        using var db = CrearDb(p1, p2);
        var api = CrearController(db, "Admin");

        Assert.IsType<ConflictObjectResult>(await api.BuscarPorCodigo(duplicado, default));
        var listado = Assert.IsType<OkObjectResult>(
            await api.Etiquetas(p1.Id + "," + p2.Id, default));
        Assert.Equal(2, Assert.IsType<List<EtiquetaProducto>>(listado.Value).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Etiquetas_ListaInvalida_Rechazada(string ids)
    {
        using var db = CrearDb();
        var api = CrearController(db, "Admin");
        Assert.IsType<BadRequestObjectResult>(await api.Etiquetas(ids, default));
    }

    [Fact]
    public async Task Etiquetas_ExigeMaximoCienSeleccionados()
    {
        using var db = CrearDb();
        var ids = string.Join(",", Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()));
        Assert.IsType<BadRequestObjectResult>(
            await CrearController(db, "Admin").Etiquetas(ids, default));
    }

    [Fact]
    public async Task BuscarPorCodigo_DebeValidarDigitoVerificador()
    {
        using var db = CrearDb();
        Assert.IsType<BadRequestObjectResult>(
            await CrearController(db, "Admin").BuscarPorCodigo("2000000000014", default));
    }
}
