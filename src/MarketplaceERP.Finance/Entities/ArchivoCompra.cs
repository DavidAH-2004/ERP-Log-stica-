using System;

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Durable PDF snapshot of a purchase order. Exactly one current document per PO.
/// Stored in PostgreSQL, not the ephemeral Render filesystem.
/// </summary>
public sealed class ArchivoCompra
{
    public Guid Id { get; set; }
    public Guid OrdenCompraId { get; set; }
    public string NumeroPO { get; set; } = string.Empty;
    public Guid ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = string.Empty;
    public string ProveedorNit { get; set; } = string.Empty;
    public string Contacto { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public DateTime FechaCreacionUtc { get; set; }
    public DateTime FechaActualizacionUtc { get; set; }
    public int CantidadItems { get; set; }
    public decimal MontoTotal { get; set; }
    public string Estado { get; set; } = string.Empty;
    public byte[] PdfContenido { get; set; } = Array.Empty<byte>();
    public string PdfSha256 { get; set; } = string.Empty;
    public int VersionPdf { get; set; } = 1;
    public OrdenCompra OrdenCompra { get; set; } = null!;
}
