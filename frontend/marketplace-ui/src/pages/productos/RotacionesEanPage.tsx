import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, ShieldAlert } from 'lucide-react';
import { ApiError } from '../../api/client';
import { rotacionesEanApi, type RotacionEanEvento } from '../../api/codigosBarras';
import { esEan13Valido } from '../../utils/ean13';

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function nuevoId(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6] & 15) | 64;
  b[8] = (b[8] & 63) | 128;
  const h = [...b].map(v => v.toString(16).padStart(2, '0')).join('');
  return [h.slice(0, 8), h.slice(8, 12), h.slice(12, 16), h.slice(16, 20), h.slice(20)].join('-');
}

const campo: React.CSSProperties = {
  width: '100%', maxWidth: 550, boxSizing: 'border-box',
  padding: '0.65rem', border: '1px solid var(--border)',
  borderRadius: 8, background: 'var(--bg-surface)', color: 'var(--text-primary)',
};

/** UI requires second Admin; the backend independently enforces dual flags. */
export default function RotacionesEanPage() {
  const navigate = useNavigate();
  const [producto, setProducto] = useState('');
  const [nuevoCodigo, setNuevoCodigo] = useState('');
  const [motivo, setMotivo] = useState('');
  const [solicitud, setSolicitud] = useState(nuevoId);
  const [aprobacion, setAprobacion] = useState('');
  const [cancelacion, setCancelacion] = useState('');
  const [eventos, setEventos] = useState<RotacionEanEvento[]>([]);
  const [ocupado, setOcupado] = useState(false);
  const [resultado, setResultado] = useState('');
  const [error, setError] = useState('');
  const [inactivo, setInactivo] = useState(false);

  const manejarError = (err: unknown) => {
    if (err instanceof ApiError && err.status === 503) {
      setInactivo(true);
      setError('Rotaciones desactivadas. Falta aprobar migración, pruebas físicas o liberación.');
    } else {
      setError(err instanceof Error ? err.message : 'Respuesta incierta. Conserva el ID de solicitud y consulta historial.');
    }
  };

  const cargarSinCambiarEstado = async () => {
    if (uuid.test(producto)) setEventos(await rotacionesEanApi.historial(producto));
  };

  const historial = async () => {
    if (!uuid.test(producto)) { setError('El producto debe tener un UUID válido.'); return; }
    setOcupado(true); setError('');
    try { await cargarSinCambiarEstado(); }
    catch (e) { manejarError(e); }
    finally { setOcupado(false); }
  };

  const solicitar = async () => {
    if (!uuid.test(producto) || !uuid.test(solicitud) ||
        !esEan13Valido(nuevoCodigo) || !nuevoCodigo.startsWith('20') ||
        motivo.trim().length < 10 || motivo.trim().length > 1000) {
      setError('Indica UUID válidos, EAN-13 nuevo con prefijo 20 y motivo de 10 a 1000 caracteres.');
      return;
    }
    setOcupado(true); setError(''); setResultado('');
    try {
      const r = await rotacionesEanApi.solicitar(producto, solicitud, nuevoCodigo, motivo.trim());
      setResultado('Solicitud ' + r.solicitudId + ': ' + r.estado + '. Otro administrador debe aprobarla.');
      try { await cargarSinCambiarEstado(); } catch { /* the request already succeeded */ }
    } catch (e) { manejarError(e); }
    finally { setOcupado(false); }
  };

  const aprobar = async () => {
    if (!uuid.test(aprobacion)) { setError('Se requiere UUID de solicitud válido.'); return; }
    if (!window.confirm('¿Aprobar con esta cuenta Admin? La rotación es irreversible y mantendrá el EAN anterior como alias.')) return;
    setOcupado(true); setError(''); setResultado('');
    try {
      const r = await rotacionesEanApi.aprobar(aprobacion);
      setResultado('Solicitud ' + r.solicitudId + ': ' + r.estado + '. Verifica historial antes de imprimir.');
      try { await cargarSinCambiarEstado(); } catch { /* approval result is authoritative */ }
    } catch (e) { manejarError(e); }
    finally { setOcupado(false); }
  };

  const cancelar = async () => {
    if (!uuid.test(cancelacion)) { setError('Indica UUID de solicitud para cancelar.'); return; }
    if (!window.confirm('Solo el Admin solicitante puede cancelar una solicitud todavía pendiente. ¿Continuar?')) return;
    setOcupado(true); setError(''); setResultado('');
    try {
      const r = await rotacionesEanApi.cancelar(cancelacion);
      setResultado('Solicitud ' + r.solicitudId + ': ' + r.estado + '. No se modificó ningún EAN.');
      try { await cargarSinCambiarEstado(); } catch { /* cancellation result is authoritative */ }
    } catch (e) { manejarError(e); }
    finally { setOcupado(false); }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 810 }}>
      <button className="btn btn-secondary" style={{ alignSelf: 'flex-start' }}
        onClick={() => navigate('/productos')}><ArrowLeft size={16}/> Volver a productos</button>
      <h1 style={{ margin: 0 }}>Rotaciones EAN de productos</h1>
      <p role="note" style={{ border: '1px solid var(--border)', padding: 12, borderRadius: 10 }}>
        <ShieldAlert size={18}/> Una solicitud NO modifica el código. Se requiere un segundo administrador
        y autorización del entorno. Los códigos antiguos no se reutilizan ni se reimprimen automáticamente.
      </p>
      {inactivo && <p role="status">Función aún inhabilitada en este entorno.</p>}
      {error && <p role="alert" style={{ color: 'var(--red)' }}>{error}</p>}
      {resultado && <p role="status">{resultado}</p>}
      <section style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <h2 style={{ fontSize: '1.1rem' }}>1. Solicitar rotación (Admin A)</h2>
        <label>UUID del producto<input style={campo} value={producto}
          onChange={e => setProducto(e.target.value.trim())} placeholder="Producto UUID"/></label>
        <label>Nuevo EAN-13 interno<input style={campo} inputMode="numeric" maxLength={13}
          value={nuevoCodigo} onChange={e => setNuevoCodigo(e.target.value.trim())} placeholder="20…"/></label>
        <label>Justificación<textarea style={campo} rows={3} value={motivo}
          maxLength={1000} onChange={e => setMotivo(e.target.value)} placeholder="Motivo sin datos personales"/></label>
        <label>ID idempotente (conservar si hay error de red)<input style={campo} value={solicitud} readOnly/></label>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
          <button className="btn btn-primary" disabled={ocupado || inactivo} onClick={solicitar}>Registrar solicitud</button>
          <button className="btn btn-secondary" disabled={ocupado} onClick={() => setSolicitud(nuevoId())}>
            Nuevo ID
          </button>
          <button className="btn btn-secondary" disabled={ocupado || inactivo} onClick={historial}>
            Consultar historial
          </button>
        </div>
      </section>
      <section style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <h2 style={{ fontSize: '1.1rem' }}>2. Aprobar y ejecutar (Admin B)</h2>
        <p>El aprobador debe iniciar sesión con otra cuenta Admin. El autor original no puede autoaprobarse.</p>
        <label>UUID de solicitud<input style={campo} value={aprobacion}
          onChange={e => setAprobacion(e.target.value.trim())}/></label>
        <button className="btn btn-primary" style={{ alignSelf: 'flex-start' }} disabled={ocupado || inactivo}
          onClick={aprobar}>Aprobar solicitud</button>
      </section>
      <section style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <h2 style={{ fontSize: '1.1rem' }}>3. Cancelar solicitud pendiente</h2>
        <p>Solo el Admin que la registró puede cancelarla. Una rotación ejecutada no puede revertirse desde aquí.</p>
        <label>UUID de solicitud<input style={campo} value={cancelacion}
          onChange={e => setCancelacion(e.target.value.trim())}/></label>
        <button className="btn btn-secondary" style={{ alignSelf: 'flex-start' }}
          disabled={ocupado || inactivo} onClick={cancelar}>Cancelar solicitud pendiente</button>
      </section>
      <section>
        <h2 style={{ fontSize: '1.1rem' }}>Historial del producto (máximo 100 eventos)</h2>
        {eventos.length === 0 ? <p>Sin eventos consultados.</p> :
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead><tr><th>Solicitud</th><th>Versión</th><th>Estado</th><th>Anterior</th><th>Nuevo</th></tr></thead>
              <tbody>{eventos.map(e => (
                <tr key={e.solicitudId + '-' + e.version}>
                  <td style={{ padding: 7, fontFamily: 'monospace' }}>{e.solicitudId}</td>
                  <td style={{ padding: 7 }}>{e.version}</td>
                  <td style={{ padding: 7 }}>{e.estado}</td>
                  <td style={{ padding: 7, fontFamily: 'monospace' }}>{e.codigoAnterior}</td>
                  <td style={{ padding: 7, fontFamily: 'monospace' }}>{e.codigoNuevo}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>}
      </section>
    </div>
  );
}
