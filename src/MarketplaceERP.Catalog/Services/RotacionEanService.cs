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
}
