using System.Security.Cryptography;
using System.Text;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// F461: pure verifier for the documented Wompi El Salvador "wompi_hash"
/// webhook header (HMAC-SHA256 of the EXACT original HTTP body, using the
/// application's API Secret). No payment processing, DI, routing or secrets.
/// An authentic webhook is NOT authorization to approve an order.
/// </summary>
public static class WompiSvWebhookSignature
{
    public const string HeaderName = "wompi_hash";
    public const int MaxBodyBytes = 256 * 1024;

    public static bool Verificar(ReadOnlySpan<byte> cuerpoOriginal,
        string? firmaHeader, string? apiSecret)
    {
        // Missing credentials, duplicate/comma-joined headers, oversized body
        // and malformed hex are rejected before any business operation.
        if (cuerpoOriginal.IsEmpty || cuerpoOriginal.Length > MaxBodyBytes ||
            string.IsNullOrWhiteSpace(apiSecret) || firmaHeader is not { Length: 64 })
            return false;

        byte[] declarada;
        try
        {
            declarada = Convert.FromHexString(firmaHeader);
        }
        catch (FormatException)
        {
            return false;
        }

        if (declarada.Length != 32)
            return false;

        var key = Encoding.UTF8.GetBytes(apiSecret);
        try
        {
            var calculada = HMACSHA256.HashData(key, cuerpoOriginal);
            return CryptographicOperations.FixedTimeEquals(calculada, declarada);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
