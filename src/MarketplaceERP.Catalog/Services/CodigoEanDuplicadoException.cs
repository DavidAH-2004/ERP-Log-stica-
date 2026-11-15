namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// F420: conflicto de identificador en una importación atómica.
/// No incluye el valor del EAN para evitar filtrarlo en errores o logs.
/// </summary>
public sealed class CodigoEanDuplicadoException : Exception
{
    public CodigoEanDuplicadoException(string? sku = null)
        : base(sku is null
            ? "EAN ya asignado a otro producto; reintentar tras revisión del catálogo."
            : $"SKU {sku}: EAN ya asignado a otro producto; el lote no fue importado.")
    {
    }
}
