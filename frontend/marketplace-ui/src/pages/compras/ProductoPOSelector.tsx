/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

/** Selector de productos activos para compras. Consulta el catálogo completo desde API. */
import { useEffect, useRef, useState } from 'react';
import { ChevronDown, Search, RefreshCw } from 'lucide-react';
import { comprasApi, type ProductoParaCompra } from '../../api/compras';

interface Props {
  sku: string;
  nombre: string;
  onSeleccionar: (producto: ProductoParaCompra) => void;
  disabled?: boolean;
}

const PAGE_SIZE = 25;

export default function ProductoPOSelector({ sku, nombre, onSeleccionar, disabled = false }: Props) {
  const [abierto, setAbierto] = useState(false);
  const [busqueda, setBusqueda] = useState('');
  const [texto, setTexto] = useState('');
  const [pagina, setPagina] = useState(1);
  const [total, setTotal] = useState(0);
  const [opciones, setOpciones] = useState<ProductoParaCompra[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState('');
  const [actualizacion, setActualizacion] = useState(0);
  const refContenedor = useRef<HTMLDivElement>(null);
  const refBusqueda = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const temporizador = window.setTimeout(() => {
      setTexto(busqueda.trim());
      setPagina(1);
    }, 250);
    return () => window.clearTimeout(temporizador);
  }, [busqueda]);

  useEffect(() => {
    if (!abierto) return;
    const cerrarAlHacerClicFuera = (event: MouseEvent) => {
      if (!refContenedor.current?.contains(event.target as Node)) setAbierto(false);
    };
    document.addEventListener('mousedown', cerrarAlHacerClicFuera);
    refBusqueda.current?.focus();
    return () => {
      document.removeEventListener('mousedown', cerrarAlHacerClicFuera);
    };
  }, [abierto]);

  useEffect(() => {
    if (!abierto) return;
    let vigente = true;
    setCargando(true);
    setError('');
    comprasApi.productosDisponibles({ q: texto, pagina, tamano: PAGE_SIZE })
      .then(res => {
        if (!vigente) return;
        setOpciones(res.items ?? []);
        setTotal(res.total ?? 0);
      })
      .catch(err => {
        if (!vigente) return;
        setOpciones([]);
        setTotal(0);
        setError(err instanceof Error ? err.message : 'No fue posible consultar el catálogo.');
      })
      .finally(() => { if (vigente) setCargando(false); });
    return () => { vigente = false; };
  }, [abierto, texto, pagina, actualizacion]);

  const inputStyle = {
    width: '100%', padding: '0.55rem 0.65rem', boxSizing: 'border-box' as const,
    borderRadius: 6, border: '1px solid var(--border)',
    background: 'var(--bg-card)', color: 'var(--text-primary)',
  };

  return (
    <div ref={refContenedor} onKeyDownCapture={event => {
      if (abierto && event.key === 'Escape') { event.stopPropagation(); setAbierto(false); }
    }} style={{ position: 'relative', minWidth: 0 }}>
      <button
        type="button"
        aria-expanded={abierto}
        aria-haspopup="listbox"
        aria-label="Seleccionar producto registrado"
        disabled={disabled}
        onClick={() => { setAbierto(valor => !valor); setActualizacion(v => v + 1); }}
        style={{
          ...inputStyle, cursor: disabled ? 'not-allowed' : 'pointer', textAlign: 'left',
          display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8,
        }}
      >
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: sku ? 'var(--text-primary)' : 'var(--text-secondary)' }}>
          {sku ? `${sku} — ${nombre}` : 'Seleccionar producto registrado…'}
        </span>
        <ChevronDown size={16} style={{ flexShrink: 0 }} />
      </button>
      {abierto && (
        <div style={{
          position: 'relative', marginTop: 4, zIndex: 1,
          background: 'var(--bg-card)', border: '1px solid var(--border)',
          borderRadius: 8, boxShadow: '0 10px 28px rgba(0,0,0,0.24)', padding: 8,
        }}>
          <div style={{ display: 'flex', gap: 6, alignItems: 'center', marginBottom: 7 }}>
            <Search size={15} style={{ color: 'var(--text-secondary)', flexShrink: 0 }} />
            <input
              ref={refBusqueda}
              type="search"
              aria-label="Buscar producto por SKU o nombre"
              placeholder="Buscar SKU o nombre…"
              maxLength={100}
              value={busqueda}
              onChange={event => setBusqueda(event.target.value)}
              style={{ ...inputStyle, minWidth: 0, flex: 1 }}
            />
            <button type="button" title="Actualizar productos" aria-label="Actualizar productos" onClick={() => setActualizacion(v => v + 1)}
              style={{ border: '1px solid var(--border)', background: 'var(--bg-surface)', borderRadius: 6, padding: 7, cursor: 'pointer', color: 'var(--text-primary)' }}>
              <RefreshCw size={15} />
            </button>
          </div>
          <div role="listbox" aria-label="Productos activos del catálogo" style={{ maxHeight: 245, overflowY: 'auto' }}>
            {cargando && <p style={{ color: 'var(--text-secondary)', fontSize: 12, padding: 8 }}>Consultando productos…</p>}
            {!cargando && error && <p role="alert" style={{ color: 'var(--red)', fontSize: 12, padding: 8 }}>{error}</p>}
            {!cargando && !error && opciones.length === 0 && (
              <p style={{ color: 'var(--text-secondary)', fontSize: 12, padding: 8 }}>No hay productos activos que coincidan.</p>
            )}
            {!cargando && !error && opciones.map(producto => (
              <button key={producto.id} type="button" role="option" aria-selected={producto.sku === sku}
                onClick={() => { onSeleccionar(producto); setAbierto(false); }}
                style={{ display: 'block', width: '100%', padding: '0.55rem 0.45rem', textAlign: 'left',
                  border: 'none', borderBottom: '1px solid var(--border)', cursor: 'pointer',
                  background: producto.sku === sku ? 'var(--accent-glow)' : 'transparent', color: 'var(--text-primary)' }}>
                <strong style={{ fontSize: 12 }}>{producto.sku}</strong>
                <div style={{ fontSize: 12 }}>{producto.nombre}</div>
                <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
                  {producto.precioCompraReferencia != null
                    ? `Última compra: $${producto.precioCompraReferencia.toFixed(2)}`
                    : 'Sin precio de compra histórico'}
                </div>
              </button>
            ))}
          </div>
          {!cargando && !error && total > PAGE_SIZE && (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 8, gap: 4 }}>
              <button type="button" disabled={pagina === 1} onClick={() => setPagina(p => Math.max(1, p - 1))}>Anterior</button>
              <span style={{ fontSize: 11, color: 'var(--text-secondary)' }}>{pagina} / {Math.ceil(total / PAGE_SIZE)}</span>
              <button type="button" disabled={pagina * PAGE_SIZE >= total} onClick={() => setPagina(p => p + 1)}>Siguiente</button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
