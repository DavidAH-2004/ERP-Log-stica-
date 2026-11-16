using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class RegistroProveedoresControllerTests
{
    private static (ProveedoresController Controller, CoreDbContext Core) Create()
    {
        var id = Guid.NewGuid().ToString("N");
        var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("proveedores-" + id).Options);
        var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("catalog-" + id).Options);
        var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("finance-" + id).Options);

        var controller = new ProveedoresController(core, catalog, finance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return (controller, core);
    }

    private static RegistrarProveedorRequest Valid(
        string nombre = "Distribuidora Ejemplo",
        string razon = "Distribuidora Ejemplo S.A. de C.V.",
        string nit = "0614-123456-123-4",
        string pago = "Credito",
        int dias = 30,
        bool activo = true) => new(
            nombre, razon, nit, "1234-5", "Insumos", "Compras QA",
            "compras@example.invalid", "+503 2222 3333",
            "Dirección de ensayo", pago, dias, activo);

    [Fact]
    public async Task RegistersOnlyOneSupplierInCoreAndDoesNotInventOtif()
    {
        var (controller, core) = Create();
        var result = await controller.Registrar(Valid(
            "  Distribuidora   Ejemplo  ", "  Ejemplo  Sociedad  ",
            "", "Contado", 0), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var proveedor = Assert.Single(await core.Proveedores.ToListAsync());
        Assert.NotEqual(Guid.Empty, proveedor.Id);
        Assert.Equal("Distribuidora Ejemplo", proveedor.NombreComercial);
        Assert.Equal("Ejemplo Sociedad", proveedor.RazonSocial);
        Assert.Equal(0, proveedor.PlazoCreditoDias);
        Assert.Null(proveedor.OtifHistorico);
        Assert.True(proveedor.Activo);
        Assert.Equal(DateTimeKind.Utc, proveedor.FechaRegistro.Kind);
    }

    [Fact]
    public async Task RejectsDuplicateBusinessNameRegardlessOfCaseOrWhitespace()
    {
        var (controller, core) = Create();
        Assert.IsType<CreatedResult>(await controller.Registrar(Valid(nit:""), CancellationToken.None));
        var second = Valid("  distribuidora   ejemplo ", "Otra entidad", nit:"");
        Assert.IsType<ConflictObjectResult>(await controller.Registrar(second, CancellationToken.None));
        Assert.Equal(1, await core.Proveedores.CountAsync());
    }

    [Fact]
    public async Task RejectsDuplicateNormalizedSocialNameAndNit()
    {
        var (controller, core) = Create();
        Assert.IsType<CreatedResult>(await controller.Registrar(Valid(), CancellationToken.None));
        var nitDuplicate = Valid("Otra empresa", "Razón distinta", "06141234561234");
        Assert.IsType<ConflictObjectResult>(await controller.Registrar(nitDuplicate, CancellationToken.None));
        var socialDuplicate = Valid("Nombre distinto", "  distribuidora ejemplo s.a. de c.v.  ", "");
        Assert.IsType<ConflictObjectResult>(await controller.Registrar(socialDuplicate, CancellationToken.None));
        Assert.Equal(1, await core.Proveedores.CountAsync());
    }

    [Theory]
    [InlineData("", "Razón válida", "", "Contado", 0)]
    [InlineData("Nombre", "", "", "Contado", 0)]
    [InlineData("Nombre", "Razón", "123", "Contado", 0)]
    [InlineData("Nombre", "Razón", "", "Contado", 1)]
    [InlineData("Nombre", "Razón", "", "Credito", 0)]
    [InlineData("Nombre", "Razón", "", "Credito", -1)]
    [InlineData("Nombre", "Razón", "", "Credito", 3651)]
    [InlineData("Nombre", "Razón", "", "Desconocido", 0)]
    public async Task RejectsInvalidFieldsAndPaymentTerms(
        string nombre, string razon, string nit, string pago, int dias)
    {
        var (controller, core) = Create();
        var input = Valid(nombre, razon, nit, pago, dias);
        Assert.IsType<BadRequestObjectResult>(await controller.Registrar(input, CancellationToken.None));
        Assert.Equal(0, await core.Proveedores.CountAsync());
    }

    [Fact]
    public async Task RejectsBadEmailAndPhone()
    {
        var (controller, core) = Create();
        var invalidEmail = Valid() with { Email = "bad-email" };
        var invalidPhone = Valid() with { Telefono = "123" };
        Assert.IsType<BadRequestObjectResult>(await controller.Registrar(invalidEmail, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Registrar(invalidPhone, CancellationToken.None));
        Assert.Equal(0, await core.Proveedores.CountAsync());
    }

    [Fact]
    public async Task ExplicitInactiveSupplierRemainsInactiveAndUnavailableToPurchaseFilter()
    {
        var (controller, core) = Create();
        Assert.IsType<CreatedResult>(
            await controller.Registrar(Valid(activo:false), CancellationToken.None));
        Assert.Equal(1, await core.Proveedores.CountAsync());
        Assert.Equal(0, await core.Proveedores.CountAsync(p => p.Activo));
    }

    [Fact]
    public void EndpointPreservesAdminOnlyRoleAndRoute()
    {
        var role = (AuthorizeAttribute?)typeof(ProveedoresController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).SingleOrDefault();
        Assert.NotNull(role);
        Assert.Equal("Admin", role!.Roles);
        var action = typeof(ProveedoresController).GetMethod(nameof(ProveedoresController.Registrar));
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttributes(typeof(HttpPostAttribute), false).SingleOrDefault());
    }
}
