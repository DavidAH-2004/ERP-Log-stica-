using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// F452: optional guard for a FUTURE approved recovery provider. No DI
/// registration, real gateway, webhook or external charge is enabled here.
/// Authorization, durable refund idempotency and reconciliation stay required.
/// </summary>
public sealed class PasarelaRecuperacionValidada : IPaymentRecoveryGateway
{
    private readonly IPaymentRecoveryGateway _inner;

    public PasarelaRecuperacionValidada(IPaymentRecoveryGateway inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<ConsultaPagoResult> ConsultarEstadoAsync(
        string referenciaOperacion, CancellationToken cancellationToken = default)
    {
        if (!ValidacionIntegridadPago.IdProveedorValido(referenciaOperacion))
            throw new ArgumentException("Referencia de operación inválida.", nameof(referenciaOperacion));
        return _inner.ConsultarEstadoAsync(referenciaOperacion, cancellationToken);
    }

    public Task<ReembolsoPagoResult> ConsultarReembolsoAsync(
        string claveIdempotencia, CancellationToken cancellationToken = default)
    {
        if (!ValidacionIntegridadPago.ClaveIdempotenciaValida(claveIdempotencia))
            throw new ArgumentException("Clave de idempotencia inválida.", nameof(claveIdempotencia));
        return _inner.ConsultarReembolsoAsync(claveIdempotencia, cancellationToken);
    }

    public Task<ReembolsoPagoResult> ReembolsarAsync(
        ReembolsoPagoRequest request, CancellationToken cancellationToken = default)
    {
        if (!ValidacionIntegridadPago.SolicitudReembolsoValida(request))
            throw new ArgumentException("Solicitud de reembolso inválida.", nameof(request));
        return _inner.ReembolsarAsync(request, cancellationToken);
    }
}
