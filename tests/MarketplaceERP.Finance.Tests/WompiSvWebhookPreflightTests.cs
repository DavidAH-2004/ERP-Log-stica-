using System.Security.Cryptography;
using System.Text;
using MarketplaceERP.Finance.Services;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class WompiSvWebhookPreflightTests
{
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid App = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Txn = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Secret = "dummy-sandbox-only-hmac-not-a-merchant-key";

    private static string Body(bool productive, string? extra = null) =>
        $$"""
        {
            "IdCuenta":"{{Cuenta}}",
            "IdTransaccion":"{{Txn}}",
            "Monto":10.25,
            "ResultadoTransaccion":"ExitosaAprobada",
            "EsProductiva":{{productive.ToString().ToLowerInvariant()}},
            "Aplicativo":{"Id":"{{App}}"}{{extra}}
        }
        """;

    private static WompiSvWebhookPreflight.Resultado Run(
        string json, Guid? cuenta = null, Guid? app = null,
        Guid? tx = null, decimal amount = 10.25m)
    {
        var raw = Encoding.UTF8.GetBytes(json);
        var hash = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret), raw));
        return WompiSvWebhookPreflight.Evaluar(raw, hash, Secret,
            cuenta ?? Cuenta, app ?? App, tx ?? Txn, amount);
    }

    [Fact]
    public void F471_SignedDevelopmentPaymentCannotBePostedAsReal()
    {
        Assert.Equal(WompiSvWebhookPreflight.Resultado.SandboxSinCargo, Run(Body(false)));
        Assert.Equal(WompiSvWebhookPreflight.Resultado.RequiereConsultaProveedor,
            Run(Body(true)));
    }

    [Fact]
    public void F471_ExpectedAmountAndAllProviderIdentitiesAreMandatory()
    {
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado,
            Run(Body(true), amount: 10.26m));
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado,
            Run(Body(true), cuenta: Guid.NewGuid()));
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado,
            Run(Body(true), app: Guid.NewGuid()));
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado,
            Run(Body(true), tx: Guid.NewGuid()));
    }

    [Fact]
    public void F471_DuplicatePropertiesAreRejectedEvenWithValidHmac()
    {
        var duplicateFlag = Body(true, ",\n\"EsProductiva\":false");
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado, Run(duplicateFlag));
        var duplicateTransaction = Body(true, ",\n\"IdTransaccion\":\"33333333-3333-3333-3333-333333333333\"");
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado, Run(duplicateTransaction));
    }

    [Fact]
    public void F471_InvalidStatusAndJsonAreNeverConfirmed()
    {
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado,
            Run(Body(true).Replace("ExitosaAprobada", "Fallida")));
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado, Run("{"));
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado,
            Run(Body(true).Replace("true", "\"true\"")));
    }

    [Fact]
    public void F471_ValidLookingJsonWithWrongSignatureIsRejected()
    {
        var raw = Encoding.UTF8.GetBytes(Body(true));
        var fake = new string('0', 64);
        Assert.Equal(WompiSvWebhookPreflight.Resultado.Rechazado,
            WompiSvWebhookPreflight.Evaluar(raw, fake, Secret,
                Cuenta, App, Txn, 10.25m));
    }
}
