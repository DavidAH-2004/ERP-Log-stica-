using System.Globalization;
using System.Security.Cryptography;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Makes a self-contained printable snapshot of a purchase order.
/// The API invokes this before committing a PO so both persist atomically.
/// </summary>
public sealed class ComprasArchivoPdfService
{
    private readonly FinanceDbContext _db;
    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    public ComprasArchivoPdfService(FinanceDbContext db) => _db = db;

    public async Task PrepararAsync(OrdenCompra po, Proveedor? proveedor, CancellationToken ct)
    {
        var existing = await _db.ArchivosCompras
            .FirstOrDefaultAsync(x => x.OrdenCompraId == po.Id, ct);
        var pdf = CrearPdf(po, proveedor, existing);
        if (pdf.Length < 100 || pdf.Length > 2_000_000)
            throw new InvalidOperationException("Tamaño PDF de compra fuera del límite permitido.");

        var now = DateTime.UtcNow;
        var archive = existing ?? new ArchivoCompra
        {
            Id = Guid.NewGuid(),
            OrdenCompraId = po.Id,
            FechaCreacionUtc = now
        };
        archive.NumeroPO = po.NumeroPO;
        archive.ProveedorId = po.ProveedorId;
        archive.ProveedorNombre = po.ProveedorNombre;
        archive.ProveedorNit = proveedor?.Nit ?? existing?.ProveedorNit ?? string.Empty;
        archive.Contacto = proveedor?.ContactoPrincipal ?? existing?.Contacto ?? string.Empty;
        archive.Correo = proveedor?.Email ?? existing?.Correo ?? string.Empty;
        archive.Telefono = proveedor?.Telefono ?? existing?.Telefono ?? string.Empty;
        archive.CantidadItems = po.Items.Count;
        archive.MontoTotal = po.MontoTotal;
        archive.Estado = po.Estado.ToString();
        archive.PdfContenido = pdf;
        archive.PdfSha256 = Convert.ToHexString(SHA256.HashData(pdf));
        archive.FechaActualizacionUtc = now;
        if (existing is null) _db.ArchivosCompras.Add(archive);
        else archive.VersionPdf++;
    }

    private static byte[] CrearPdf(OrdenCompra po, Proveedor? proveedor, ArchivoCompra? previous)
    {
        // The Community license is used for the educational/evaluation build only.
        // Commercial rollout must confirm the organisation qualifies or supply a paid license.
        QuestPDF.Settings.License = LicenseType.Community;
        string nit = proveedor?.Nit ?? previous?.ProveedorNit ?? string.Empty;
        string contacto = proveedor?.ContactoPrincipal ?? previous?.Contacto ?? string.Empty;
        string email = proveedor?.Email ?? previous?.Correo ?? string.Empty;
        string telefono = proveedor?.Telefono ?? previous?.Telefono ?? string.Empty;
        string Money(decimal value) => "$" + value.ToString("N2", Usd);
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(35);
                page.DefaultTextStyle(x => x.FontSize(9));
                page.Header().Column(h =>
                {
                    h.Item().Row(row =>
                    {
                        row.RelativeItem().Text("LOGIMKT").Bold().FontSize(22).FontColor(Colors.Blue.Darken2);
                        row.ConstantItem(200).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("ORDEN DE COMPRA").Bold().FontSize(13);
                            c.Item().AlignRight().Text(po.NumeroPO).FontSize(8);
                        });
                    });
                    h.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });
                page.Content().PaddingVertical(18).Column(col =>
                {
                    col.Spacing(13);
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("PROVEEDOR").Bold();
                            c.Item().Text(po.ProveedorNombre);
                            c.Item().Text("NIT: " + (string.IsNullOrWhiteSpace(nit) ? "No registrado" : nit));
                            c.Item().Text("Contacto: " + contacto);
                            c.Item().Text("Correo: " + email);
                            c.Item().Text("Teléfono: " + telefono);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("CONDICIONES").Bold();
                            c.Item().Text("Fecha: " + po.FechaEmision.ToString("dd/MM/yyyy HH:mm 'UTC'"));
                            c.Item().Text("Pago: " + po.TerminosPago);
                            c.Item().Text("Entrega prevista: " + po.FechaEntregaEsperada?.ToString("dd/MM/yyyy"));
                            c.Item().Text("Estado: " + po.Estado);
                        });
                    });
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(5);
                            c.RelativeColumn(1);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });
                        t.Header(h =>
                        {
                            foreach (var label in new[] { "Producto / SKU", "Cant.", "Precio unit.", "Subtotal" })
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text(label).Bold();
                        });
                        foreach (var item in po.Items.OrderBy(x => x.Sku))
                        {
                            t.Cell().Padding(5).Text(item.NombreProducto + " (" + item.Sku + ")");
                            t.Cell().Padding(5).AlignRight().Text(item.CantidadSolicitada.ToString());
                            t.Cell().Padding(5).AlignRight().Text(Money(item.PrecioUnitarioAcordado));
                            t.Cell().Padding(5).AlignRight().Text(Money(item.CantidadSolicitada * item.PrecioUnitarioAcordado));
                        }
                    });
                    col.Item().AlignRight().Column(c =>
                    {
                        c.Item().Text("Subtotal: " + Money(po.SubTotal));
                        c.Item().Text("IVA 13%: " + Money(po.ImpuestoIva));
                        c.Item().Text("TOTAL: " + Money(po.MontoTotal)).Bold().FontSize(13);
                    });
                    if (!string.IsNullOrWhiteSpace(po.Observaciones))
                        col.Item().Text("Observaciones: " + po.Observaciones);
                    col.Item().PaddingTop(28).Row(row =>
                    {
                        row.RelativeItem().Column(c => { c.Item().LineHorizontal(1); c.Item().AlignCenter().Text("Elaborado por"); });
                        row.ConstantItem(28);
                        row.RelativeItem().Column(c => { c.Item().LineHorizontal(1); c.Item().AlignCenter().Text("Autorizado por"); });
                    });
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("LOGIMKT · Generado " + DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'") + " · Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
