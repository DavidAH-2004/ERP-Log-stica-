using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class ValidacionIntegridadPagoTests
{
    private static ReembolsoPagoRequest Request(
        decimal monto = 1.25m, string moneda = "USD",
        string id = "TXN-QA-01", string clave = "refund-qa-key-01",
        Guid? orden = null) =>
        new(orden ?? Guid.NewGuid(), id, clave, monto, moneda);

    [Theory]
    [InlineData("0", "USD")]
    [InlineData("-0.01", "USD")]
    [InlineData("4.001", "USD")]
    [InlineData("4.00", "usd")]
    [InlineData("4.00", "")]
    [InlineData("4.00", "USDD")]
    public void F452_InvalidCurrencyOrAmountRejectsRefund(string amount, string currency)
    {
        var request = Request(decimal.Parse(amount,
            System.Globalization.CultureInfo.InvariantCulture), currency);
        Assert.False(ValidacionIntegridadPago.SolicitudReembolsoValida(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" TXN-01")]
    [InlineData("TXN-01 ")]
    [InlineData("TXN-01\n")]
    [InlineData("TXN-01\r")]
    public async Task F452_RejectsBadProviderIdBeforeCallingGateway(string id)
    {
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        var guarded = new PasarelaRecuperacionValidada(gateway.Object);
        await Assert.ThrowsAsync<ArgumentException>(
            () => guarded.ReembolsarAsync(Request(id: id)));
        gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("refund key with spaces")]
    [InlineData("refund-key\n")]
    public async Task F452_RejectsBadIdempotencyKeyBeforeCallingGateway(string key)
    {
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        var guarded = new PasarelaRecuperacionValidada(gateway.Object);
        await Assert.ThrowsAsync<ArgumentException>(
            () => guarded.ReembolsarAsync(Request(clave: key)));
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task F452_MissingOrderDoesNotCallGateway()
    {
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        var guarded = new PasarelaRecuperacionValidada(gateway.Object);
        await Assert.ThrowsAsync<ArgumentException>(
            () => guarded.ReembolsarAsync(Request(orden: Guid.Empty)));
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task F452_ValidSyntheticRequestPassesThroughWithoutAutomaticReconciliation()
    {
        var request = Request();
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        gateway.Setup(g => g.ReembolsarAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Pendiente, null));
        var result = await new PasarelaRecuperacionValidada(gateway.Object)
            .ReembolsarAsync(request);
        Assert.Equal(EstadoReembolsoProveedor.Pendiente, result.Estado);
        gateway.Verify(g => g.ReembolsarAsync(request, It.IsAny<CancellationToken>()),
            Times.Once);
        gateway.VerifyNoOtherCalls();
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" TXN-PROVEEDOR")]
    [InlineData("TXN-PROVEEDOR\n")]
    public async Task F459_ConfirmedChargeWithoutSafeProviderIdIsUncertain(string? transaction)
    {
        var provider = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        provider.Setup(g => g.ConsultarEstadoAsync(
            "PAY-TEST-VALID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Confirmado, transaction));
        var result = await new PasarelaRecuperacionValidada(provider.Object)
            .ConsultarEstadoAsync("PAY-TEST-VALID");
        Assert.Equal(EstadoCobroProveedor.Desconocido, result.Estado);
        Assert.Null(result.TransaccionId);
        provider.Verify(g => g.ConsultarEstadoAsync(
            "PAY-TEST-VALID", It.IsAny<CancellationToken>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("REFUND-ID\n")]
    public async Task F459_CompletedRefundWithoutSafeIdRemainsUncertain(string refundId)
    {
        var request = Request();
        var provider = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        provider.Setup(g => g.ReembolsarAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Completado, refundId));
        var guarded = new PasarelaRecuperacionValidada(provider.Object);
        var result = await guarded.ReembolsarAsync(request);
        Assert.Equal(EstadoReembolsoProveedor.Desconocido, result.Estado);
        Assert.Null(result.ReembolsoId);
        // Do not automatically issue another refund after uncertain response.
        provider.Verify(g => g.ReembolsarAsync(request, It.IsAny<CancellationToken>()),
            Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task F459_RefundInquiryWithoutIdCannotBeConfirmed()
    {
        var provider = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        provider.Setup(g => g.ConsultarReembolsoAsync("refund-qa-key-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Completado, null));
        var result = await new PasarelaRecuperacionValidada(provider.Object)
            .ConsultarReembolsoAsync("refund-qa-key-01");
        Assert.Equal(EstadoReembolsoProveedor.Desconocido, result.Estado);
        Assert.Null(result.ReembolsoId);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task F459_ProviderConfirmedWithValidIdRetained()
    {
        var provider = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        provider.Setup(g => g.ConsultarEstadoAsync("PAY-TEST-VALID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Confirmado, "TXN-QA-123"));
        var result = await new PasarelaRecuperacionValidada(provider.Object)
            .ConsultarEstadoAsync("PAY-TEST-VALID");
        Assert.Equal(EstadoCobroProveedor.Confirmado, result.Estado);
        Assert.Equal("TXN-QA-123", result.TransaccionId);
        provider.VerifyNoOtherCalls();
    }

}
