using System.Text.Json.Serialization;

namespace MarketplaceERP.Catalog.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModoSesionConteo { Recorrido, Planificado }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoSesionConteo { Abierta, Cerrada, Cancelada }

/// <summary>Resultado de escanear un producto dentro de una sesión de conteo.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResultadoLectura
{
    Coincide,
    Diferencia,
    AjusteCreado,
    FueraDeLugar,
    ElegirUbicacion,
    BloqueadoPorAjuste,
    YaContado,
    StockCambio,
    CodigoInvalido,
    ProductoNoEncontrado,
    SesionNoAbierta,
    /// <summary>Una regla de inventario impidió registrar el conteo (p. ej. ajuste mayor que lo disponible).</summary>
    Rechazado,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccionCierre { NoHay, Pendiente }

/// <summary>
/// Sesión de conteo con escáner: un recorrido por pasillo o una lista de conteos planificados.
/// Escanear solo registra conteos; nunca mueve stock (los ajustes se aprueban aparte).
/// </summary>
public class SesionConteo
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public ModoSesionConteo Modo { get; set; }
    public string? Pasillo { get; set; }
    public EstadoSesionConteo Estado { get; set; } = EstadoSesionConteo.Abierta;
    public string CreadaPor { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; } = DateTime.UtcNow;
    public DateTime? FechaCierre { get; set; }
    public string? Observaciones { get; set; }
}

/// <summary>Cada lectura registrada en una sesión. El Id lo genera el cliente y sirve de clave de idempotencia.</summary>
public class LecturaConteo
{
    public Guid Id { get; set; }
    public Guid SesionConteoId { get; set; }
    public string CodigoLeido { get; set; } = string.Empty;
    public Guid? ProductoId { get; set; }
    public Guid? StockFisicoId { get; set; }
    public Guid? ConteoCiclicoId { get; set; }
    public int? Cantidad { get; set; }
    public ResultadoLectura Resultado { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

/// <summary>Producto escaneado donde el sistema no lo tiene registrado. No mueve stock.</summary>
public class HallazgoConteo
{
    public Guid Id { get; set; }
    public Guid SesionConteoId { get; set; }
    public Guid ProductoId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NombreProducto { get; set; } = string.Empty;
    public string Pasillo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
