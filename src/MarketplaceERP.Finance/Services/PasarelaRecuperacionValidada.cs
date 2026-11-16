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

    public async Task<ConsultaPagoResult> ConsultarEstadoAsync(
        string referenciaOperacion, CancellationToken cancellationToken = default)
    {
        if (!ValidacionIntegridadPago.IdProveedorValido(referenciaOperacion))
            throw new ArgumentException("Referencia de operación inválida.", nameof(referenciaOperacion));
        var resultado = await _inner.ConsultarEstadoAsync(referenciaOperacion, cancellationToken);
        // F459: un proveedor que confirma sin ID confiable no permite cierre automático.
        if (resultado is null ||
            (resultado.Estado == EstadoCobroProveedor.Confirmado &&
             !ValidacionIntegridadPago.IdProveedorValido(resultado.TransaccionId)))
            return new ConsultaPagoResult(EstadoCobroProveedor.Desconocido, null);
        return resultado;
    }

    public async Task<ReembolsoPagoResult> ConsultarReembolsoAsync(
        string claveIdempotencia, CancellationToken cancellationToken = default)
    {
        if (!ValidacionIntegridadPago.ClaveIdempotenciaValida(claveIdempotencia))
            throw new ArgumentException("Clave de idempotencia inválida.", nameof(claveIdempotencia));
        return SanearReembolso(await _inner.ConsultarReembolsoAsync(
            claveIdempotencia, cancellationToken));
    }

    public async Task<ReembolsoPagoResult> ReembolsarAsync(
        ReembolsoPagoRequest request, CancellationToken cancellationToken = default)
    {
        if (!ValidacionIntegridadPago.SolicitudReembolsoValida(request))
            throw new ArgumentException("Solicitud de reembolso inválida.", nameof(request));
        // Only an already authorized caller may reach this method.
        // Never retry a provider call merely because its response is malformed.
        return SanearReembolso(await _inner.ReembolsarAsync(request, cancellationToken));
    }

    private static ReembolsoPagoResult SanearReembolso(ReembolsoPagoResult? resultado)
    {
        if (resultado is null ||
            (resultado.Estado == EstadoReembolsoProveedor.Completado &&
             !ValidacionIntegridadPago.IdProveedorValido(resultado.ReembolsoId)))
            return new ReembolsoPagoResult(EstadoReembolsoProveedor.Desconocido, null);
        return resultado;
    }
}
