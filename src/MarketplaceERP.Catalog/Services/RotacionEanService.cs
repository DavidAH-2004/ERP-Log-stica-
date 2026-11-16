using System.Data;
using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// F470: Admin-only entrypoints call stored PostgreSQL operations.
/// The transaction, row locks, immutable audit and historic reservations
/// are all inside the database. The HTTP feature flags default to OFF.
/// This class deliberately does not accept roles/claimed actor IDs.
/// </summary>
public sealed class RotacionEanService
{
    private readonly CatalogDbContext _db;

    public RotacionEanService(CatalogDbContext db) => _db = db;

    public Task<string> SolicitarAsync(
        Guid solicitudId, Guid productoId, string codigoNuevo,
        string motivo, Guid actorAdmin, CancellationToken ct = default)
    {
        if (solicitudId == Guid.Empty || productoId == Guid.Empty || actorAdmin == Guid.Empty)
            throw new ArgumentException("Identificadores de solicitud, producto y actor obligatorios.");
        if (!CodigoBarras.EsEan13Valido(codigoNuevo) ||
            !codigoNuevo.StartsWith("20", StringComparison.Ordinal))
            throw new ArgumentException("Solo EAN-13 internos del prefijo 20 pueden rotarse.");
        var justificacion = motivo?.Trim();
        if (justificacion is null || justificacion.Length is < 10 or > 1000)
            throw new ArgumentException("La justificación debe tener entre 10 y 1000 caracteres.");

        return _db.Database.SqlQuery<string>($"""
            SELECT catalog.f470_solicitar_rotacion(
                {solicitudId}, {productoId}, {codigoNuevo}, {justificacion}, {actorAdmin})
            AS "Value"
            """).SingleAsync(ct);
    }

    public Task<string> AprobarAsync(
        Guid solicitudId, Guid actorAdmin, CancellationToken ct = default)
    {
        if (solicitudId == Guid.Empty || actorAdmin == Guid.Empty)
            throw new ArgumentException("Identificadores de solicitud y aprobador obligatorios.");

        return _db.Database.SqlQuery<string>($"""
            SELECT catalog.f470_aprobar_rotacion({solicitudId}, {actorAdmin}) AS "Value"
            """).SingleAsync(ct);
    }
    /// <summary>
    /// F481: cancel only a pending request, as its ORIGINAL requester.
    /// Approved/executed rotation is never reversible through this endpoint.
    /// </summary>
    public Task<string> CancelarAsync(
        Guid solicitudId, Guid actorAdmin, CancellationToken ct = default)
    {
        if (solicitudId == Guid.Empty || actorAdmin == Guid.Empty)
            throw new ArgumentException("Solicitud y administrador obligatorios.");
        return _db.Database.SqlQuery<string>($"""
            SELECT catalog.f481_cancelar_rotacion({solicitudId}, {actorAdmin}) AS "Value"
            """).SingleAsync(ct);
    }

    /// <summary>Read-only, capped Admin audit. No operator PII or credentials.</summary>
    public async Task<IReadOnlyList<RotacionEanEvento>> HistorialAsync(
        Guid productoId, CancellationToken ct = default)
    {
        if (productoId == Guid.Empty)
            throw new ArgumentException("Producto inválido.", nameof(productoId));

        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "SolicitudId", "Version", "Estado", "CodigoAnterior", "CodigoNuevo",
                       "SolicitadoUtc", "AprobadoUtc", "EjecutadoUtc"
                FROM catalog.rotaciones_ean
                WHERE "ProductoId" = @producto
                ORDER BY "SolicitadoUtc" DESC, "Version" DESC
                LIMIT 100
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "producto";
            parameter.Value = productoId;
            command.Parameters.Add(parameter);
            var result = new List<RotacionEanEvento>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new RotacionEanEvento(
                    reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetDateTime(5),
                    reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    reader.IsDBNull(7) ? null : reader.GetDateTime(7)));
            }
            return result;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

}

public sealed record RotacionEanEvento(
    Guid SolicitudId, int Version, string Estado,
    string CodigoAnterior, string CodigoNuevo,
    DateTime SolicitadoUtc, DateTime? AprobadoUtc, DateTime? EjecutadoUtc);
