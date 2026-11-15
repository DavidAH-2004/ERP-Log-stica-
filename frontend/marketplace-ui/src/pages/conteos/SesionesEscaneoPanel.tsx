import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { sesionesConteoApi, type ResumenSesion, type SesionConteo } from '../../api/sesionesConteo';

const fecha = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : '—');

/** Vista del supervisor: sesiones de conteo con escáner y lo encontrado fuera de lugar. */
export default function SesionesEscaneoPanel() {
  const navigate = useNavigate();
  const [sesiones, setSesiones] = useState<SesionConteo[]>([]);
  const [error, setError] = useState('');
  const [detalle, setDetalle] = useState<ResumenSesion | null>(null);

  useEffect(() => {
    sesionesConteoApi.listar()
      .then(setSesiones)
      .catch(e => setError(e instanceof Error ? e.message : 'No se pudieron cargar las sesiones.'));
  }, []);

  const verDetalle = async (id: string) => {
    if (detalle?.sesion.id === id) { setDetalle(null); return; }
    try {
      setDetalle(await sesionesConteoApi.resumen(id));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo cargar la sesión.');
    }
  };

  return (
    <section style={{ marginTop: '1.5rem' }}>
      <h2 style={{ fontSize: '1.1rem', marginBottom: '0.5rem' }}>Sesiones de escaneo</h2>
      {error && <div role="alert" style={{ color: 'var(--red)' }}>{error}</div>}
      {sesiones.length === 0 && !error && <div style={{ color: 'var(--text-muted)' }}>Todavía no hay sesiones de escaneo.</div>}
      {sesiones.length > 0 && (
        <div className="table-container" style={{ overflowX: 'auto' }}>
          <table>
            <thead>
              <tr>
                <th>Sesión</th><th>Modo</th><th>Pasillo</th><th>Estado</th><th>Operador</th>
                <th>Inicio</th><th>Cierre</th><th>Contados</th><th>Fuera de lugar</th><th></th>
              </tr>
            </thead>
            <tbody>
              {sesiones.map(s => (
                <tr key={s.id}>
                  <td className="td-mono">{s.numero}</td>
                  <td>{s.modo}</td>
                  <td>{s.pasillo ?? '—'}</td>
                  <td>{s.estado}</td>
                  <td>{s.creadaPor}</td>
                  <td>{fecha(s.fechaInicio)}</td>
                  <td>{fecha(s.fechaCierre)}</td>
                  <td>{s.contados} / {s.esperados}</td>
                  <td>{s.hallazgos}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button className="btn btn-ghost" onClick={() => verDetalle(s.id)}>
                      {detalle?.sesion.id === s.id ? 'Ocultar' : 'Detalle'}
                    </button>
                    {s.estado === 'Abierta' && (
                      <button className="btn btn-secondary" onClick={() => navigate(`/conteos/escaner/${s.id}`)}>Continuar</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {detalle && (
        <div style={{ marginTop: '0.75rem', border: '1px solid var(--border)', borderRadius: 10, padding: '0.75rem' }}>
          <div style={{ fontWeight: 700 }}>{detalle.sesion.numero}</div>
          <div style={{ color: 'var(--text-secondary)', fontSize: '0.9rem', margin: '0.25rem 0 0.5rem' }}>
            Contados {detalle.contados.length} · Faltan {detalle.faltan.length} · Reconteo {detalle.reconteo.length} ·
            Fuera de lugar {detalle.fueraDeLugar.length}
          </div>
          {detalle.fueraDeLugar.length > 0 && (
            <>
              <div style={{ fontWeight: 600, marginBottom: 4 }}>Encontrados fuera de lugar</div>
              <ul style={{ margin: 0, paddingLeft: '1.1rem' }}>
                {detalle.fueraDeLugar.map((h, i) => (
                  <li key={i}>
                    <span className="td-mono">{h.sku}</span> {h.nombre} — {h.cantidad} u. en {h.pasillo} ({h.usuario}, {fecha(h.fecha)})
                  </li>
                ))}
              </ul>
            </>
          )}
          {detalle.reconteo.length > 0 && (
            <div style={{ marginTop: 6, fontSize: '0.9rem' }}>
              Pendientes de reconteo: {detalle.reconteo.map(r => r.sku).join(', ')}
            </div>
          )}
        </div>
      )}
    </section>
  );
}
