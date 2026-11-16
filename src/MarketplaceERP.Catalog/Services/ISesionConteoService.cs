using MarketplaceERP.Catalog.Entities;

namespace MarketplaceERP.Catalog.Services;

public sealed record OpcionUbicacion(Guid StockFisicoId, string UbicacionCodigo);

/// <summary>Respuesta a una lectura. Conteo a ciegas: nunca incluye cantidades del sistema.</summary>
public sealed record ResultadoLecturaDto(
    ResultadoLectura Resultado,
    string Mensaje,
    string? Sku,
    string? Nombre,
    string? UbicacionCodigo,
    IReadOnlyList<OpcionUbicacion>? Opciones);

public sealed record SesionConteoDto(
    Guid Id,
    string Numero,
    ModoSesionConteo Modo,
    string? Pasillo,
    EstadoSesionConteo Estado,
    string CreadaPor,
    DateTime FechaInicio,
    DateTime? FechaCierre,
    int Esperados,
    int Contados,
    int Hallazgos);

/// <summary>Producto esperado en la sesión (sin cantidades del sistema).</summary>
public sealed record ItemSesionDto(Guid StockFisicoId, string Sku, string Nombre, string UbicacionCodigo);

public sealed record HallazgoDto(string Sku, string Nombre, string Pasillo, int Cantidad, string Usuario, DateTime Fecha);

public sealed record ResumenSesionDto(
    SesionConteoDto Sesion,
    IReadOnlyList<ItemSesionDto> Faltan,
    IReadOnlyList<ItemSesionDto> Contados,
    IReadOnlyList<ItemSesionDto> Reconteo,
    IReadOnlyList<HallazgoDto> FueraDeLugar);

public sealed record DecisionCierre(Guid StockFisicoId, AccionCierre Accion);

public sealed record OperacionSesionResult(bool Exitoso, string Mensaje, SesionConteoDto? Sesion, string? NumeroSesionExistente = null);

/// <summary>
/// Sesiones de conteo cíclico con escáner. Orquesta las reglas existentes de IConteoCiclicoService;
/// escanear nunca mueve stock: las diferencias confirmadas terminan en ajustes que aprueba un Admin.
/// </summary>
public interface ISesionConteoService
{
    Task<OperacionSesionResult> AbrirRecorridoAsync(string pasillo, string usuario, CancellationToken ct = default);
    Task<OperacionSesionResult> AbrirPlanificadaAsync(string? pasillo, string usuario, CancellationToken ct = default);
    Task<IReadOnlyList<SesionConteoDto>> ListarAsync(EstadoSesionConteo? estado, CancellationToken ct = default);

    /// <summary>Null si la sesión no existe.</summary>
    Task<ResumenSesionDto?> ObtenerResumenAsync(Guid sesionId, CancellationToken ct = default);

    /// <summary>
    /// Registra una lectura en un solo paso. Lanza ArgumentOutOfRangeException si la cantidad es negativa
    /// y KeyNotFoundException si la sesión no existe.
    /// </summary>
    Task<ResultadoLecturaDto> RegistrarLecturaAsync(Guid sesionId, Guid lecturaId, string codigo, int cantidad,
        Guid? stockFisicoId, string usuario, CancellationToken ct = default);

    /// <summary>Exige una decisión por cada producto que falta. KeyNotFoundException si la sesión no existe.</summary>
    Task<OperacionSesionResult> CerrarAsync(Guid sesionId, IReadOnlyList<DecisionCierre> decisiones, string usuario,
        CancellationToken ct = default);

    /// <summary>Deja los conteos registrados como están. KeyNotFoundException si la sesión no existe.</summary>
    Task<OperacionSesionResult> CancelarAsync(Guid sesionId, string usuario, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListarPasillosAsync(CancellationToken ct = default);
}
