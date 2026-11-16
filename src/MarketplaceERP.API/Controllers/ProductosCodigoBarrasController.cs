using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// F397: consultas de códigos/etiquetas de solo lectura.
/// Conserva TODOS los EAN existentes; no crea, modifica ni normaliza productos.
/// La separación por vendedor es obligatoria incluso para solicitudes por ID.
/// </summary>
[ApiController]
[Route("api/productos")]
[Authorize(Roles = "Admin,Vendedor")]
public sealed class ProductosCodigoBarrasController : ControllerBase
{
    private readonly CatalogDbContext _catalog;
    private readonly bool _permitirHistoricos;

    public ProductosCodigoBarrasController(CatalogDbContext catalog,
        IConfiguration? configuration = null)
    {
        _catalog = catalog;
        // OFF unless F451 schema, scan isolation and rollback were certified.
        _permitirHistoricos = configuration?.GetValue<bool>("Barcodes:EnableHistoricalAliasLookup") ?? false;
    }

    [HttpGet("etiquetas")]
    public async Task<IActionResult> Etiquetas([FromQuery] string? ids, CancellationToken ct)
    {
        Guid? vendedor = null;
        if (!User.IsInRole("Admin"))
        {
            vendedor = ObtenerVendedorActual();
            if (!vendedor.HasValue) return Forbid();
        }

        IReadOnlyCollection<Guid>? seleccionados = null;
        if (ids is not null)
        {
            var textos = ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (textos.Length is < 1 or > 100 ||
                textos.Any(texto => !Guid.TryParse(texto, out var id) || id == Guid.Empty))
                return BadRequest(new { mensaje = "Selecciona entre 1 y 100 identificadores válidos." });

            seleccionados = textos.Select(Guid.Parse).Distinct().ToArray();
        }

        var etiquetas = await ConsultaEtiquetas.ObtenerAsync(_catalog, seleccionados, vendedor, ct);
        if (etiquetas.Count > 500)
            return BadRequest(new { mensaje = "El listado supera 500 productos. Selecciona un lote menor." });

        Response.Headers.CacheControl = "no-store";
        return Ok(etiquetas);
    }

    [HttpGet("por-codigo/{codigo}")]
    public async Task<IActionResult> BuscarPorCodigo(string codigo, CancellationToken ct)
    {
        if (!CodigoBarras.EsEan13Valido(codigo))
            return BadRequest(new { mensaje = "Se requiere un EAN-13 con dígito verificador válido." });

        Guid? vendedor = null;
        if (!User.IsInRole("Admin"))
        {
            vendedor = ObtenerVendedorActual();
            if (!vendedor.HasValue) return Forbid();
        }

        List<ResultadoConsultaCodigoEan> coincidencias;
        try
        {
            coincidencias = await ConsultaCodigoEan.BuscarAsync(
                _catalog, codigo, vendedor, _permitirHistoricos, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01" && _permitirHistoricos)
        {
            // Feature switch was enabled before the schema migration.
            // Never return unverified legacy results as a fallback.
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "Historial de EAN no disponible hasta completar la migración." });
        }

        if (coincidencias.Count == 0) return NotFound(new { mensaje = "Código no encontrado." });
        if (coincidencias.Count > 1)
            return Conflict(new { mensaje = "Código duplicado: requiere revisión antes de escanear." });
        Response.Headers.CacheControl = "no-store";
        return Ok(coincidencias[0]);
    }

    /// <summary>
    /// F402: asigna un consecutivo interno SOLO a productos que no tienen EAN.
    /// Jamás renumera identificadores existentes. Requiere Admin y una
    /// migración PostgreSQL aditiva previa; no se invoca desde startup.
    /// </summary>
    [HttpPost("{id:guid}/codigo-barras/asignar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AsignarCodigoInterno(
        Guid id, [FromServices] CodigoBarrasAsignacionService asignacion, CancellationToken ct)
    {
        if (id == Guid.Empty) return BadRequest(new { mensaje = "Identificador inválido." });

        var codigo = await asignacion.AsignarSiFaltaAsync(id, ct);
        if (codigo is null) return NotFound(new { mensaje = "Producto no encontrado." });

        Response.Headers.CacheControl = "no-store";
        return Ok(new { codigoEAN = codigo });
    }

    private Guid? ObtenerVendedorActual()
    {
        var valores = User.FindAll("VendedorId")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();
        if (valores.Length == 0 ||
            valores.Any(v => !Guid.TryParse(v, out var id) || id == Guid.Empty))
            return null;

        var ids = valores.Select(Guid.Parse).Distinct().ToArray();
        return ids.Length == 1 ? ids[0] : null;
    }
}
