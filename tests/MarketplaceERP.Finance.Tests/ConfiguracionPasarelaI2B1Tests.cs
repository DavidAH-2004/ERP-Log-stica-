using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance;
using MarketplaceERP.Finance.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>La pasarela simulada debe estar deshabilitada por defecto y en Production.</summary>
public class ConfiguracionPasarelaI2B1Tests
{
    private static IConfiguration Configurar(string? provider, string? enableMock)
    {
        var valores = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=qa;Username=qa;Password=not_used"
        };
        if (provider is not null) valores["Payments:Provider"] = provider;
        if (enableMock is not null) valores["Payments:EnableMock"] = enableMock;
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    [Fact]
    public void SinProveedor_NoArrancaNiEnDevelopment()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddFinanceServices(Configurar(null, null), isDevelopment: true));
        Assert.Empty(services);
    }

    [Fact]
    public void MockSinHabilitacionExplicita_SeRechaza()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddFinanceServices(Configurar("Mock", null), isDevelopment: true));
    }

    [Fact]
    public void MockDeshabilitado_SeRechaza()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddFinanceServices(Configurar("Mock", "false"), isDevelopment: true));
    }

    [Fact]
    public void Production_ProhibeMockAunqueLaBanderaEsteActiva()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddFinanceServices(Configurar("Mock", "true"), isDevelopment: false));
        Assert.Empty(services);
    }

    [Fact]
    public void ProveedorNoImplementado_SeRechazaSinFingirCobros()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddFinanceServices(Configurar("Stripe", "true"), isDevelopment: true));
    }

    [Fact]
    public async Task DemoAlojada_UsaProveedorDeshabilitadoYRechazaCualquierCobro()
    {
        var valores = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=qa;Username=qa;Password=unused",
            ["Demo:Hosted"] = "true", ["Payments:Provider"] = "Disabled",
            ["Payments:EnableMock"] = "false"
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinanceServices(new ConfigurationBuilder().AddInMemoryCollection(valores).Build(),
            isDevelopment: false);
        using var sp = services.BuildServiceProvider();
        var pasarela = sp.GetRequiredService<IPaymentGateway>();
        Assert.IsType<PagoDeshabilitadoService>(pasarela);
        var resultado = await pasarela.ProcesarPagoAsync(
            new MarketplaceERP.Shared.DTOs.PagoRequest(Guid.NewGuid(), Guid.NewGuid(), 5m, "USD"));
        Assert.False(resultado.Exitoso);
        Assert.Null(resultado.TransaccionId);
    }

    [Fact]
    public void DisabledSinModoDemo_SeRechazaEnProduction()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddFinanceServices(Configurar("Disabled", "false"),
                isDevelopment: false));
    }

    [Fact]
    public void Development_ConOptInExplicito_RegistraSolamenteElMock()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinanceServices(Configurar("Mock", "true"), isDevelopment: true);
        using var sp = services.BuildServiceProvider();
        var pasarela = sp.GetRequiredService<IPaymentGateway>();
        Assert.IsType<PagoServiceMock>(pasarela);
    }
}
