using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// F402: asigna códigos EAN-13 internos SOLO a productos con CodigoEAN NULL o vacío.
/// Conserva absolutamente todos los EAN existentes, aunque no sean EAN-13 válidos.
/// PostgreSQL nextval es atómico en varias instancias y no reutiliza números.
/// Los huecos en el consecutivo por carreras/reintentos son intencionales.
/// </summary>
public sealed class CodigoBarrasAsignacionService
{
    private readonly CatalogDbContext _catalog;

    public CodigoBarrasAsignacionService(CatalogDbContext catalog) => _catalog = catalog;

    public async Task<string?> AsignarSiFaltaAsync(Guid productoId, CancellationToken ct = default)
    {
        var actual = await _catalog.Productos.AsNoTracking()
            .Where(p => p.Id == productoId)
            .Select(p => new { p.CodigoEAN })
            .SingleOrDefaultAsync(ct);
        if (actual is null) return null;

        // Preservar incluso identificadores heredados con formato no estándar.
        if (actual.CodigoEAN is { Length: > 0 })
            return actual.CodigoEAN;

        // F402 crea esta secuencia con MAX histórico como origen. Nunca se
        // toca una base si no se ha ejecutado previamente la migración.
        var serial = await _catalog.Database.SqlQueryRaw<long>(
            """SELECT nextval('catalog.codigos_barras_consecutivo') AS "Value" """)
            .SingleAsync(ct);
        var nuevoCodigo = CodigoBarras.GenerarInterno(serial);

        // La actualización condicional decide el ganador si dos instancias
        // reciben la solicitud a la vez para el mismo producto.
        var actualizados = await _catalog.Productos
            .Where(p => p.Id == productoId && (p.CodigoEAN == null || p.CodigoEAN == ""))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CodigoEAN, nuevoCodigo), ct);

        if (actualizados == 1) return nuevoCodigo;

        // Otra instancia pudo ganar; devolver el identificador real guardado.
        return await _catalog.Productos.AsNoTracking()
            .Where(p => p.Id == productoId)
            .Select(p => p.CodigoEAN)
            .SingleOrDefaultAsync(ct);
    }
}
