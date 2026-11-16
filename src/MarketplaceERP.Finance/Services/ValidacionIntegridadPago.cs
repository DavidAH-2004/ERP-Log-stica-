using MarketplaceERP.Shared.DTOs;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// F452: shared fail-closed financial value checks. Does not charge or refund.
/// Provider-specific rules and durable idempotency remain separate gates (#69).
/// </summary>
public static class ValidacionIntegridadPago
{
    public static bool MontoMonedaValidos(decimal monto, string? moneda) =>
        monto > 0m && monto == decimal.Round(monto, 2) &&
        moneda is { Length: 3 } &&
        moneda.All(c => c is >= 'A' and <= 'Z');

    public static bool IdProveedorValido(string? referencia) =>
        !string.IsNullOrWhiteSpace(referencia) &&
        referencia.Length <= 200 &&
        string.Equals(referencia, referencia.Trim(), StringComparison.Ordinal) &&
        referencia.All(c => !char.IsControl(c));

    public static bool ClaveIdempotenciaValida(string? clave) =>
        clave is { Length: >= 8 and <= 128 } &&
        clave.All(c => c is >= 'A' and <= 'Z'
                       or >= 'a' and <= 'z'
                       or >= '0' and <= '9'
                       or '-' or '_' or ':' or '.');

    public static bool SolicitudReembolsoValida(ReembolsoPagoRequest? solicitud) =>
        solicitud is not null &&
        solicitud.OrdenId != Guid.Empty &&
        IdProveedorValido(solicitud.TransaccionId) &&
        ClaveIdempotenciaValida(solicitud.ClaveIdempotencia) &&
        MontoMonedaValidos(solicitud.Monto, solicitud.Moneda);
}
