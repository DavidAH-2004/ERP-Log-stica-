/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.Finance;

/// <summary>
/// Extensiones de inyección de dependencias para el módulo Finance (Equipo 3).
/// Llame desde Program.cs: builder.Services.AddFinanceServices(builder.Configuration, builder.Environment.IsDevelopment())
/// </summary>
public static class FinanceServiceExtensions
{
    public static IServiceCollection AddFinanceServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        // El mock permanece restringido a Development y con habilitacion explicita.
        // La unica excepcion en Production es la DEMO ALOJADA, que registra
        // una pasarela que siempre rechaza pagos. No existe proveedor real.
        var provider = configuration["Payments:Provider"]?.Trim();
        var allowMock = string.Equals(configuration["Payments:EnableMock"], "true", StringComparison.OrdinalIgnoreCase);
        var demoAlojada = string.Equals(configuration["Demo:Hosted"], "true", StringComparison.OrdinalIgnoreCase) && !isDevelopment &&
            string.Equals(provider, "Disabled", StringComparison.OrdinalIgnoreCase) && !allowMock;
        var mockPermitido = isDevelopment && allowMock &&
            string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase);
        if (!demoAlojada && !mockPermitido)
        {
            throw new InvalidOperationException(
                "Pasarela no autorizada. Solo se admite Mock con opt-in en Development, " +
                "o Disabled con Demo:Hosted=true en Production (sin cobros).");
        }

        // ── DbContext ─────────────────────────────────────────────────────────
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));

        // En la demo alojada no se registra el Mock ni un proveedor real.
        if (mockPermitido)
            services.AddScoped<IPaymentGateway, PagoServiceMock>();
        else
            services.AddScoped<IPaymentGateway, PagoDeshabilitadoService>();

        // ── Checkout ─────────────────────────────────────────────────────────
        services.AddScoped<CheckoutOrchestrator>();

        // ── Recuperación administrada, bloqueada por defecto en HTTP ──────────
        // No se registra IPaymentRecoveryGateway: no existe proveedor de
        // recuperación real ni simulador habilitado automáticamente.
        services.AddScoped<EvaluadorConciliacionPago>(sp => ActivatorUtilities.CreateInstance<EvaluadorConciliacionPago>(sp));
        services.AddScoped<RecuperacionFinancieraService>(sp => ActivatorUtilities.CreateInstance<RecuperacionFinancieraService>(sp));

        // ── Procurement / 3-Way Match ─────────────────────────────────────────
        services.AddScoped<ITresWayMatchingService, TresWayMatchingService>();

        // ── Contabilidad General (Libro Diario / Mayor / Balance) ─────────────
        services.AddScoped<IContabilidadService, ContabilidadService>();

        // ── Liquidaciones a Vendedores (Split 90/10) ──────────────────────────
        services.AddScoped<ILiquidacionService, LiquidacionService>();

        return services;
    }
}
