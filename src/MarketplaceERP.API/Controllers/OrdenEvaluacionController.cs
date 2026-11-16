using System.Security.Claims;
using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/ordenes/evaluacion")]
public sealed class OrdenEvaluacionController(
    OrdenEvaluacionService orders, ILogger<OrdenEvaluacionController> logger) : ControllerBase
{
    [HttpGet("opciones")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await orders.GetOptionsAsync(ct)); }
        catch (EvaluacionOrdenException ex) { return StatusCode(ex.StatusCode, new { mensaje = ex.Message }); }
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var order = await orders.CancelAsync(id, ct);
            logger.LogInformation("Compensación OMS evaluación: Orden={OrdenId}, Actor={Actor}",
                id, User.FindFirstValue(ClaimTypes.NameIdentifier));
            return Ok(new { order.Id, estado = order.Estado.ToString() });
        }
        catch (EvaluacionOrdenException ex) { return StatusCode(ex.StatusCode, new { mensaje = ex.Message }); }
    }
}
