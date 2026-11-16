/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Services;
using MarketplaceERP.Core.Entities;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Npgsql;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Vista logística de proveedores. Consume el maestro único core.proveedores;
/// no crea ni duplica proveedores desde Catalog o Finance.
/// </summary>
[ApiController]
[Route("api/proveedores")]
[Authorize(Roles = "Admin")]
public class ProveedoresController : ControllerBase
{
    private readonly CoreDbContext _coreDb;
    private readonly CatalogDbContext _catalogDb;
    private readonly FinanceDbContext _financeDb;

    public ProveedoresController(CoreDbContext coreDb, CatalogDbContext catalogDb, FinanceDbContext financeDb)
    {
        _coreDb = coreDb;
        _catalogDb = catalogDb;
        _financeDb = financeDb;
    }

    /// <summary>Admin-only registration in core.proveedores.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Registrar(
        [FromBody] RegistrarProveedorRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { mensaje = "Introduce los datos del proveedor." });

        static string Clean(string? value) =>
            Regex.Replace(value?.Trim() ?? "", @"\s+", " ");
        static bool Fits(string value, int max) => value.Length <= max;

        var nombre = Clean(request.NombreComercial);
        var razon = Clean(request.RazonSocial);
        var categoria = Clean(request.CategoriaSuministro);
        var contacto = Clean(request.ContactoPrincipal);
        var email = (request.Email ?? "").Trim();
        var telefono = (request.Telefono ?? "").Trim();
        var direccion = Clean(request.Direccion);
        var nrc = (request.Nrc ?? "").Trim();
        var nit = Regex.Replace((request.Nit ?? "").Trim(), @"[\s-]+", "");
        var condicion = Clean(request.CondicionesPago).ToLowerInvariant();

        if (nombre.Length == 0 || razon.Length == 0)
            return BadRequest(new { mensaje = "Nombre comercial y razón social son obligatorios." });
        if (!Fits(nombre, 200) || !Fits(razon, 250) || !Fits(categoria, 100) ||
            !Fits(contacto, 150) || !Fits(email, 254) || !Fits(telefono, 30) ||
            !Fits(direccion, 300) || !Fits(nrc, 50))
            return BadRequest(new { mensaje = "Uno de los campos excede la longitud permitida." });
        if (nit.Length != 0 && !Regex.IsMatch(nit, @"\A[0-9]{14}\z"))
            return BadRequest(new { mensaje = "El NIT debe contener 14 dígitos (con o sin guiones)." });
        if (email.Length > 0 && !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { mensaje = "Ingresa un correo electrónico válido." });
        if (telefono.Length > 0 &&
            (!Regex.IsMatch(telefono, @"\A\+?[0-9()\-\s]{7,30}\z") ||
             telefono.Count(char.IsAsciiDigit) < 7))
            return BadRequest(new { mensaje = "El teléfono debe contener al menos 7 dígitos y un formato válido." });
        if (condicion is not ("contado" or "credito" or "crédito"))
            return BadRequest(new { mensaje = "Selecciona contado o crédito como condición de pago." });
        if (request.DiasCredito < 0 || request.DiasCredito > 3650 ||
            (condicion == "contado" && request.DiasCredito != 0) ||
            (condicion != "contado" && request.DiasCredito == 0))
            return BadRequest(new { mensaje = "Para contado usa 0 días; para crédito indica entre 1 y 3650 días." });

        var nombreKey = nombre.ToLowerInvariant();
        var razonKey = razon.ToLowerInvariant();
        var duplicado = await _coreDb.Proveedores.AsNoTracking().AnyAsync(
            p => p.NombreComercial.Trim().ToLower() == nombreKey ||
                 p.RazonSocial.Trim().ToLower() == razonKey ||
                 (nit != "" && p.Nit.Replace("-", "").Replace(" ", "") == nit),
            cancellationToken);
        if (duplicado)
            return Conflict(new { mensaje = "Ya existe un proveedor con ese nombre, razón social o NIT." });

        var proveedor = new Proveedor
        {
            Id = Guid.NewGuid(),
            NombreComercial = nombre,
            RazonSocial = razon,
            Nit = nit,
            Nrc = nrc,
            CategoriaSuministro = categoria,
            ContactoPrincipal = contacto,
            Email = email,
            Telefono = telefono,
            Direccion = direccion,
            PlazoCreditoDias = request.DiasCredito,
            Activo = request.Activo,
            OtifHistorico = null,
            FechaRegistro = DateTime.UtcNow
        };
        _coreDb.Proveedores.Add(proveedor);
        try
        {
            await _coreDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when
            (ex.InnerException is PostgresException pg &&
             pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _coreDb.Entry(proveedor).State = EntityState.Detached;
            return Conflict(new { mensaje = "Ya existe un proveedor con ese nombre, razón social o NIT." });
        }

        Response.Headers.CacheControl = "no-store";
        return Created($"/api/proveedores/{proveedor.Id}", new
        {
            proveedor.Id,
            proveedor.NombreComercial,
            proveedor.RazonSocial,
            proveedor.Nit,
            proveedor.Nrc,
            proveedor.CategoriaSuministro,
            proveedor.ContactoPrincipal,
            proveedor.Email,
            proveedor.Telefono,
            proveedor.Direccion,
            proveedor.PlazoCreditoDias,
            proveedor.Activo,
            proveedor.OtifHistorico,
            proveedor.FechaRegistro
        });
    }

    [HttpGet("logistica")]
    public async Task<IActionResult> ListarLogistica(
        [FromQuery] string? buscar = null,
        CancellationToken cancellationToken = default)
    {
        var proveedoresQuery = _coreDb.Proveedores
            .AsNoTracking()
            .Where(p => p.Activo);

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var termino = buscar.Trim().ToLower();
            proveedoresQuery = proveedoresQuery.Where(p =>
                p.NombreComercial.ToLower().Contains(termino) ||
                p.RazonSocial.ToLower().Contains(termino) ||
                p.CategoriaSuministro.ToLower().Contains(termino));
        }

        var proveedores = await proveedoresQuery
            .OrderBy(p => p.NombreComercial)
            .ToListAsync(cancellationToken);

        var todosProveedoresActivos = await _coreDb.Proveedores
            .AsNoTracking()
            .Where(p => p.Activo)
            .ToListAsync(cancellationToken);

        var productos = await _catalogDb.Productos
            .AsNoTracking()
            .Include(p => p.Stocks)
            .Where(p => p.EstaPublicado && p.EstadoProducto == "Activo")
            .ToListAsync(cancellationToken);

        var posAbiertas = await _financeDb.OrdenesCompra
            .AsNoTracking()
            .Include(po => po.Items)
            .Where(po => po.Estado == EstadoOrdenCompra.Borrador ||
                         po.Estado == EstadoOrdenCompra.Enviada ||
                         po.Estado == EstadoOrdenCompra.RecibidaParcial)
            .ToListAsync(cancellationToken);

        var vinculaciones = productos
            .Select(p => new
            {
                Producto = p,
                Proveedor = ProveedorLogisticaMatcher.Resolver(todosProveedoresActivos, p.ProveedorNombre)
            })
            .ToList();

        var items = proveedores.Select(proveedor =>
        {
            var productosProveedor = vinculaciones
                .Where(x => x.Proveedor?.Id == proveedor.Id)
                .Select(x => x.Producto)
                .ToList();

            var bajoMinimo = productosProveedor.Count(p =>
            {
                var disponible = p.Stocks.Sum(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada);
                return p.StockMinimo > 0 && disponible <= p.StockMinimo;
            });

            var compras = posAbiertas.Where(po => po.ProveedorId == proveedor.Id).ToList();
            var pendienteRecepcion = compras
                .SelectMany(po => po.Items)
                .Sum(i => Math.Max(0, i.CantidadSolicitada - i.CantidadRecibida));

            return new
            {
                proveedor.Id,
                proveedor.NombreComercial,
                proveedor.RazonSocial,
                proveedor.CategoriaSuministro,
                proveedor.ContactoPrincipal,
                proveedor.Email,
                proveedor.Telefono,
                proveedor.Direccion,
                proveedor.PlazoCreditoDias,
                proveedor.OtifHistorico,
                ProductosCatalogo = productosProveedor.Count,
                ProductosBajoMinimo = bajoMinimo,
                OrdenesCompraAbiertas = compras.Count,
                UnidadesPendientesRecepcion = pendienteRecepcion
            };
        }).ToList();

        var nombresNoVinculados = vinculaciones
            .Where(x => x.Proveedor is null && !string.IsNullOrWhiteSpace(x.Producto.ProveedorNombre))
            .Select(x => x.Producto.ProveedorNombre.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();

        return Ok(new
        {
            Total = items.Count,
            ConProductosCatalogo = items.Count(x => x.ProductosCatalogo > 0),
            ConCompraAbierta = items.Count(x => x.OrdenesCompraAbiertas > 0),
            NombresCatalogoNoVinculados = nombresNoVinculados,
            Items = items
        });
    }
}

public sealed record RegistrarProveedorRequest(
    string? NombreComercial,
    string? RazonSocial,
    string? Nit,
    string? Nrc,
    string? CategoriaSuministro,
    string? ContactoPrincipal,
    string? Email,
    string? Telefono,
    string? Direccion,
    string? CondicionesPago,
    int DiasCredito,
    bool Activo = true);
