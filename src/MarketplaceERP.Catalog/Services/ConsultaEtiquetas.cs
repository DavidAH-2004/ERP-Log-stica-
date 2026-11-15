using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Services;

/// <summary>Lo que se imprime en una etiqueta de código de barras.</summary>
public sealed record EtiquetaProducto(Guid Id, string Sku, string Nombre, string CodigoEAN);

/// <summary>Carga en una sola consulta los datos para imprimir etiquetas.</summary>
public static class ConsultaEtiquetas
{
    /// <summary>
    /// Sin <paramref name="ids"/>: todos los productos publicados con código NO NULO ni vacío.
    /// Con <paramref name="ids"/>: exactamente esos productos (los inexistentes se ignoran);
    /// la pantalla avisa cuáles no tienen código. Siempre ordenados por SKU.
    /// </summary>
    public static async Task<List<EtiquetaProducto>> ObtenerAsync(
        CatalogDbContext db, IReadOnlyCollection<Guid>? ids,
        Guid? vendedorId = null, CancellationToken ct = default)
    {
        if (ids is { Count: > 100 })
            throw new ArgumentOutOfRangeException(nameof(ids), "Máximo 100 productos seleccionados.");

        var consulta = db.Productos.AsNoTracking();
        if (vendedorId.HasValue)
            consulta = consulta.Where(p => p.VendedorId == vendedorId.Value);

        consulta = ids is null
            ? consulta.Where(p => p.EstaPublicado && p.CodigoEAN != null && p.CodigoEAN != "")
            : consulta.Where(p => ids.Contains(p.Id));

        return await consulta
            .OrderBy(p => p.Sku)
            .Select(p => new EtiquetaProducto(p.Id, p.Sku, p.Nombre, p.CodigoEAN ?? string.Empty))
            .Take(501) // Límite de seguridad: la API rechaza listados mayores de 500.
            .ToListAsync(ct);
    }
}
