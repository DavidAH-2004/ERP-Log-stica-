using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;

namespace MarketplaceERP.Finance.Services;

/// <summary>Pasarela que JAMAS realiza ni simula un cobro.
/// Se registra solo en la demostracion alojada, separada de Production real.
/// </summary>
internal sealed class PagoDeshabilitadoService : IPaymentGateway
{
    public Task<PagoResult> ProcesarPagoAsync(PagoRequest request) =>
        Task.FromResult(new PagoResult(false, null,
            "Los pagos estan deshabilitados en la demostracion alojada."));
}
