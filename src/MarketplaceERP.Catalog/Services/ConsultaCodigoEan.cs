using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Services;

/// <summary>Resultado de búsqueda de un código actual o histórico.</summary>
public sealed record ResultadoConsultaCodigoEan(
    Guid Id, string Sku, string Nombre, string? CodigoEAN,
    string CodigoLeido, bool EsAlias);

/// <summary>
/// F457: lectura parametrizada del registro irrevocable F451.
/// Alias OFF by default: no dependence on the new table in existing environments.
/// Feature flag ON is only valid after F451 migration/backfill certification.
/// DOES NOT enable rotation, inventory mutation or historical label printing.
/// </summary>
public static class ConsultaCodigoEan
{
    public static async Task<List<ResultadoConsultaCodigoEan>> BuscarAsync(
        CatalogDbContext db, string codigo, Guid? vendedorId,
        bool permitirHistoricos, CancellationToken ct = default)
    {
        if (!CodigoBarras.EsEan13Valido(codigo))
            throw new ArgumentException("Código EAN-13 inválido.", nameof(codigo));

        IQueryable<Producto> consulta = permitirHistoricos
            ? db.Productos.FromSqlInterpolated($"""
                SELECT p.*
                FROM catalog.productos AS p
                INNER JOIN catalog.reservas_ean AS r ON r."ProductoId" = p."Id"
                WHERE r."CodigoEAN" = {codigo}
                  AND r."Estado" IN ('Actual', 'Alias')
                """)
            : db.Productos.Where(p => p.CodigoEAN == codigo);

        // En ambos modos el vendedor autenticado restringe los resultados
        // ANTES de materializar el producto: sin filtrado en memoria.
        if (vendedorId.HasValue)
            consulta = consulta.Where(p => p.VendedorId == vendedorId.Value);

        var productos = await consulta.AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Sku, p.Nombre, p.CodigoEAN })
            .Take(2)
            .ToListAsync(ct);

        return productos
            .Select(p => new ResultadoConsultaCodigoEan(
                p.Id, p.Sku, p.Nombre, p.CodigoEAN, codigo,
                !string.Equals(p.CodigoEAN, codigo, StringComparison.Ordinal)))
            .ToList();
    }
}
