using System.Globalization;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Permite únicamente la conciliación 3-Way Match de una factura sintética
/// explícitamente autorizada en el servicio Render STAGING aislado.
/// No habilita ni registra un proveedor de pagos; la pasarela debe seguir Disabled.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StagingThreeWayMatchGuardAttribute : ActionFilterAttribute
{
    private const string EnabledKey = "Procurement:StagingThreeWayMatch:Enabled";
    // Development-only opt-in: permit normal 3-Way Match for QA invoices.
    // It is never valid without the isolated Render staging identity, disabled
    // external payments, and the independently approved Neon direct endpoint.
    private const string AllQaInvoicesKey = "Procurement:StagingThreeWayMatch:AllowIsolatedQaInvoices";
    private const string InvoiceIdKey = "Procurement:StagingThreeWayMatch:InvoiceId";
    private const string OrderIdKey = "Procurement:StagingThreeWayMatch:OrderId";
    private const string ReceivedQuantityKey = "Procurement:StagingThreeWayMatch:ReceivedQuantity";
    private const string ExpiresAtUtcKey = "Procurement:StagingThreeWayMatch:ExpiresAtUtc";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var services = context.HttpContext.RequestServices;
        var configuration = services.GetRequiredService<IConfiguration>();
        var paymentMode = services.GetRequiredService<PaymentOperationsMode>();

        var request = context.ActionArguments.Values
            .OfType<Validar3WayMatchRequest>()
            .SingleOrDefault();

        var enabled = bool.TryParse(configuration[EnabledKey], out var isEnabled) && isEnabled;
        var stagingIdentity =
            string.Equals(
                configuration["RENDER_SERVICE_NAME"],
                StagingDatabaseTargetGuard.ServiceName,
                StringComparison.Ordinal) &&
            string.Equals(
                configuration["Deployment:Target"],
                StagingDatabaseTargetGuard.TargetMarker,
                StringComparison.Ordinal);
        var paymentsRemainDisabled =
            paymentMode.IsDisabled &&
            string.Equals(
                configuration["Payments:Provider"],
                "Disabled",
                StringComparison.OrdinalIgnoreCase);

        var invoiceConfigured = Guid.TryParse(configuration[InvoiceIdKey], out var allowedInvoiceId);
        var orderConfigured = Guid.TryParse(configuration[OrderIdKey], out var allowedOrderId);
        var quantityConfigured = int.TryParse(
            configuration[ReceivedQuantityKey],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var allowedQuantity);
        var expiryConfigured = DateTimeOffset.TryParse(
            configuration[ExpiresAtUtcKey],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var expiresAtUtc);

        var requestMatchesAllowlist =
            request is not null &&
            invoiceConfigured &&
            orderConfigured &&
            quantityConfigured &&
            request.FacturaProveedorId == allowedInvoiceId &&
            request.OrdenCompraId == allowedOrderId &&
            request.CantidadRecibidaBodega == allowedQuantity;

        // The single synthetic-invoice allowlist remains the default. A separate
        // explicit switch permits testing all ordinary invoices ONLY on isolated
        // STAGING and only when external payments remain strictly disabled.
        var allQaInvoicesEnabled =
            enabled &&
            bool.TryParse(configuration[AllQaInvoicesKey], out var allowAllQaInvoices) &&
            allowAllQaInvoices &&
            stagingIdentity &&
            paymentsRemainDisabled;

        var isolatedQaTargetVerified = false;
        if (allQaInvoicesEnabled)
        {
            // Do not trust environment markers alone: verify the actual runtime
            // connection against the approved isolated Neon endpoint, without
            // connecting to a database or logging any connection string.
            try
            {
                StagingDatabaseTargetGuard.ValidateRuntime(
                    configuration,
                    configuration["RENDER_SERVICE_NAME"],
                    "Production", // ASP.NET hosting identity required by staging startup.
                    configuration.GetConnectionString("DefaultConnection"));
                isolatedQaTargetVerified = true;
            }
            catch (InvalidOperationException)
            {
                // Fail closed; the existing 503 guard response is intentional.
            }
        }

        var syntheticInvoiceAuthorized =
            enabled &&
            stagingIdentity &&
            paymentsRemainDisabled &&
            requestMatchesAllowlist &&
            expiryConfigured &&
            expiresAtUtc > DateTimeOffset.UtcNow;

        if (syntheticInvoiceAuthorized || (isolatedQaTargetVerified && request is not null))
            return;

        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(new
        {
            tipo = "3WayMatchStagingNoAutorizado",
            mensaje = "El 3-Way Match está deshabilitado salvo para la factura sintética explícitamente autorizada en STAGING; los pagos externos permanecen deshabilitados."
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };
    }
}
