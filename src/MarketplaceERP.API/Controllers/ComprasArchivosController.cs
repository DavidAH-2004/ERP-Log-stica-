using System.Security.Cryptography;
using MarketplaceERP.API.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/compras/archivos")]
public sealed class ComprasArchivosController : ControllerBase
{
    private readonly FinanceDbContext _finance;
    private readonly CoreDbContext _core;
    private readonly ComprasArchivoPdfService _pdf;

    public ComprasArchivosController(FinanceDbContext finance, CoreDbContext core, ComprasArchivoPdfService pdf)
    {
        _finance = finance;
        _core = core;
        _pdf = pdf;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? numeroPO = null,
        [FromQuery] string? proveedor = null,
        [FromQuery] Guid? proveedorId = null,
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 25,
        CancellationToken ct = default)
    {
        if (pagina < 1 || tamano is < 1 or > 100 || (long)(pagina - 1) * tamano > int.MaxValue
            || numeroPO?.Length > 100 || proveedor?.Length > 100 || (desde.HasValue && hasta.HasValue && desde > hasta))
            return BadRequest(new { mensaje = "Filtros o paginación inválidos." });

        var query = _finance.ArchivosCompras.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(numeroPO))
            query = query.Where(a => a.NumeroPO.Contains(numeroPO.Trim()));
        if (proveedorId.HasValue)
            query = query.Where(a => a.ProveedorId == proveedorId.Value);
        if (!string.IsNullOrWhiteSpace(proveedor))
            query = query.Where(a => a.ProveedorNombre.ToLower().Contains(proveedor.Trim().ToLower()));
        if (desde.HasValue)
        {
            var min = DateTime.SpecifyKind(desde.Value.Date, DateTimeKind.Utc);
            query = query.Where(a => a.FechaCreacionUtc >= min);
        }
        if (hasta.HasValue)
        {
            var end = DateTime.SpecifyKind(hasta.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(a => a.FechaCreacionUtc < end);
        }

        var total = await query.CountAsync(ct);
        var archivos = await query.OrderByDescending(a => a.FechaCreacionUtc).ThenBy(a => a.Id)
            .Skip((pagina - 1) * tamano).Take(tamano)
            .Select(a => new
            {
                a.Id, a.OrdenCompraId, a.NumeroPO, a.ProveedorId, a.ProveedorNombre,
                a.FechaCreacionUtc, a.FechaActualizacionUtc, a.CantidadItems,
                a.MontoTotal, a.Estado, a.VersionPdf
            }).ToListAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { total, pagina, tamano, archivos });
    }

    [HttpGet("{poId:guid}/pdf")]
    public async Task<IActionResult> Documento(
        Guid poId, [FromQuery] bool descargar = false, CancellationToken ct = default)
    {
        var doc = await _finance.ArchivosCompras.AsNoTracking()
            .FirstOrDefaultAsync(a => a.OrdenCompraId == poId, ct);
        if (doc is null) return NotFound(new { mensaje = "La PO aún no tiene documento archivado." });
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(doc.PdfContenido)), doc.PdfSha256,
                StringComparison.OrdinalIgnoreCase))
            return StatusCode(503, new { mensaje = "El PDF archivado no superó la verificación de integridad." });
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var filename = doc.NumeroPO + ".pdf";
        return descargar
            ? File(doc.PdfContenido, "application/pdf", filename)
            : File(doc.PdfContenido, "application/pdf");
    }

    // Bounded, idempotent historical migration. Can be called by the admin page
    // during first use; never recreates an existing document or edits source POs.
    [HttpPost("reconciliar")]
    public async Task<IActionResult> ConciliarHistoricos([FromQuery] int limite = 20, CancellationToken ct = default)
    {
        if (limite is < 1 or > 50) return BadRequest(new { mensaje = "Límite fuera del rango permitido." });
        var pendientes = await _finance.OrdenesCompra.AsNoTracking()
            .Where(po => !_finance.ArchivosCompras.Any(a => a.OrdenCompraId == po.Id))
            .OrderBy(po => po.FechaEmision).Select(po => po.Id).Take(limite).ToListAsync(ct);

        var archivadas = 0;
        foreach (var poId in pendientes)
        {
            var po = await _finance.OrdenesCompra.Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == poId, ct);
            if (po is null) continue;
            var proveedor = await _core.Proveedores.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == po.ProveedorId, ct);
            await _pdf.PrepararAsync(po, proveedor, ct);
            await _finance.SaveChangesAsync(ct);
            archivadas++;
        }
        Response.Headers.CacheControl = "no-store";
        return Ok(new { archivadas, limite });
    }
}
