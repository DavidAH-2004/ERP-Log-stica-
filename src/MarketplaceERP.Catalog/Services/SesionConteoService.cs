using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Services;

/// <inheritdoc />
internal sealed class SesionConteoService : ISesionConteoService
{
    private static readonly EstadoConteoCiclico[] EstadosActivos =
        { EstadoConteoCiclico.Planificado, EstadoConteoCiclico.EnConteo, EstadoConteoCiclico.RequiereReconteo };

    private static readonly ResultadoLectura[] ResultadosContados =
        { ResultadoLectura.Coincide, ResultadoLectura.AjusteCreado };

    private readonly CatalogDbContext _db;
    private readonly IConteoCiclicoService _conteos;
    public SesionConteoService(CatalogDbContext db, IConteoCiclicoService conteos)
    {
        _db = db;
        _conteos = conteos;
    }

    // ── Apertura ─────────────────────────────────────────────────────────────

    public async Task<OperacionSesionResult> AbrirRecorridoAsync(string pasillo, string usuario, CancellationToken ct = default)
    {
        pasillo = (pasillo ?? string.Empty).Trim();
        if (!await _db.UbicacionesAlmacen.AnyAsync(u => u.Pasillo == pasillo, ct))
            return new(false, "El pasillo no existe.", null);

        var abierta = await _db.SesionesConteo.FirstOrDefaultAsync(s =>
            s.Modo == ModoSesionConteo.Recorrido && s.Estado == EstadoSesionConteo.Abierta && s.Pasillo == pasillo, ct);
        if (abierta is not null)
            return new(false, "Ya hay un recorrido abierto en este pasillo.", null, abierta.Numero);

        var sesion = await NuevaSesionAsync(ModoSesionConteo.Recorrido, pasillo, usuario, ct);
        await _db.SaveChangesAsync(ct);
        return new(true, "Recorrido iniciado.", await MapearAsync(sesion, ct));
    }

    public async Task<OperacionSesionResult> AbrirPlanificadaAsync(string? pasillo, string usuario, CancellationToken ct = default)
    {
        pasillo = string.IsNullOrWhiteSpace(pasillo) ? null : pasillo.Trim();

        var consulta = _db.ConteosCiclicos.Where(c => c.SesionConteoId == null && EstadosActivos.Contains(c.Estado));
        if (pasillo is not null)
        {
            var stocksDelPasillo = StocksDelPasillo(pasillo).Select(s => s.Id);
            consulta = consulta.Where(c => stocksDelPasillo.Contains(c.StockFisicoId));
        }

        var pendientes = await consulta.ToListAsync(ct);
        if (pendientes.Count == 0)
            return new(false, "No hay conteos planificados pendientes.", null);

        var sesion = await NuevaSesionAsync(ModoSesionConteo.Planificado, pasillo, usuario, ct);
        foreach (var conteo in pendientes)
            conteo.SesionConteoId = sesion.Id;

        await _db.SaveChangesAsync(ct);
        return new(true, "Sesión de conteos planificados iniciada.", await MapearAsync(sesion, ct));
    }

    private async Task<SesionConteo> NuevaSesionAsync(ModoSesionConteo modo, string? pasillo, string usuario, CancellationToken ct)
    {
        var sesion = new SesionConteo
        {
            Id = Guid.NewGuid(),
            Numero = await SiguienteNumeroAsync(ct),
            Modo = modo,
            Pasillo = pasillo,
            Estado = EstadoSesionConteo.Abierta,
            CreadaPor = usuario,
            FechaInicio = DateTime.UtcNow,
        };
        _db.SesionesConteo.Add(sesion);
        return sesion;
    }

    private async Task<string> SiguienteNumeroAsync(CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            // F399: secuencia compartida PostgreSQL, evita Count()+1 entre instancias.
            var numero = await _db.Database.SqlQueryRaw<long>(
                """SELECT nextval('catalog.sesiones_conteo_consecutivo') AS "Value" """)
                .SingleAsync(ct);
            return $"SC-{DateTime.UtcNow.Year}-{numero:D6}";
        }

        var prefijo = $"SC-{DateTime.UtcNow.Year}-";
        var numeros = await _db.SesionesConteo
            .Where(s => s.Numero.StartsWith(prefijo))
            .Select(s => s.Numero)
            .ToListAsync(ct);

        var ultimo = numeros
            .Select(n => int.TryParse(n[prefijo.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefijo}{ultimo + 1:D4}";
    }

    // ── Consultas ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SesionConteoDto>> ListarAsync(EstadoSesionConteo? estado, CancellationToken ct = default)
    {
        var consulta = _db.SesionesConteo.AsNoTracking();
        if (estado is not null)
            consulta = consulta.Where(s => s.Estado == estado);

        var sesiones = await consulta.OrderByDescending(s => s.FechaInicio).ToListAsync(ct);
        var resultado = new List<SesionConteoDto>(sesiones.Count);
        foreach (var s in sesiones)
            resultado.Add(await MapearAsync(s, ct));
        return resultado;
    }

    public async Task<ResumenSesionDto?> ObtenerResumenAsync(Guid sesionId, CancellationToken ct = default)
    {
        var sesion = await _db.SesionesConteo.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sesionId, ct);
        if (sesion is null)
            return null;

        var esperados = await EsperadosAsync(sesion, ct);
        var contadosIds = await ContadosAsync(sesion, ct);
        var reconteoIds = (await _db.ConteosCiclicos.AsNoTracking()
                .Where(c => c.SesionConteoId == sesionId && c.Estado == EstadoConteoCiclico.RequiereReconteo)
                .Select(c => c.StockFisicoId)
                .ToListAsync(ct))
            .ToHashSet();

        // Falta todo lo esperado que no está contado ni pidiendo reconteo. Se decide por el estado
        // actual (no por la última lectura): tras un StockCambio el conteo vuelve a EnConteo y reaparece aquí.
        var faltanIds = esperados.Where(id => !contadosIds.Contains(id) && !reconteoIds.Contains(id)).ToList();

        var hallazgos = await _db.HallazgosConteo.AsNoTracking()
            .Where(h => h.SesionConteoId == sesionId)
            .OrderBy(h => h.Fecha)
            .Select(h => new HallazgoDto(h.Sku, h.NombreProducto, h.Pasillo, h.Cantidad, h.Usuario, h.Fecha))
            .ToListAsync(ct);

        return new ResumenSesionDto(
            await MapearAsync(sesion, ct),
            await ItemsAsync(faltanIds, ct),
            await ItemsAsync(contadosIds, ct),
            await ItemsAsync(reconteoIds, ct),
            hallazgos);
    }

    public async Task<IReadOnlyList<string>> ListarPasillosAsync(CancellationToken ct = default) =>
        await _db.UbicacionesAlmacen.AsNoTracking()
            .Where(u => u.Pasillo != "")
            .Select(u => u.Pasillo)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(ct);

    // ── Lectura, cierre y cancelación (Tasks 3 y 4) ──────────────────────────

    public async Task<ResultadoLecturaDto> RegistrarLecturaAsync(Guid sesionId, Guid lecturaId, string codigo, int cantidad,
        Guid? stockFisicoId, string usuario, CancellationToken ct = default)
    {
        if (cantidad < 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), cantidad, "La cantidad no puede ser negativa.");

        // Reintento de una lectura ya procesada (red móvil): devolver lo mismo, sin recalcular.
        var guardada = await _db.LecturasConteo.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lecturaId, ct);
        if (guardada is not null)
        {
            // El mismo UUID nunca debe recuperar datos de otra sesión.
            if (guardada.SesionConteoId != sesionId || guardada.CodigoLeido != codigo)
                return Dto(ResultadoLectura.Rechazado);
            return await ReconstruirAsync(guardada, ct);
        }

        var sesion = await _db.SesionesConteo.FirstOrDefaultAsync(s => s.Id == sesionId, ct)
            ?? throw new KeyNotFoundException($"Sesión {sesionId} no encontrada.");
        if (sesion.Estado != EstadoSesionConteo.Abierta)
            return Dto(ResultadoLectura.SesionNoAbierta);

        codigo = new string((codigo ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!CodigoBarras.EsEan13Valido(codigo))
            return Dto(ResultadoLectura.CodigoInvalido);

        var coincidencias = await _db.Productos
            .Where(p => p.CodigoEAN == codigo)
            .OrderBy(p => p.Id)
            .Take(2)
            .ToListAsync(ct);
        if (coincidencias.Count > 1)
            return new ResultadoLecturaDto(ResultadoLectura.Rechazado,
                "El código corresponde a varios productos: requiere conciliación.", null, null, null, null);
        var producto = coincidencias.FirstOrDefault();
        if (producto is null)
            return new ResultadoLecturaDto(ResultadoLectura.ProductoNoEncontrado,
                "No existe un producto con ese código.", null, null, null, null);

        var registros = await AlcanceDeSesion(sesion)
            .Where(s => s.ProductoId == producto.Id)
            .Select(s => new OpcionUbicacion(s.Id, s.UbicacionCodigo ?? string.Empty))
            .ToListAsync(ct);

        if (registros.Count == 0)
            return await RegistrarFueraDeLugarAsync(sesion, lecturaId, codigo, producto, cantidad, usuario, ct);

        var elegido = registros.FirstOrDefault(r => r.StockFisicoId == stockFisicoId);
        if (elegido is null && registros.Count > 1)
        {
            return new ResultadoLecturaDto(ResultadoLectura.ElegirUbicacion, Mensajes[ResultadoLectura.ElegirUbicacion],
                producto.Sku, producto.Nombre, null, registros.OrderBy(r => r.UbicacionCodigo).ToList());
        }

        elegido ??= registros[0];
        return await ContarAsync(sesion, lecturaId, codigo, producto, elegido, cantidad, usuario, ct);
    }

    private static readonly Dictionary<ResultadoLectura, string> Mensajes = new()
    {
        [ResultadoLectura.Coincide] = "Coincide.",
        [ResultadoLectura.Diferencia] = "Hay diferencia: vuelve a contarlo.",
        [ResultadoLectura.AjusteCreado] = "Diferencia confirmada: se creó un ajuste para aprobación.",
        [ResultadoLectura.FueraDeLugar] = "Encontrado fuera de lugar: quedó registrado para el supervisor.",
        [ResultadoLectura.ElegirUbicacion] = "Este producto está en varias posiciones: elige cuál estás contando.",
        [ResultadoLectura.BloqueadoPorAjuste] = "Tiene un ajuste pendiente de aprobación; no se puede contar.",
        [ResultadoLectura.YaContado] = "Ya contado en esta sesión.",
        [ResultadoLectura.StockCambio] = "El stock cambió mientras contabas: vuelve a escanearlo.",
        [ResultadoLectura.CodigoInvalido] = "Código inválido: vuelve a escanear.",
        [ResultadoLectura.SesionNoAbierta] = "La sesión ya no está abierta.",
        [ResultadoLectura.Rechazado] = "No se pudo registrar el conteo: una regla de inventario lo impidió.",
    };

    private static ResultadoLecturaDto Dto(ResultadoLectura r, Producto? p = null, string? ubicacion = null) =>
        new(r, Mensajes[r], p?.Sku, p?.Nombre, ubicacion, null);

    /// <summary>Registros donde se busca el producto: el pasillo (Recorrido) o los conteos de la sesión (Planificado).</summary>
    private IQueryable<StockFisico> AlcanceDeSesion(SesionConteo sesion)
    {
        if (sesion.Modo == ModoSesionConteo.Recorrido)
            return StocksDelPasillo(sesion.Pasillo!);

        var ids = _db.ConteosCiclicos.Where(c => c.SesionConteoId == sesion.Id).Select(c => c.StockFisicoId);
        return _db.StockFisico.Where(s => ids.Contains(s.Id));
    }

    private async Task<ResultadoLecturaDto> RegistrarFueraDeLugarAsync(SesionConteo sesion, Guid lecturaId, string codigo,
        Producto producto, int cantidad, string usuario, CancellationToken ct)
    {
        _db.HallazgosConteo.Add(new HallazgoConteo
        {
            Id = Guid.NewGuid(),
            SesionConteoId = sesion.Id,
            ProductoId = producto.Id,
            Sku = producto.Sku,
            NombreProducto = producto.Nombre,
            Pasillo = sesion.Pasillo ?? "(planificado)",
            Cantidad = cantidad,
            Usuario = usuario,
            Fecha = DateTime.UtcNow,
        });
        GuardarLectura(sesion.Id, lecturaId, codigo, producto.Id, null, null, cantidad, ResultadoLectura.FueraDeLugar, usuario);
        await _db.SaveChangesAsync(ct);
        return Dto(ResultadoLectura.FueraDeLugar, producto);
    }

    /// <summary>
    /// Aplica las reglas existentes de conteo cíclico en un solo paso transaccional:
    /// planificar/iniciar si hace falta, registrar primer conteo o reconteo, y guardar la lectura.
    /// </summary>
    /// <param name="dentroDeCierre">
    /// True cuando lo llama el cierre de sesión: se une a su transacción y deja que el cierre maneje los rechazos.
    /// </param>
    private async Task<ResultadoLecturaDto> ContarAsync(SesionConteo sesion, Guid lecturaId, string codigo, Producto producto,
        OpcionUbicacion registro, int cantidad, string usuario, CancellationToken ct, bool dentroDeCierre = false)
    {
        var stockId = registro.StockFisicoId;
        ResultadoLecturaDto Responder(ResultadoLectura r) => Dto(r, producto, registro.UbicacionCodigo);

        // Si el cierre de sesión ya abrió una transacción, esta lectura se une a ella.
        await using var transaccion = _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            // Bloquear el registro de stock ANTES de leer el estado del conteo: si dos equipos escanean
            // lo mismo a la vez, el segundo espera y ve lo que hizo el primero (evita dos ajustes).
            if (_db.Database.IsRelational())
            {
                await _db.Database
                    .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM catalog.stock_fisico WHERE \"Id\" = {stockId} FOR UPDATE")
                    .ToListAsync(ct);
            }

            var bloqueado = await _db.AjustesInventario.AnyAsync(a => a.StockFisicoId == stockId &&
                (a.Estado == EstadoAjusteInventario.PendienteAprobacion || a.Estado == EstadoAjusteInventario.Aprobado), ct);
            var yaContado = !bloqueado && await _db.LecturasConteo.AnyAsync(l =>
                l.SesionConteoId == sesion.Id && l.StockFisicoId == stockId && ResultadosContados.Contains(l.Resultado), ct);

            if (bloqueado || yaContado)
            {
                var r = bloqueado ? ResultadoLectura.BloqueadoPorAjuste : ResultadoLectura.YaContado;
                GuardarLectura(sesion.Id, lecturaId, codigo, producto.Id, stockId, null, cantidad, r, usuario);
                await _db.SaveChangesAsync(ct);
                if (transaccion is not null) await transaccion.CommitAsync(ct);
                return Responder(r);
            }

            var conteo = await _db.ConteosCiclicos.FirstOrDefaultAsync(c =>
                c.StockFisicoId == stockId && EstadosActivos.Contains(c.Estado), ct);

            if (conteo is null)
            {
                conteo = await PlanificarEIniciarAsync(sesion, stockId, usuario, ct);
            }
            else
            {
                conteo.SesionConteoId ??= sesion.Id;
                await _db.SaveChangesAsync(ct);
                if (conteo.Estado == EstadoConteoCiclico.Planificado)
                    Asegurar(await _conteos.IniciarAsync(conteo.Id, usuario, ct));
            }

            var stock = await _db.StockFisico.SingleAsync(s => s.Id == stockId, ct);
            if (conteo.Estado != EstadoConteoCiclico.Planificado && conteo.CantidadSistema != stock.CantidadTotal)
            {
                // Hubo movimientos desde la foto: reiniciar el conteo con la cantidad nueva.
                Asegurar(await _conteos.CancelarAsync(conteo.Id, usuario, ct));
                var nuevo = await PlanificarEIniciarAsync(sesion, stockId, usuario, ct);
                GuardarLectura(sesion.Id, lecturaId, codigo, producto.Id, stockId, nuevo.Id, null, ResultadoLectura.StockCambio, usuario);
                await _db.SaveChangesAsync(ct);
                if (transaccion is not null) await transaccion.CommitAsync(ct);
                return Responder(ResultadoLectura.StockCambio);
            }

            Asegurar(conteo.Estado == EstadoConteoCiclico.RequiereReconteo
                ? await _conteos.RegistrarReconteoAsync(conteo.Id, cantidad, usuario, ct)
                : await _conteos.RegistrarPrimerConteoAsync(conteo.Id, cantidad, usuario, ct));

            var resultado = conteo.Estado switch
            {
                EstadoConteoCiclico.Completado => ResultadoLectura.Coincide,
                EstadoConteoCiclico.RequiereReconteo => ResultadoLectura.Diferencia,
                EstadoConteoCiclico.CompletadoConAjuste => ResultadoLectura.AjusteCreado,
                _ => throw new InvalidOperationException($"Estado de conteo inesperado tras registrar: {conteo.Estado}."),
            };

            GuardarLectura(sesion.Id, lecturaId, codigo, producto.Id, stockId, conteo.Id, cantidad, resultado, usuario);
            await _db.SaveChangesAsync(ct);
            if (transaccion is not null) await transaccion.CommitAsync(ct);
            return Responder(resultado);
        }
        catch (ReglaRechazadaException ex) when (!dentroDeCierre)
        {
            // Una regla de inventario impidió registrar (p. ej. el ajuste excede lo disponible):
            // se deshace todo y se responde como resultado de negocio, no como error del servidor.
            await DeshacerAsync(transaccion, ct);
            GuardarLectura(sesion.Id, lecturaId, codigo, producto.Id, stockId, null, cantidad, ResultadoLectura.Rechazado, usuario);
            await _db.SaveChangesAsync(ct);
            return new ResultadoLecturaDto(ResultadoLectura.Rechazado, ex.Message, producto.Sku, producto.Nombre,
                registro.UbicacionCodigo, null);
        }
        catch (DbUpdateException) when (!dentroDeCierre)
        {
            // La misma lectura llegó dos veces a la vez (reintento): devolver la que quedó guardada.
            await DeshacerAsync(transaccion, ct);
            var guardada = await _db.LecturasConteo.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lecturaId, ct);
            if (guardada is null) throw;
            return await ReconstruirAsync(guardada, ct);
        }
        catch
        {
            if (transaccion is not null) await transaccion.RollbackAsync(ct);
            if (_db.Database.CurrentTransaction is null) _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task DeshacerAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaccion, CancellationToken ct)
    {
        if (transaccion is not null) await transaccion.RollbackAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task<ConteoCiclico> PlanificarEIniciarAsync(SesionConteo sesion, Guid stockId, string usuario, CancellationToken ct)
    {
        var plan = Asegurar(await _conteos.PlanificarAsync(stockId, null, usuario, ct));
        var conteo = await _db.ConteosCiclicos.SingleAsync(c => c.Id == plan.Conteo!.Id, ct);
        conteo.SesionConteoId = sesion.Id;
        await _db.SaveChangesAsync(ct);
        Asegurar(await _conteos.IniciarAsync(conteo.Id, usuario, ct));
        return conteo;
    }

    /// <summary>Una regla del servicio de conteos o de ajustes rechazó la operación.</summary>
    private sealed class ReglaRechazadaException(string mensaje) : InvalidOperationException(mensaje);

    private static ConteoCiclicoResult Asegurar(ConteoCiclicoResult r) =>
        r.Exitoso ? r : throw new ReglaRechazadaException(r.Mensaje);

    private void GuardarLectura(Guid sesionId, Guid lecturaId, string codigo, Guid? productoId, Guid? stockId,
        Guid? conteoId, int? cantidad, ResultadoLectura resultado, string usuario) =>
        _db.LecturasConteo.Add(new LecturaConteo
        {
            Id = lecturaId,
            SesionConteoId = sesionId,
            CodigoLeido = codigo,
            ProductoId = productoId,
            StockFisicoId = stockId,
            ConteoCiclicoId = conteoId,
            Cantidad = cantidad,
            Resultado = resultado,
            Usuario = usuario,
            Fecha = DateTime.UtcNow,
        });

    private async Task<ResultadoLecturaDto> ReconstruirAsync(LecturaConteo lectura, CancellationToken ct)
    {
        var producto = lectura.ProductoId is null ? null
            : await _db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == lectura.ProductoId, ct);
        var ubicacion = lectura.StockFisicoId is null ? null
            : await _db.StockFisico.AsNoTracking().Where(s => s.Id == lectura.StockFisicoId)
                .Select(s => s.UbicacionCodigo).FirstOrDefaultAsync(ct);
        return Dto(lectura.Resultado, producto, ubicacion);
    }

    public async Task<OperacionSesionResult> CerrarAsync(Guid sesionId, IReadOnlyList<DecisionCierre> decisiones, string usuario,
        CancellationToken ct = default)
    {
        var sesion = await _db.SesionesConteo.FirstOrDefaultAsync(s => s.Id == sesionId, ct)
            ?? throw new KeyNotFoundException($"Sesión {sesionId} no encontrada.");
        if (sesion.Estado != EstadoSesionConteo.Abierta)
            return new(false, Mensajes[ResultadoLectura.SesionNoAbierta], null);

        var faltan = (await ObtenerResumenAsync(sesionId, ct))!.Faltan;
        var idsFaltan = faltan.Select(f => f.StockFisicoId).ToHashSet();
        var idsDecididos = decisiones.Select(d => d.StockFisicoId).ToList();
        if (idsDecididos.Count != idsDecididos.Distinct().Count() || !idsFaltan.SetEquals(idsDecididos))
            return new(false, "Debes decidir cada producto que falta, una sola vez.", null);

        await using var transaccion = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            foreach (var decision in decisiones.Where(d => d.Accion == AccionCierre.NoHay))
            {
                // "No hay ninguno" = contar 0 con las mismas reglas que una lectura.
                var item = faltan.First(f => f.StockFisicoId == decision.StockFisicoId);
                var producto = await _db.Productos.SingleAsync(p =>
                    _db.StockFisico.Any(s => s.Id == decision.StockFisicoId && s.ProductoId == p.Id), ct);
                await ContarAsync(sesion, Guid.NewGuid(), producto.CodigoEAN ?? string.Empty, producto,
                    new OpcionUbicacion(item.StockFisicoId, item.UbicacionCodigo), 0, usuario, ct, dentroDeCierre: true);
            }

            sesion.Estado = EstadoSesionConteo.Cerrada;
            sesion.FechaCierre = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            if (transaccion is not null) await transaccion.CommitAsync(ct);
        }
        catch (ReglaRechazadaException ex)
        {
            if (transaccion is not null) await transaccion.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return new(false, $"No se pudo cerrar: {ex.Message}", null);
        }
        catch
        {
            if (transaccion is not null) await transaccion.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            throw;
        }

        return new(true, "Sesión cerrada.", await MapearAsync(sesion, ct));
    }

    public async Task<OperacionSesionResult> CancelarAsync(Guid sesionId, string usuario, CancellationToken ct = default)
    {
        var sesion = await _db.SesionesConteo.FirstOrDefaultAsync(s => s.Id == sesionId, ct)
            ?? throw new KeyNotFoundException($"Sesión {sesionId} no encontrada.");
        if (sesion.Estado != EstadoSesionConteo.Abierta)
            return new(false, Mensajes[ResultadoLectura.SesionNoAbierta], null);

        // Los conteos ya registrados quedan como están.
        sesion.Estado = EstadoSesionConteo.Cancelada;
        sesion.FechaCierre = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new(true, "Sesión cancelada.", await MapearAsync(sesion, ct));
    }

    // ── Ayudantes ────────────────────────────────────────────────────────────

    private IQueryable<StockFisico> StocksDelPasillo(string pasillo) =>
        from s in _db.StockFisico
        join u in _db.UbicacionesAlmacen on s.UbicacionAlmacenId equals u.Id
        where u.Pasillo == pasillo
        select s;

    /// <summary>
    /// Registros esperados: en Recorrido, los del pasillo con stock (&gt; 0); en Planificado, los de sus conteos.
    /// </summary>
    private async Task<List<Guid>> EsperadosAsync(SesionConteo sesion, CancellationToken ct)
    {
        if (sesion.Modo == ModoSesionConteo.Recorrido)
        {
            return await StocksDelPasillo(sesion.Pasillo!)
                .Where(s => s.CantidadTotal > 0)
                .Select(s => s.Id)
                .ToListAsync(ct);
        }

        return await _db.ConteosCiclicos
            .Where(c => c.SesionConteoId == sesion.Id)
            .Select(c => c.StockFisicoId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>Datos de cada registro de stock, ordenados por ubicación y SKU.</summary>
    private async Task<List<ItemSesionDto>> ItemsAsync(IEnumerable<Guid> stockIds, CancellationToken ct)
    {
        var ids = stockIds.ToList();
        if (ids.Count == 0)
            return new List<ItemSesionDto>();

        var items = await (
            from s in _db.StockFisico.AsNoTracking()
            join p in _db.Productos.AsNoTracking() on s.ProductoId equals p.Id
            where ids.Contains(s.Id)
            select new ItemSesionDto(s.Id, p.Sku, p.Nombre, s.UbicacionCodigo ?? string.Empty)
        ).ToListAsync(ct);

        return items.OrderBy(i => i.UbicacionCodigo).ThenBy(i => i.Sku).ToList();
    }

    /// <summary>
    /// Registros ya contados en la sesión: lecturas que coincidieron o crearon ajuste y, en sesiones
    /// planificadas, conteos vinculados que se completaron aunque fuera fuera de la sesión.
    /// </summary>
    private async Task<HashSet<Guid>> ContadosAsync(SesionConteo s, CancellationToken ct)
    {
        var porLectura = await _db.LecturasConteo.AsNoTracking()
            .Where(l => l.SesionConteoId == s.Id && l.StockFisicoId != null && ResultadosContados.Contains(l.Resultado))
            .Select(l => l.StockFisicoId!.Value)
            .ToListAsync(ct);
        var contados = porLectura.ToHashSet();

        if (s.Modo == ModoSesionConteo.Planificado)
        {
            var completados = await _db.ConteosCiclicos.AsNoTracking()
                .Where(c => c.SesionConteoId == s.Id &&
                            (c.Estado == EstadoConteoCiclico.Completado || c.Estado == EstadoConteoCiclico.CompletadoConAjuste))
                .Select(c => c.StockFisicoId)
                .ToListAsync(ct);
            contados.UnionWith(completados);
        }

        return contados;
    }

    private async Task<SesionConteoDto> MapearAsync(SesionConteo s, CancellationToken ct)
    {
        var esperados = (await EsperadosAsync(s, ct)).Count;
        var contados = (await ContadosAsync(s, ct)).Count;
        var hallazgos = await _db.HallazgosConteo.CountAsync(h => h.SesionConteoId == s.Id, ct);

        return new SesionConteoDto(s.Id, s.Numero, s.Modo, s.Pasillo, s.Estado, s.CreadaPor,
            s.FechaInicio, s.FechaCierre, esperados, contados, hallazgos);
    }
}
