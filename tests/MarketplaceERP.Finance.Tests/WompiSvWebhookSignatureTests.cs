using System.Security.Cryptography;
using System.Text;
using MarketplaceERP.Finance.Services;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class WompiSvWebhookSignatureTests
{
    private const string DummySecret = "qa-fictitious-secret-no-commercial-account";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"IdTransaccion":"11111111-1111-1111-1111-111111111111","EsProductiva":false,"Monto":10.25}""");

    private static string Signature(ReadOnlySpan<byte> body, string secret = DummySecret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    [Fact]
    public void F461_AuthenticByteExactFixtureAccepted_NoBankCalls()
    {
        Assert.Equal("wompi_hash", WompiSvWebhookSignature.HeaderName);
        Assert.True(WompiSvWebhookSignature.Verificar(Body, Signature(Body), DummySecret));
        Assert.True(WompiSvWebhookSignature.Verificar(Body, Signature(Body).ToUpperInvariant(), DummySecret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0123456789abcdef")]
    public void F461_BadOrMissingHeaderRejected(string? header)
    {
        Assert.False(WompiSvWebhookSignature.Verificar(Body, header, DummySecret));
    }

    [Fact]
    public void F461_AlteredRawBodyOrWrongSecretRejected()
    {
        var sig = Signature(Body);
        var altered = Encoding.UTF8.GetBytes(
            """{"IdTransaccion":"11111111-1111-1111-1111-111111111111","EsProductiva":true,"Monto":10.25}""");
        Assert.False(WompiSvWebhookSignature.Verificar(altered, sig, DummySecret));
        Assert.False(WompiSvWebhookSignature.Verificar(Body, sig, "wrong-dummy-key"));
        Assert.False(WompiSvWebhookSignature.Verificar(
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Body) + "\n"), sig, DummySecret));
    }

    [Fact]
    public void F461_RejectsMultiValueHeaderAndMissingOrOversizedBody()
    {
        var sig = Signature(Body);
        Assert.False(WompiSvWebhookSignature.Verificar(Body, sig + "," + sig, DummySecret));
        Assert.False(WompiSvWebhookSignature.Verificar(Body, sig, null));
        Assert.False(WompiSvWebhookSignature.Verificar(ReadOnlySpan<byte>.Empty, sig, DummySecret));
        var tooLarge = new byte[WompiSvWebhookSignature.MaxBodyBytes + 1];
        Assert.False(WompiSvWebhookSignature.Verificar(tooLarge, sig, DummySecret));
    }

    [Fact]
    public void F461_SameSignatureDoesNotProvePaymentProductionOrAuthorization()
    {
        // Deliberately a real HMAC over a synthetic NON-production payload.
        // Signature authenticity alone cannot settle, refund or release stock.
        Assert.True(WompiSvWebhookSignature.Verificar(Body, Signature(Body), DummySecret));
        Assert.Contains("\"EsProductiva\":false", Encoding.UTF8.GetString(Body));
    }
}
