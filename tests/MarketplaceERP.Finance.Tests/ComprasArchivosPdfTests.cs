using System.Text;
using MarketplaceERP.API.Services;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class ComprasArchivosPdfTests
{
    [Fact]
    public async Task UnaPO_UnPDFDuradero_ActualizarEstadoNoDuplica()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("compras-archivos-pdf-" + Guid.NewGuid())
            .Options;
        await using var db = new FinanceDbContext(options);
        var proveedor = new Proveedor
        {
            Id = Guid.NewGuid(), NombreComercial = "Proveedor QA",
            RazonSocial = "Proveedor QA SA de CV", Nit = "TEST-0001",
            ContactoPrincipal = "Contacto QA", Email = "qa@example.invalid",
            Telefono = "0000-0000"
        };
        var po = new OrdenCompra
        {
            Id = Guid.NewGuid(),
            NumeroPO = "PO-QA-ARCHIVO-001",
            ProveedorId = proveedor.Id,
            ProveedorNombre = proveedor.RazonSocial,
            Estado = EstadoOrdenCompra.Borrador,
            FechaEmision = DateTime.UtcNow,
            FechaEntregaEsperada = DateTime.UtcNow.AddDays(15),
            TerminosPago = "Crédito 30 días",
            SubTotal = 125.00m, ImpuestoIva = 16.25m, MontoTotal = 141.25m,
            Items = new List<ItemOrdenCompra>
            {
                new() { Id = Guid.NewGuid(), Sku = "QA-A", NombreProducto = "Producto de prueba A",
                    CantidadSolicitada = 2, PrecioUnitarioAcordado = 25.00m },
                new() { Id = Guid.NewGuid(), Sku = "QA-B", NombreProducto = "Producto de prueba B",
                    CantidadSolicitada = 3, PrecioUnitarioAcordado = 15.00m },
                new() { Id = Guid.NewGuid(), Sku = "QA-C", NombreProducto = "Producto de prueba C",
                    CantidadSolicitada = 1, PrecioUnitarioAcordado = 30.00m }
            }
        };
        db.OrdenesCompra.Add(po);
        var generator = new ComprasArchivoPdfService(db);
        await generator.PrepararAsync(po, proveedor, CancellationToken.None);
        await db.SaveChangesAsync();

        var primero = Assert.Single(db.ArchivosCompras);
        Assert.Equal(po.Id, primero.OrdenCompraId);
        Assert.Equal(3, primero.CantidadItems);
        Assert.Equal(po.MontoTotal, primero.MontoTotal);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(primero.PdfContenido[..8]));
        Assert.True(primero.PdfContenido.Length > 600);
        Assert.Equal(64, primero.PdfSha256.Length);
        Assert.Equal("TEST-0001", primero.ProveedorNit);
        var hashOriginal = primero.PdfSha256;

        po.Estado = EstadoOrdenCompra.Enviada;
        await generator.PrepararAsync(po, null, CancellationToken.None);
        await db.SaveChangesAsync();

        var segundo = Assert.Single(db.ArchivosCompras);
        Assert.Equal("Enviada", segundo.Estado);
        Assert.Equal(2, segundo.VersionPdf);
        Assert.Equal("TEST-0001", segundo.ProveedorNit);
        Assert.NotEqual(hashOriginal, segundo.PdfSha256);
    }
}
