using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

public record AbrirRecorridoRequest(string Pasillo);
public record AbrirPlanificadaRequest(string? Pasillo);
public record LecturaRequest(Guid LecturaId, string Codigo, int Cantidad, Guid? StockFisicoId);
public record CerrarSesionRequest(List<DecisionCierre> Decisiones);

/// <summary>
/// Conteos cíclicos con escáner: sesiones por pasillo o planificadas.
/// Escanear solo registra conteos; las diferencias confirmadas terminan en ajustes que aprueba un Admin.
/// </summary>
[ApiController]
[Route("api/inventario/conteos-ciclicos/sesiones")]
[Authorize(Roles = "Admin")]
public class SesionesConteoController : ControllerBase
{
    private readonly ISesionConteoService _sesiones;

    public SesionesConteoController(ISesionConteoService sesiones)
    {
        _sesiones = sesiones;
    }

    private string Usuario => User.Identity?.Name ?? "desconocido";

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] EstadoSesionConteo? estado, CancellationToken ct) =>
        Ok(await _sesiones.ListarAsync(estado, ct));

    [HttpGet("pasillos")]
    public async Task<IActionResult> Pasillos(CancellationToken ct) => Ok(await _sesiones.ListarPasillosAsync(ct));

    [HttpPost("recorrido")]
    public async Task<IActionResult> AbrirRecorrido([FromBody] AbrirRecorridoRequest request, CancellationToken ct)
    {
        var r = await _sesiones.AbrirRecorridoAsync(request.Pasillo, Usuario, ct);
        if (r.Exitoso)
            return CreatedAtAction(nameof(Resumen), new { id = r.Sesion!.Id }, r.Sesion);
        return r.NumeroSesionExistente is not null
            ? Conflict(new { mensaje = r.Mensaje, numeroSesionExistente = r.NumeroSesionExistente })
            : BadRequest(new { mensaje = r.Mensaje });
    }

    [HttpPost("planificada")]
    public async Task<IActionResult> AbrirPlanificada([FromBody] AbrirPlanificadaRequest request, CancellationToken ct)
    {
        var r = await _sesiones.AbrirPlanificadaAsync(request.Pasillo, Usuario, ct);
        return r.Exitoso
            ? CreatedAtAction(nameof(Resumen), new { id = r.Sesion!.Id }, r.Sesion)
            : BadRequest(new { mensaje = r.Mensaje });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Resumen(Guid id, CancellationToken ct)
    {
        var resumen = await _sesiones.ObtenerResumenAsync(id, ct);
        return resumen is null ? NotFound(new { mensaje = "Sesión no encontrada." }) : Ok(resumen);
    }

    [HttpPost("{id:guid}/lecturas")]
    public async Task<IActionResult> Leer(Guid id, [FromBody] LecturaRequest request, CancellationToken ct)
    {
        if (request.LecturaId == Guid.Empty)
            return BadRequest(new { mensaje = "Falta el identificador de la lectura." });
        if (request.Cantidad < 0)
            return BadRequest(new { mensaje = "La cantidad no puede ser negativa." });

        try
        {
            return Ok(await _sesiones.RegistrarLecturaAsync(
                id, request.LecturaId, request.Codigo, request.Cantidad, request.StockFisicoId, Usuario, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { mensaje = "Sesión no encontrada." });
        }
    }

    [HttpPost("{id:guid}/cerrar")]
    public async Task<IActionResult> Cerrar(Guid id, [FromBody] CerrarSesionRequest request, CancellationToken ct)
    {
        try
        {
            var r = await _sesiones.CerrarAsync(id, request.Decisiones ?? new List<DecisionCierre>(), Usuario, ct);
            return r.Exitoso
                ? Ok(await _sesiones.ObtenerResumenAsync(id, ct))
                : BadRequest(new { mensaje = r.Mensaje });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { mensaje = "Sesión no encontrada." });
        }
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        try
        {
            var r = await _sesiones.CancelarAsync(id, Usuario, ct);
            return r.Exitoso ? Ok(r.Sesion) : BadRequest(new { mensaje = r.Mensaje });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { mensaje = "Sesión no encontrada." });
        }
    }
}
