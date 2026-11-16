/** Selector de órdenes elegibles con búsqueda paginada. No permite ingresar IDs arbitrarios. */
import { useEffect, useRef, useState } from 'react';
import { ChevronDown, Search } from 'lucide-react';
import { fulfillmentApi, type OrdenParaSeleccion, type PaginaOrdenesSeleccion } from '../../api/fulfillment';
import { rmaApi } from '../../api/rma';

type Props = {
  modo: 'empaque' | 'devolucion';
  ordenId: string;
  numeroOrden: string;
  onSeleccionar: (orden: OrdenParaSeleccion) => void;
  disabled?: boolean;
};

const TAMANO = 25;

export default function SelectorOrdenRegistrada({ modo, ordenId, numeroOrden, onSeleccionar, disabled }: Props) {
  const [abierto, setAbierto] = useState(false);
  const [busqueda, setBusqueda] = useState('');
  const [termino, setTermino] = useState('');
  const [pagina, setPagina] = useState(1);
  const [datos, setDatos] = useState<PaginaOrdenesSeleccion>({ total: 0, pagina: 1, tamano: TAMANO, items: [] });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const contenedor = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => { setTermino(busqueda.trim()); setPagina(1); }, 250);
    return () => window.clearTimeout(timer);
  }, [busqueda]);

  useEffect(() => {
    if (!abierto) return;
    function fuera(event: MouseEvent) {
      if (!contenedor.current?.contains(event.target as Node)) setAbierto(false);
    }
    document.addEventListener('mousedown', fuera);
    return () => document.removeEventListener('mousedown', fuera);
  }, [abierto]);

  useEffect(() => {
    if (!abierto) return;
    let vigente = true;
    setLoading(true); setError('');
    const cargar = modo === 'empaque' ? fulfillmentApi.ordenesParaEmpaque : rmaApi.ordenesElegibles;
    cargar(termino, pagina, TAMANO)
      .then(res => { if (vigente) setDatos(res); })
      .catch(err => { if (vigente) { setDatos({ total: 0, pagina, tamano: TAMANO, items: [] }); setError(err instanceof Error ? err.message : 'No se pudieron consultar las órdenes.'); } })
      .finally(() => { if (vigente) setLoading(false); });
    return () => { vigente = false; };
  }, [abierto, modo, termino, pagina]);

  return (
    <div ref={contenedor} style={{ position: 'relative' }}>
      <button type="button" disabled={disabled} aria-expanded={abierto}
        onClick={() => setAbierto(x => !x)}
        style={{ width: '100%', display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0.6rem 0.75rem', border: '1px solid var(--border)', borderRadius: 6, background: 'var(--bg-surface)', color: 'var(--text-primary)', cursor: disabled ? 'not-allowed' : 'pointer' }}>
        <span>{ordenId ? numeroOrden : 'Seleccionar orden registrada'}</span><ChevronDown size={16} />
      </button>
      {abierto && (
        <div style={{ position: 'relative', marginTop: 4, zIndex: 25, background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: 8, padding: 8, boxShadow: '0 10px 26px rgba(0,0,0,0.14)' }}>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <Search size={15} /><input type="search" aria-label="Buscar número de orden" placeholder="Buscar número de orden..." value={busqueda} onChange={e => setBusqueda(e.target.value)} style={{ flex: 1, minWidth: 0, padding: 7, border: '1px solid var(--border)', borderRadius: 6, color: 'var(--text-primary)', background: 'var(--bg-surface)' }} />
          </div>
          {loading && <p style={{ fontSize: 12 }}>Consultando órdenes...</p>}
          {error && <p role="alert" style={{ color: 'var(--red)', fontSize: 12 }}>{error}</p>}
          {!loading && !error && datos.items.length === 0 && <p style={{ fontSize: 12 }}>No hay órdenes elegibles para esta operación.</p>}
          <div style={{ maxHeight: 220, overflowY: 'auto' }}>
            {!loading && datos.items.map(orden => (
              <button key={orden.id} type="button" onClick={() => { onSeleccionar(orden); setAbierto(false); }}
                style={{ display: 'block', width: '100%', textAlign: 'left', padding: '8px 6px', background: orden.id === ordenId ? 'var(--accent-glow)' : 'transparent', color: 'var(--text-primary)', border: 0, borderBottom: '1px solid var(--border)', cursor: 'pointer' }}>
                {orden.numeroOrden}
              </button>
            ))}
          </div>
          {!loading && datos.total > TAMANO && (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6, marginTop: 8 }}>
              <button type="button" disabled={pagina <= 1} onClick={() => setPagina(x => x - 1)}>Anterior</button>
              <span style={{ fontSize: 11 }}>{pagina} / {Math.ceil(datos.total / TAMANO)}</span>
              <button type="button" disabled={pagina * TAMANO >= datos.total} onClick={() => setPagina(x => x + 1)}>Siguiente</button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
