using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class F472ReleaseBoundaryTests
{
    private static IConfiguration Config(params (string, string)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            values.ToDictionary(x => x.Item1, x => (string?)x.Item2)).Build();

    [Fact]
    public void F472_ProductionCannotEnableRotationEvenWithBothFlags()
    {
        var config = Config(
            ("Barcodes:EnableHistoricalAliasLookup", "true"),
            ("Barcodes:EnableEanRotation", "true"));
        Assert.Throws<InvalidOperationException>(() =>
            BarcodeRotationDeploymentGuard.Validate(config, "marketplaceerp-api"));
        BarcodeRotationDeploymentGuard.Validate(config, "marketplaceerp-api-staging");
    }

    [Fact]
    public void F472_RotationWithoutHistoricAliasOrInvalidFlagIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BarcodeRotationDeploymentGuard.Validate(
                Config(("Barcodes:EnableEanRotation", "true")), "marketplaceerp-api-staging"));
        Assert.Throws<InvalidOperationException>(() =>
            BarcodeRotationDeploymentGuard.Validate(
                Config(("Barcodes:EnableEanRotation", "perhaps")), "marketplaceerp-api-staging"));
        BarcodeRotationDeploymentGuard.Validate(Config(), "marketplaceerp-api");
    }

    [Fact]
    public async Task F472_RotationEndpointsAreDisabledAndRequireAdminRole()
    {
        using var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("f472-" + Guid.NewGuid()).Options);
        var ctrl = new RotacionesEanController(new RotacionEanService(db), Config())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Role, "Admin"),
                                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
                        "F472"))
                }
            }
        };
        var req = new SolicitarRotacionEanRequest(
            Guid.NewGuid(), CodigoBarras.GenerarInterno(100), "Motivo sintético F472");
        var disabled = Assert.IsType<ObjectResult>(
            await ctrl.Solicitar(Guid.NewGuid(), req, default));
        Assert.Equal(503, disabled.StatusCode);
        var second = Assert.IsType<ObjectResult>(
            await ctrl.Aprobar(Guid.NewGuid(), default));
        Assert.Equal(503, second.StatusCode);
        var roles = typeof(RotacionesEanController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>();
        Assert.Contains(roles, x => x.Roles == "Admin");
    }

    [Fact]
    public void F472_RealPaymentGatewayCannotActivateInProductionOrResolveRecovery()
    {
        var services = new ServiceCollection();
        services.AddFinanceServices(Config(("Payments:Provider", "Disabled")), isDevelopment: false);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<PagoServiceDisabled>(
            scope.ServiceProvider.GetRequiredService<IPaymentGateway>());
        Assert.Null(scope.ServiceProvider.GetService<IPaymentRecoveryGateway>());

        var forbidden = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            forbidden.AddFinanceServices(Config(
                ("Payments:Provider", "Mock"), ("Payments:EnableMock", "true")),
                isDevelopment: false));
    }
}
