using System.Security.Claims;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace MarketplaceERP.API.Controllers;

public sealed record SolicitarRotacionEanRequest(
    Guid SolicitudId, string CodigoNuevo, string Motivo);

/// <summary>
/// F470: independent Admin requests/approvals for INTERNAL EAN codes only.
/// Both switches remain OFF by default; no payment or catalogue bulk import.
/// </summary>
[ApiController]
[Route("api/productos")]
[Authorize(Roles = "Admin")]
public sealed class RotacionesEanController : ControllerBase
{
    private readonly RotacionEanService _service;
    private readonly bool _habilitada;

    public RotacionesEanController(RotacionEanService service, IConfiguration configuration)
    {
        _service = service;
        // An old-label-compatible read path must be enabled before any
        // successful rotation. Without both flags the route fails closed.
        _habilitada =
            configuration.GetValue<bool>("Barcodes:EnableHistoricalAliasLookup") &&
            configuration.GetValue<bool>("Barcodes:EnableEanRotation");
    }

    [HttpGet("{productoId:guid}/codigo-barras/rotaciones")]
    public async Task<IActionResult> Historial(Guid productoId, CancellationToken ct)
    {
        if (!_habilitada) return Inactiva();
        if (!ActorAdmin(out _)) return Unauthorized(new { mensaje = "Admin autenticado requerido." });
        try
        {
            var eventos = await _service.HistorialAsync(productoId, ct);
            Response.Headers.CacheControl = "no-store";
            return Ok(eventos);
        }
        catch (ArgumentException)
        {
            return BadRequest(new { mensaje = "Producto inválido." });
        }
        catch (PostgresException ex)
        {
            return MapearSql(ex);
        }
    }

    [HttpPost("{productoId:guid}/codigo-barras/rotaciones")]
    public async Task<IActionResult> Solicitar(
        Guid productoId, [FromBody] SolicitarRotacionEanRequest request,
        CancellationToken ct)
    {
        if (!_habilitada) return Inactiva();
        if (request is null || !ActorAdmin(out var actor) || productoId == Guid.Empty)
            return Unauthorized(new { mensaje = "Administrador identificado requerido." });

        try
        {
            var estado = await _service.SolicitarAsync(
                request.SolicitudId, productoId, request.CodigoNuevo,
                request.Motivo, actor, ct);
            Response.Headers.CacheControl = "no-store";
            return Ok(new { solicitudId = request.SolicitudId, productoId, estado });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { mensaje = "Solicitud de rotación inválida." });
        }
        catch (PostgresException ex)
        {
            return MapearSql(ex);
        }
    }

    [HttpPost("codigo-barras/rotaciones/{solicitudId:guid}/aprobar")]
    public async Task<IActionResult> Aprobar(Guid solicitudId, CancellationToken ct)
    {
        if (!_habilitada) return Inactiva();
        if (!ActorAdmin(out var actor))
            return Unauthorized(new { mensaje = "Administrador identificado requerido." });

        try
        {
            var estado = await _service.AprobarAsync(solicitudId, actor, ct);
            Response.Headers.CacheControl = "no-store";
            return Ok(new { solicitudId, estado });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { mensaje = "Solicitud inválida." });
        }
        catch (PostgresException ex)
        {
            return MapearSql(ex);
        }
    }

    [HttpPost("codigo-barras/rotaciones/{solicitudId:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid solicitudId, CancellationToken ct)
    {
        if (!_habilitada) return Inactiva();
        if (!ActorAdmin(out var actor))
            return Unauthorized(new { mensaje = "Admin autenticado requerido." });
        try
        {
            var estado = await _service.CancelarAsync(solicitudId, actor, ct);
            Response.Headers.CacheControl = "no-store";
            return Ok(new { solicitudId, estado });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { mensaje = "Solicitud inválida." });
        }
        catch (PostgresException ex)
        {
            return MapearSql(ex);
        }
    }

    private bool ActorAdmin(out Guid actor)
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out actor) && actor != Guid.Empty;
    }

    private IActionResult Inactiva() => StatusCode(
        StatusCodes.Status503ServiceUnavailable,
        new { mensaje = "Rotaciones inhabilitadas: falta certificación de migración, alias y hardware." });

    private IActionResult MapearSql(PostgresException ex) => ex.SqlState switch
    {
        "P0002" => NotFound(new { mensaje = "Solicitud o producto inexistente." }),
        "23505" => Conflict(new { mensaje = "EAN reservado, solicitud duplicada o carrera concurrente." }),
        "23514" => Conflict(new { mensaje = "Rotación rechazada por política, estado o doble aprobación." }),
        "42883" or "42P01" or "42703" => StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new { mensaje = "Esquema de rotaciones todavía no preparado." }),
        _ => throw ex
    };
}
