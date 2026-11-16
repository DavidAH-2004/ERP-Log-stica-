using System.Text.Json;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// F471: verified Wompi SV webhook envelope, never a payment confirmation.
/// Result RequiereConsultaProveedor MUST be reconciled against Wompi by a
/// separately authorized adapter before invoice, stock or ledger mutation.
/// No registration or webhook endpoint exists on purpose (#69 deferred).
/// </summary>
public static class WompiSvWebhookPreflight
{
    public enum Resultado
    {
        Rechazado = 0,
        SandboxSinCargo = 1,
        RequiereConsultaProveedor = 2
    }

    public static Resultado Evaluar(
        ReadOnlySpan<byte> rawBody, string? wompiHash, string? apiSecret,
        Guid cuentaEsperada, Guid aplicativoEsperado, Guid transaccionEsperada,
        decimal montoEsperado)
    {
        if (cuentaEsperada == Guid.Empty || aplicativoEsperado == Guid.Empty ||
            transaccionEsperada == Guid.Empty || montoEsperado <= 0m ||
            montoEsperado != decimal.Round(montoEsperado, 2) ||
            !WompiSvWebhookSignature.Verificar(rawBody, wompiHash, apiSecret))
            return Resultado.Rechazado;

        try
        {
            using var doc = JsonDocument.Parse(rawBody.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Resultado.Rechazado;

            if (!GuidMatches(root, "IdCuenta", cuentaEsperada) ||
                !GuidMatches(root, "IdTransaccion", transaccionEsperada))
                return Resultado.Rechazado;

            var app = UniqueProperty(root, "Aplicativo");
            if (app is null || app.Value.ValueKind != JsonValueKind.Object ||
                !GuidMatches(app.Value, "Id", aplicativoEsperado))
                return Resultado.Rechazado;

            var monto = UniqueProperty(root, "Monto");
            if (monto is null || monto.Value.ValueKind != JsonValueKind.Number ||
                !monto.Value.TryGetDecimal(out var reportedAmount) ||
                reportedAmount != montoEsperado)
                return Resultado.Rechazado;

            var status = UniqueProperty(root, "ResultadoTransaccion");
            if (status is null || status.Value.ValueKind != JsonValueKind.String ||
                status.Value.GetString() != "ExitosaAprobada")
                return Resultado.Rechazado;

            var productiva = UniqueProperty(root, "EsProductiva");
            if (productiva is null)
                return Resultado.Rechazado;
            return productiva.Value.ValueKind switch
            {
                JsonValueKind.False => Resultado.SandboxSinCargo,
                JsonValueKind.True => Resultado.RequiereConsultaProveedor,
                _ => Resultado.Rechazado
            };
        }
        catch (JsonException)
        {
            return Resultado.Rechazado;
        }
        catch (FormatException)
        {
            return Resultado.Rechazado;
        }
        catch (OverflowException)
        {
            return Resultado.Rechazado;
        }
    }

    private static bool GuidMatches(JsonElement parent, string property, Guid expected)
    {
        var found = UniqueProperty(parent, property);
        return found is not null && found.Value.ValueKind == JsonValueKind.String &&
            found.Value.TryGetGuid(out var value) && value == expected;
    }

    /// <summary>
    /// Reject duplicate JSON fields: different consumers may choose first/last
    /// and disagree about transaction or the production flag.
    /// </summary>
    private static JsonElement? UniqueProperty(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object)
            return null;
        var count = 0;
        JsonElement value = default;
        foreach (var property in parent.EnumerateObject())
        {
            // F478: reject case-variant duplicates; another JSON consumer
            // might be case-insensitive and choose a different value.
            if (!string.Equals(property.Name, name,
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (!property.NameEquals(name)) return null;
            value = property.Value;
            if (++count != 1) return null;
        }
        return count == 1 ? value : null;
    }
}
