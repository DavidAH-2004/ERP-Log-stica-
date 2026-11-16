using System.Data;
using System.Security.Cryptography;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Finance.Services;

public enum ResultadoInboxWompiSv
{
    Rechazado,
    SandboxSinCargo,
    NuevoRequiereConsulta,
    RepetidoSinMutacion,
    ConflictoBloqueado
}

/// <summary>
/// F483: durable quarantine for SIGNED synthetic provider notices.
/// Cannot approve an order, refund, create invoice, release stock or
/// call a bank. Not registered in API DI until commercial #69 approval.
/// </summary>
public sealed class WompiSvWebhookInbox
{
    private readonly FinanceDbContext _db;

    public WompiSvWebhookInbox(FinanceDbContext db) => _db = db;

    public async Task<ResultadoInboxWompiSv> RegistrarAsync(
        ReadOnlyMemory<byte> cuerpoOriginal, string? wompiHash,
        string? apiSecret, Guid cuentaEsperada, Guid aplicativoEsperado,
        Guid transaccionEsperada, decimal montoEsperado,
        CancellationToken ct = default)
    {
        var checkedResult = WompiSvWebhookPreflight.Evaluar(
            cuerpoOriginal.Span, wompiHash, apiSecret, cuentaEsperada,
            aplicativoEsperado, transaccionEsperada, montoEsperado);

        if (checkedResult == WompiSvWebhookPreflight.Resultado.Rechazado)
            return ResultadoInboxWompiSv.Rechazado;
        if (checkedResult == WompiSvWebhookPreflight.Resultado.SandboxSinCargo)
            return ResultadoInboxWompiSv.SandboxSinCargo;

        var fingerprint = Convert.ToHexString(SHA256.HashData(cuerpoOriginal.Span));
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = """
                    INSERT INTO finance.webhooks_pago_inbox
                        ("Proveedor", "CuentaId", "AplicativoId", "TransaccionId",
                         "HuellaCuerpo", "Estado")
                    VALUES ('WompiSV', @cuenta, @app, @txn, @huella, 'RequiereConsulta')
                    ON CONFLICT ON CONSTRAINT "PK_webhooks_pago_inbox" DO NOTHING
                    RETURNING "HuellaCuerpo"
                    """;
                Add(insert, "cuenta", cuentaEsperada);
                Add(insert, "app", aplicativoEsperado);
                Add(insert, "txn", transaccionEsperada);
                Add(insert, "huella", fingerprint);
                if (await insert.ExecuteScalarAsync(ct) is not null)
                    return ResultadoInboxWompiSv.NuevoRequiereConsulta;
            }

            // A repeated, identical signed body is a no-op. A different
            // signed body for the same payment identity is NOT silently
            // treated as a second operation, even across two API processes.
            await using var select = connection.CreateCommand();
            select.CommandText = """
                SELECT "HuellaCuerpo"
                FROM finance.webhooks_pago_inbox
                WHERE "Proveedor"='WompiSV' AND "CuentaId"=@cuenta
                  AND "AplicativoId"=@app AND "TransaccionId"=@txn
                """;
            Add(select, "cuenta", cuentaEsperada);
            Add(select, "app", aplicativoEsperado);
            Add(select, "txn", transaccionEsperada);
            var existing = (string?)await select.ExecuteScalarAsync(ct);
            return string.Equals(existing?.Trim(), fingerprint,
                StringComparison.Ordinal)
                ? ResultadoInboxWompiSv.RepetidoSinMutacion
                : ResultadoInboxWompiSv.ConflictoBloqueado;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private static void Add(System.Data.Common.DbCommand command,
        string key, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = key;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
