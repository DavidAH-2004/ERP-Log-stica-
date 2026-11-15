using System.Security.Claims;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Solo base EF InMemory privada: la elección de IDs procede de entidades
/// Core existentes y evita exponer vendedores ajenos a un Vendedor.
/// </summary>
public sealed class ProductosOpcionesAltaQaTests
{
    [Theory]
    [InlineData("Admin", "sin", 2)]
    [InlineData("Vendedor", "propio", 1)]
    public async Task OpcionesAlta_DevuelveSoloVendedoresActivosPermitidos(
        string rol, string tipoClaim, int esperados)
    {
        var (catalog, core, vendedorA, vendedorB, inactivo) = CrearContexts();
        await using var _ = catalog;
        await using var __ = core;
        var controller = Controller(catalog, rol,
            tipoClaim == "propio" ? vendedorA.ToString() : null);

        var response = await controller.OpcionesAlta(core, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(response);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var lista = doc.RootElement.GetProperty("vendedores").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(esperados, lista.Length);
        Assert.Contains(vendedorA, lista);
        if (rol == "Admin") Assert.Contains(vendedorB, lista);
        else Assert.DoesNotContain(vendedorB, lista);
        Assert.DoesNotContain(inactivo, lista);
        var cats = doc.RootElement.GetProperty("categorias").EnumerateArray()
            .Select(e => e.GetProperty("nombre").GetString()).ToArray();
        Assert.Equal(2, cats.Length);
        Assert.Contains("Accesorios", cats);
        Assert.Contains("Tecnología", cats);
        // El contrato de selección no publica Email ni otros datos de Core.
        Assert.DoesNotContain("email", JsonSerializer.Serialize(ok.Value),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222")]
    [InlineData("00000000-0000-0000-0000-000000000000", "2576e782-dcdd-4a8b-88d3-e9d7d8994ff2")]
    [InlineData("713b54af-e490-4626-a025-65197558a9cb", "00000000-0000-0000-0000-000000000000")]
    [InlineData("713b54af-e490-4626-a025-65197558a9cb", "22222222-2222-2222-2222-222222222222")]
    public async Task Create_AdminNoPuedePersistirIdVacioNiPlantilla(
        string vendedor, string categoria)
    {
        var (catalog, core, _, _, _) = CrearContexts();
        await using var cat = catalog;
        await using var coreContext = core;
        var controller = Controller(catalog, "Admin", null);
        var entrada = new MarketplaceERP.Catalog.Entities.Producto
        {
            Id = Guid.NewGuid(), Sku = "QA-REFERENCIAS-FAKE",
            Nombre = "No debe persistirse",
            VendedorId = Guid.Parse(vendedor), CategoriaId = Guid.Parse(categoria)
        };
        Assert.IsType<BadRequestObjectResult>(await controller.Create(entrada));
        Assert.Equal(0, await catalog.Productos.CountAsync());
    }

    [Fact]
    public async Task Create_AdminSoloPermiteReferenciasExistentesYCoherentesDeCore()
    {
        var (catalog, core, vendedorA, _, inactivo) = CrearContexts();
        await using var cat = catalog;
        await using var coreContext = core;
        var controller = Controller(catalog, "Admin", null, core);
        var categoria = await core.Categorias.AsNoTracking().FirstAsync();
        MarketplaceERP.Catalog.Entities.Producto Producto(Guid vendedor,
            Guid idCategoria, string categoriaNombre, string sku) => new()
        {
            Id = Guid.NewGuid(), Sku = sku, Nombre = "QA Altas de Producto",
            VendedorId = vendedor, CategoriaId = idCategoria,
            CategoriaNombre = categoriaNombre,
            PrecioB2C = 20m, PrecioB2B = 18m, Moneda = "USD",
            EstadoProducto = "Activo", ProveedorNombre = "QA"
        };

        Assert.IsType<BadRequestObjectResult>(await controller.Create(
            Producto(Guid.NewGuid(), categoria.Id, categoria.Nombre, "QA-SIN-VENDEDOR")));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(
            Producto(inactivo, categoria.Id, categoria.Nombre, "QA-INACTIVO")));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(
            Producto(vendedorA, Guid.NewGuid(), categoria.Nombre, "QA-SIN-CATEGORIA")));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(
            Producto(vendedorA, categoria.Id, "Nombre no coincidente", "QA-NOMBRE-FALSO")));
        Assert.Equal(0, await catalog.Productos.CountAsync());

        var valido = await controller.Create(
            Producto(vendedorA, categoria.Id, categoria.Nombre, "QA-CORE-REAL"));
        Assert.IsType<CreatedAtActionResult>(valido);
        Assert.Equal(1, await catalog.Productos.CountAsync());
        Assert.Equal(0, await catalog.StockFisico.CountAsync());
    }

    [Fact]
    public async Task Create_EndpointHttpSinCoreRegistrado_FallaCerrado503()
    {
        var (catalog, core, vendedor, _, _) = CrearContexts();
        await using var cat = catalog;
        await using var coreContext = core;
        var categoria = core.Categorias.First();
        var controller = Controller(catalog, "Admin", null);
        using var services = new ServiceCollection().BuildServiceProvider();
        controller.HttpContext.RequestServices = services;
        var producto = new MarketplaceERP.Catalog.Entities.Producto
        {
            Id = Guid.NewGuid(), Sku = "QA-SIN-CORE",
            Nombre = "No persistir si Core no está disponible",
            VendedorId = vendedor, CategoriaId = categoria.Id,
            CategoriaNombre = categoria.Nombre
        };
        var response = Assert.IsType<ObjectResult>(await controller.Create(producto));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, await catalog.Productos.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-es-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task OpcionesAlta_VendedorSinClaimValido_Devuelve403(string? vendedorId)
    {
        var (catalog, core, _, _, _) = CrearContexts();
        await using var a = catalog;
        await using var b = core;
        var controller = Controller(catalog, "Vendedor", vendedorId);
        Assert.IsType<ForbidResult>(
            await controller.OpcionesAlta(core, CancellationToken.None));
    }

    [Fact]
    public void OpcionesAlta_EstaProtegidaPorRolEnHttp()
    {
        var method = typeof(ProductosController).GetMethod("OpcionesAlta")!;
        var auth = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("Admin,Vendedor", auth.Roles);
        Assert.Equal("opciones-alta",
            Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true)
                .Cast<HttpGetAttribute>()).Template);
    }


    [Fact]
    public async Task F418_Create_NoAceptaEanInvalidoNiPrefijoInternoManual()
    {
        var (catalog, core, vendedor, _, _) = CrearContexts();
        await using var cat = catalog;
        await using var coreContext = core;
        var categoria = await core.Categorias.AsNoTracking().FirstAsync();
        var vendedorController = Controller(catalog, "Vendedor", vendedor.ToString(), core);
        var adminController = Controller(catalog, "Admin", null, core);

        MarketplaceERP.Catalog.Entities.Producto Nuevo(string sku, string? ean) => new()
        {
            Id = Guid.NewGuid(), Sku = sku, Nombre = "Producto F418",
            CategoriaNombre = categoria.Nombre, CategoriaId = categoria.Id,
            VendedorId = vendedor, CodigoEAN = ean, PrecioB2C = 30m,
            PrecioB2B = 22m, Moneda = "USD", EstadoProducto = "Activo"
        };

        Assert.IsType<BadRequestObjectResult>(await vendedorController.Create(
            Nuevo("F418-BAD-CHECKSUM", "7410000000001")));
        Assert.IsType<BadRequestObjectResult>(await adminController.Create(
            Nuevo("F418-BAD-LENGTH", "12345")));

        var interno = CodigoBarras.GenerarInterno(42);
        Assert.IsType<BadRequestObjectResult>(await vendedorController.Create(
            Nuevo("F418-SELLER-INTERNAL", interno)));
        Assert.IsType<BadRequestObjectResult>(await adminController.Create(
            Nuevo("F418-ADMIN-INTERNAL", interno)));
        Assert.Equal(0, await catalog.Productos.CountAsync());

        // Un EAN externo con verificador válido no se modifica; el
        // identificador vacío debe persistir como NULL (asignación Admin).
        Assert.IsType<CreatedAtActionResult>(await vendedorController.Create(
            Nuevo("F418-EXTERNAL", " 4006381333931 ")));
        Assert.IsType<CreatedAtActionResult>(await vendedorController.Create(
            Nuevo("F418-NO-EAN", "  ")));
        var saved = await catalog.Productos.AsNoTracking().ToDictionaryAsync(p => p.Sku);
        Assert.Equal("4006381333931", saved["F418-EXTERNAL"].CodigoEAN);
        Assert.Null(saved["F418-NO-EAN"].CodigoEAN);
        Assert.Equal(0, await catalog.StockFisico.CountAsync());
    }

    [Fact]
    public async Task F419_Create_DosProductosDistintosConMismoEan_Devuelven409()
    {
        var (catalog, core, seller, _, _) = CrearContexts();
        await using var cat = catalog;
        await using var coreContext = core;
        var category = await core.Categorias.AsNoTracking().FirstAsync();
        var vendedor = Controller(catalog, "Vendedor", seller.ToString(), core);
        var admin = Controller(catalog, "Admin", null, core);

        MarketplaceERP.Catalog.Entities.Producto Nuevo(string sku) => new()
        {
            Id = Guid.NewGuid(), Sku = sku, Nombre = "Producto F419",
            CategoriaNombre = category.Nombre, CategoriaId = category.Id,
            VendedorId = seller, CodigoEAN = "4006381333931",
            PrecioB2C = 30m, PrecioB2B = 22m,
            Moneda = "USD", EstadoProducto = "Activo"
        };

        Assert.IsType<CreatedAtActionResult>(await vendedor.Create(Nuevo("F419-FIRST")));
        Assert.IsType<ConflictObjectResult>(await admin.Create(Nuevo("F419-ADMIN-DUP")));
        Assert.IsType<ConflictObjectResult>(await vendedor.Create(Nuevo("F419-SELLER-DUP")));
        var saved = await catalog.Productos.AsNoTracking().ToListAsync();
        Assert.Single(saved);
        Assert.Equal("F419-FIRST", saved[0].Sku);
        Assert.Equal("4006381333931", saved[0].CodigoEAN);
        Assert.Equal(0, await catalog.StockFisico.CountAsync());
    }

    [Theory]
    [InlineData("5901234123457")]
    [InlineData("")]
    [InlineData("2000000000015")]
    [InlineData(null)]
    public async Task F427_Update_RechazaRotacionYBorradoEanHistorico_Como409SinEscribir(
        string? eanSolicitado)
    {
        var (catalog, core, vendedor, _, _) = CrearContexts();
        await using var cat = catalog;
        await using var coreContext = core;
        var producto = new MarketplaceERP.Catalog.Entities.Producto
        {
            Id = Guid.NewGuid(), Sku = "F427-ORIGINAL",
            Nombre = "Producto histórico", CategoriaNombre = "Tecnología",
            CategoriaId = Guid.NewGuid(), VendedorId = vendedor,
            CodigoEAN = "4006381333931", PrecioB2C = 20m,
            PrecioB2B = 15m, EstadoProducto = "Activo"
        };
        catalog.Productos.Add(producto);
        await catalog.SaveChangesAsync();
        catalog.ChangeTracker.Clear();

        foreach (var role in new[] { "Admin", "Vendedor" })
        {
            var controller = Controller(catalog, role,
                role == "Vendedor" ? vendedor.ToString() : null);
            var cambio = F427Peticion(eanSolicitado, incluirEan: true);
            Assert.IsType<ConflictObjectResult>(
                await controller.Update(producto.Id, cambio));
            catalog.ChangeTracker.Clear();
            var persistido = await catalog.Productos.AsNoTracking()
                .SingleAsync(p => p.Id == producto.Id);
            Assert.Equal("4006381333931", persistido.CodigoEAN);
            Assert.Equal("F427-ORIGINAL", persistido.Sku);
            Assert.Equal(20m, persistido.PrecioB2C);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("4006381333931", true)]
    public async Task F427_Update_AdmiteEanOmitidoOIdentico_SinRotarlo(
        string? eanSolicitado, bool incluirEan)
    {
        var (catalog, core, vendedor, _, _) = CrearContexts();
        await using var cat = catalog;
        await using var coreContext = core;
        var producto = new MarketplaceERP.Catalog.Entities.Producto
        {
            Id = Guid.NewGuid(), Sku = "F427-ORIGINAL",
            Nombre = "Producto histórico", CategoriaNombre = "Tecnología",
            CategoriaId = Guid.NewGuid(), VendedorId = vendedor,
            CodigoEAN = "4006381333931", PrecioB2C = 20m,
            PrecioB2B = 15m, EstadoProducto = "Activo"
        };
        catalog.Productos.Add(producto);
        await catalog.SaveChangesAsync();
        catalog.ChangeTracker.Clear();
        var api = Controller(catalog, "Admin", null);
        Assert.IsType<OkObjectResult>(
            await api.Update(producto.Id, F427Peticion(eanSolicitado, incluirEan)));
        catalog.ChangeTracker.Clear();
        var guardado = await catalog.Productos.AsNoTracking()
            .SingleAsync(p => p.Id == producto.Id);
        Assert.Equal("4006381333931", guardado.CodigoEAN);
        Assert.Equal("F427-ORIGINAL", guardado.Sku);
        Assert.Equal(99m, guardado.PrecioB2C);
    }

    [Fact]
    public void F427_HttpJson_DistingueEanOmitidoDeNuloExplicito()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // The default JsonElement (Undefined) represents an omitted field:
            // omit it on serialization rather than trying to write Undefined.
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        };
        var omitido = F427Peticion(null, incluirEan: false);
        var explicito = F427Peticion(null, incluirEan: true);
        var serializadoOmitido = JsonSerializer.Serialize(omitido, opts);
        var serializadoExplicito = JsonSerializer.Serialize(explicito, opts);
        var rehidratado = JsonSerializer.Deserialize<ActualizarProductoRequest>(
            serializadoExplicito, opts)!;
        Assert.Equal(JsonValueKind.Undefined, omitido.CodigoEAN.ValueKind);
        Assert.Equal(JsonValueKind.Null, explicito.CodigoEAN.ValueKind);
        Assert.Equal(JsonValueKind.Null, rehidratado.CodigoEAN.ValueKind);
        Assert.NotEqual(serializadoOmitido, serializadoExplicito);
    }

    private static ActualizarProductoRequest F427Peticion(
        string? ean, bool incluirEan)
    {
        var request = new ActualizarProductoRequest(
            "F427-ORIGINAL", "Producto editado", "Descripción QA",
            "Tecnología", "General", 99m, 80m, 3,
            "Proveedor QA", "El Salvador", 1m, "10x10x10",
            "Unidad", "Activo", true);
        return incluirEan
            ? request with { CodigoEAN = JsonSerializer.SerializeToElement(ean) }
            : request;
    }

    private static ProductosController Controller(CatalogDbContext catalog,
        string role, string? claimId, CoreDbContext? core = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (claimId is not null)
            claims.Add(new Claim("VendedorId", claimId));
        return new ProductosController(catalog,
            Mock.Of<ICatalogImportService>(), NullLogger<ProductosController>.Instance, core)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "qa"))
                }
            }
        };
    }

    private static (CatalogDbContext catalog, CoreDbContext core,
        Guid a, Guid b, Guid inactivo) CrearContexts()
    {
        var key = Guid.NewGuid().ToString("N");
        var catalog = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase("qa-product-options-cat-" + key).Options);
        var core = new CoreDbContext(
            new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase("qa-product-options-core-" + key).Options);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var inactive = Guid.NewGuid();
        core.Vendedores.AddRange(
            new Vendedor { Id = a, NombreEmpresa = "QA A", Email = "qa-a@example.invalid", EstaActivo = true },
            new Vendedor { Id = b, NombreEmpresa = "QA B", Email = "qa-b@example.invalid", EstaActivo = true },
            new Vendedor { Id = inactive, NombreEmpresa = "QA Inactivo", Email = "qa-c@example.invalid", EstaActivo = false }
        );
        core.Categorias.AddRange(
            new Categoria { Id = Guid.NewGuid(), Nombre = "Tecnología" },
            new Categoria { Id = Guid.NewGuid(), Nombre = "Accesorios" }
        );
        core.SaveChanges();
        return (catalog, core, a, b, inactive);
    }
}
