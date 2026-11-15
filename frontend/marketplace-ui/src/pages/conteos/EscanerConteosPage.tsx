import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, ScanLine, ListChecks, PlayCircle } from 'lucide-react';
import { sesionesConteoApi, type SesionConteo } from '../../api/sesionesConteo';
import { ApiError } from '../../api/client';

const tarjeta: React.CSSProperties = {
  background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '12px',
  padding: '1rem', display: 'flex', flexDirection: 'column', gap: '0.75rem',
};
const control: React.CSSProperties = {
  minHeight: 44, padding: '0.5rem 0.75rem', borderRadius: '8px', border: '1px solid var(--border)',
  background: 'var(--bg-surface)', color: 'var(--text-primary)', fontSize: '1rem', width: '100%', boxSizing: 'border-box',
};
// Ícono y texto en la misma línea (los SVG del tema se muestran como bloque por defecto).
const titulo: React.CSSProperties = { fontSize: '1rem', margin: 0, display: 'flex', alignItems: 'center', gap: 8 };
const boton: React.CSSProperties = { minHeight: 44, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 8 };

/** Inicio de los conteos con escáner: retomar una sesión, nuevo recorrido o desde planificados. */
export default function EscanerConteosPage() {
  const navigate = useNavigate();
  const [abiertas, setAbiertas] = useState<SesionConteo[]>([]);
  const [pasillos, setPasillos] = useState<string[]>([]);
  const [pasilloRecorrido, setPasilloRecorrido] = useState('');
  const [pasilloPlanificado, setPasilloPlanificado] = useState('');
  const [error, setError] = useState('');
  const [existente, setExistente] = useState<string | null>(null);
  const [trabajando, setTrabajando] = useState(false);

  useEffect(() => {
    Promise.all([sesionesConteoApi.listar('Abierta'), sesionesConteoApi.pasillos()])
      .then(([s, p]) => { setAbiertas(s); setPasillos(p); })
      .catch(e => setError(e instanceof Error ? e.message : 'No se pudieron cargar las sesiones.'));
  }, []);

  const abrir = async (accion: () => Promise<SesionConteo>) => {
    setTrabajando(true);
    setError('');
    setExistente(null);
    try {
      const sesion = await accion();
      navigate(`/conteos/escaner/${sesion.id}`);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setExistente((e.data as { numeroSesionExistente?: string })?.numeroSesionExistente ?? null);
      }
      setError(e instanceof Error ? e.message : 'No se pudo abrir la sesión.');
    } finally {
      setTrabajando(false);
    }
  };

  const sesionExistente = existente ? abiertas.find(s => s.numero === existente) : undefined;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', maxWidth: 720 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <button className="btn btn-ghost" style={boton} onClick={() => navigate('/conteos')} aria-label="Volver a conteos">
          <ArrowLeft size={18} />
        </button>
        <div>
          <h1 className="page-title" style={{ margin: 0 }}>Contar con escáner</h1>
          <p className="page-subtitle" style={{ margin: 0 }}>Escanea cada producto y escribe cuántas unidades hay</p>
        </div>
      </div>

      {error && (
        <div role="alert" style={{ ...tarjeta, borderColor: 'var(--red)', color: 'var(--red)' }}>
          {error}
          {sesionExistente && (
            <button className="btn btn-secondary" style={boton} onClick={() => navigate(`/conteos/escaner/${sesionExistente.id}`)}>
              Continuar {sesionExistente.numero}
            </button>
          )}
        </div>
      )}

      {abiertas.length > 0 && (
        <section style={tarjeta}>
          <h2 style={{ fontSize: '1rem', margin: 0 }}>Sesiones abiertas</h2>
          {abiertas.map(s => (
            <button key={s.id} className="btn btn-secondary" style={{ ...boton, justifyContent: 'space-between' }}
              onClick={() => navigate(`/conteos/escaner/${s.id}`)}>
              <span><PlayCircle size={16} style={{ verticalAlign: 'middle' }} /> {s.numero} · {s.pasillo ?? 'Planificados'}</span>
              <span>{s.contados}/{s.esperados}</span>
            </button>
          ))}
        </section>
      )}

      <section style={tarjeta}>
        <h2 style={titulo}><ScanLine size={18} /> Nuevo recorrido</h2>
        <select aria-label="Pasillo a recorrer" style={control} value={pasilloRecorrido} onChange={e => setPasilloRecorrido(e.target.value)}>
          <option value="">Elige un pasillo…</option>
          {pasillos.map(p => <option key={p} value={p}>{p}</option>)}
        </select>
        <button className="btn btn-primary" style={boton} disabled={!pasilloRecorrido || trabajando}
          onClick={() => abrir(() => sesionesConteoApi.abrirRecorrido(pasilloRecorrido))}>
          Empezar recorrido
        </button>
      </section>

      <section style={tarjeta}>
        <h2 style={titulo}><ListChecks size={18} /> Desde planificados</h2>
        <select aria-label="Filtrar planificados por pasillo" style={control} value={pasilloPlanificado} onChange={e => setPasilloPlanificado(e.target.value)}>
          <option value="">Todos los pasillos</option>
          {pasillos.map(p => <option key={p} value={p}>{p}</option>)}
        </select>
        <button className="btn btn-secondary" style={boton} disabled={trabajando}
          onClick={() => abrir(() => sesionesConteoApi.abrirPlanificada(pasilloPlanificado || undefined))}>
          Contar planificados
        </button>
      </section>
    </div>
  );
}
