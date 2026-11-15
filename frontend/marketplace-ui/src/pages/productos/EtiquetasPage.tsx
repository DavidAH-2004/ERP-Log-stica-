import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ArrowLeft, Printer, AlertTriangle } from 'lucide-react';
import CodigoBarrasSvg from '../../components/ui/CodigoBarrasSvg';
import { codigosBarrasApi, type EtiquetaProducto } from '../../api/codigosBarras';
import { esEan13Valido } from '../../utils/ean13';
import './etiquetas.css';

/** Medidas de la hoja Avery 5160 / compatible (carta, 3 × 10 = 30 etiquetas de 2.625" × 1"). */
export const AVERY_5160 = {
  paginaAncho: '8.5in', paginaAlto: '11in',
  margenSuperior: '0.5in', margenLateral: '0.1875in',
  etiquetaAncho: '2.625in', etiquetaAlto: '1in',
  pasoHorizontal: '2.75in', pasoVertical: '1in',
  columnas: 3, filas: 10,
} as const;

const POR_HOJA = AVERY_5160.columnas * AVERY_5160.filas;
const CANTIDAD_MAXIMA = 300;
const MAX_ETIQUETAS_POR_LOTE = 1500; // Evita millones de nodos SVG y bloqueos de impresión.
// Dibujar cientos de etiquetas en pantalla la vuelve lenta: la vista previa muestra solo las primeras hojas.
const HOJAS_EN_VISTA_PREVIA = 2;

// Separación entre columnas = paso horizontal − ancho de etiqueta (2.75 − 2.625 = 0.125in).
const variablesHoja = {
  '--pagina-ancho': AVERY_5160.paginaAncho,
  '--pagina-alto': AVERY_5160.paginaAlto,
  '--margen-superior': AVERY_5160.margenSuperior,
  '--margen-lateral': AVERY_5160.margenLateral,
  '--etiqueta-ancho': AVERY_5160.etiquetaAncho,
  '--etiqueta-alto': AVERY_5160.etiquetaAlto,
  '--separacion-columnas': `${parseFloat(AVERY_5160.pasoHorizontal) - parseFloat(AVERY_5160.etiquetaAncho)}in`,
  '--columnas': String(AVERY_5160.columnas),
  '--filas': String(AVERY_5160.filas),
} as React.CSSProperties;

type Celda = EtiquetaProducto | null; // null = posición vacía (hoja ya usada parcialmente)

function limitar(valor: number, min: number, max: number): number {
  if (!Number.isFinite(valor)) return min;
  return Math.min(max, Math.max(min, Math.trunc(valor)));
}

function Hojas({ hojas }: { hojas: Celda[][] }) {
  return (
    <>
      {hojas.map((hoja, h) => (
        <div className="hoja-etiquetas" style={variablesHoja} key={h}>
          {hoja.map((p, i) =>
            p ? (
              <div className="etiqueta" key={i}>
                <div className="etiqueta-nombre">{p.nombre}</div>
                <CodigoBarrasSvg codigo={p.codigoEAN} alto={70} escalable className="etiqueta-barras" />
                <div className="etiqueta-sku">{p.sku}</div>
              </div>
            ) : (
              <div className="etiqueta etiqueta-vacia" key={i} />
            ),
          )}
        </div>
      ))}
    </>
  );
}

export default function EtiquetasPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  // ?todos=1 → todos los productos activos con código; ?ids=a,b → los seleccionados en Productos.
  const todos = params.get('todos') === '1';
  const idsTexto = params.get('ids') ?? '';
  const ids = useMemo(() => idsTexto.split(',').map(s => s.trim()).filter(Boolean), [idsTexto]);
  const haySolicitud = todos || ids.length > 0;

  const [productos, setProductos] = useState<EtiquetaProducto[]>([]);
  const [cantidadTodos, setCantidadTodos] = useState(1);
  const [cantidades, setCantidades] = useState<Record<string, number>>({});
  const [inicio, setInicio] = useState(1);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState('');

  // Activa las reglas de impresión de etiquetas solo mientras esta página está abierta.
  useEffect(() => {
    document.body.classList.add('imprimiendo-etiquetas');
    const estiloPagina = document.createElement('style');
    estiloPagina.textContent = '@media print { @page { size: letter; margin: 0; } }';
    document.head.appendChild(estiloPagina);
    return () => {
      document.body.classList.remove('imprimiendo-etiquetas');
      estiloPagina.remove();
    };
  }, []);

  useEffect(() => {
    let cancelado = false;
    if (!haySolicitud) {
      setCargando(false);
      return;
    }
    (async () => {
      setCargando(true);
      setError('');
      try {
        const lista = await codigosBarrasApi.etiquetas(todos ? undefined : ids);
        if (cancelado) return;
        setProductos(lista);
        setCantidades(Object.fromEntries(lista.map(p => [p.id, 1])));
      } catch (e) {
        if (!cancelado) setError(e instanceof Error ? e.message : 'No se pudieron cargar los productos.');
      } finally {
        if (!cancelado) setCargando(false);
      }
    })();
    return () => { cancelado = true; };
  }, [todos, ids, haySolicitud]);

  const conCodigo = useMemo(() => productos.filter(p => esEan13Valido(p.codigoEAN)), [productos]);
  const sinCodigo = useMemo(() => productos.filter(p => !esEan13Valido(p.codigoEAN)), [productos]);

  const aplicarCantidadATodos = (valor: number) => {
    const cantidad = limitar(valor, 1, CANTIDAD_MAXIMA);
    setCantidadTodos(cantidad);
    setCantidades(Object.fromEntries(productos.map(p => [p.id, cantidad])));
  };

  const hojas = useMemo(() => {
    const cantidadSolicitada = conCodigo.reduce((total, p) => total + (cantidades[p.id] ?? 0), 0);
    if (cantidadSolicitada > MAX_ETIQUETAS_POR_LOTE) return [];
    const celdas: Celda[] = Array.from({ length: inicio - 1 }, () => null);
    for (const p of conCodigo) {
      for (let i = 0; i < (cantidades[p.id] ?? 0); i++) celdas.push(p);
    }
    const resultado: Celda[][] = [];
    for (let i = 0; i < celdas.length; i += POR_HOJA) {
      const hoja = celdas.slice(i, i + POR_HOJA);
      while (hoja.length < POR_HOJA) hoja.push(null);
      resultado.push(hoja);
    }
    return resultado;
  }, [conCodigo, cantidades, inicio]);

  const totalEtiquetas = conCodigo.reduce((s, p) => s + (cantidades[p.id] ?? 0), 0);

  const campo: React.CSSProperties = {
    width: '5rem', padding: '0.35rem 0.5rem', borderRadius: '6px',
    border: '1px solid var(--border)', background: 'var(--bg-surface)', color: 'var(--text-primary)',
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '1rem', flexWrap: 'wrap' }}>
        <div>
          <h1 style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>Imprimir etiquetas</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.875rem', marginTop: '0.25rem' }}>
            Hoja Avery 5160 o compatible: carta, 30 etiquetas de 2.625" × 1".
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.75rem' }}>
          <button className="btn btn-secondary" onClick={() => navigate('/productos')}>
            <ArrowLeft size={15} /> Volver
          </button>
          <button className="btn btn-primary" disabled={totalEtiquetas === 0 || totalEtiquetas > MAX_ETIQUETAS_POR_LOTE} onClick={() => window.print()}>
            <Printer size={15} /> Imprimir ({totalEtiquetas})
          </button>
        </div>
      </div>

      <div
        role="note"
        style={{ display: 'flex', gap: '0.6rem', alignItems: 'center', padding: '0.75rem 1rem', borderRadius: '8px', background: 'rgba(245, 158, 11, 0.12)', color: 'var(--text-primary)', fontSize: '0.875rem' }}
      >
        <AlertTriangle size={18} color="var(--yellow)" />
        Imprime al 100 % (tamaño real). No uses «Ajustar a la página».
      </div>

      {totalEtiquetas > MAX_ETIQUETAS_POR_LOTE && (
        <div role="alert" style={{ color: 'var(--red)' }}>
          El lote supera 1500 etiquetas. Divide la impresión en lotes más pequeños.
        </div>
      )}      {error && <div style={{ color: 'var(--red)' }}>{error}</div>}
      {cargando && <div style={{ color: 'var(--text-secondary)' }}>Cargando productos…</div>}
      {!cargando && !haySolicitud && (
        <div style={{ color: 'var(--text-secondary)' }}>No hay productos seleccionados. Vuelve a Productos y marca los que quieras imprimir.</div>
      )}

      {!cargando && productos.length > 0 && (
        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', fontSize: '0.875rem', color: 'var(--text-secondary)' }}>
            Empezar en la posición
            <input
              type="number" min={1} max={POR_HOJA} value={inicio} style={campo}
              onChange={e => setInicio(limitar(Number(e.target.value), 1, POR_HOJA))}
            />
            <span style={{ fontSize: '0.8rem' }}>(1–{POR_HOJA}; para reutilizar una hoja ya usada en parte)</span>
          </label>

          <label style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', fontSize: '0.875rem', color: 'var(--text-secondary)' }}>
            Cantidad para todos
            <input
              type="number" min={1} max={CANTIDAD_MAXIMA} value={cantidadTodos} style={campo}
              onChange={e => aplicarCantidadATodos(Number(e.target.value))}
            />
            <span style={{ fontSize: '0.8rem' }}>(cambia la cantidad de los {productos.length} productos; luego puedes ajustar cada uno)</span>
          </label>

          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.875rem' }}>
            <thead>
              <tr style={{ textAlign: 'left', color: 'var(--text-secondary)' }}>
                <th style={{ padding: '0.4rem' }}>SKU</th>
                <th style={{ padding: '0.4rem' }}>Producto</th>
                <th style={{ padding: '0.4rem' }}>Código</th>
                <th style={{ padding: '0.4rem' }}>Cantidad</th>
              </tr>
            </thead>
            <tbody>
              {conCodigo.map(p => (
                <tr key={p.id} style={{ borderTop: '1px solid var(--border)' }}>
                  <td style={{ padding: '0.4rem', fontFamily: 'var(--font-mono)' }}>{p.sku}</td>
                  <td style={{ padding: '0.4rem' }}>{p.nombre}</td>
                  <td style={{ padding: '0.4rem', fontFamily: 'var(--font-mono)' }}>{p.codigoEAN}</td>
                  <td style={{ padding: '0.4rem' }}>
                    <input
                      type="number" min={1} max={CANTIDAD_MAXIMA} style={campo} value={cantidades[p.id] ?? 1}
                      onChange={e => setCantidades(c => ({ ...c, [p.id]: limitar(Number(e.target.value), 1, CANTIDAD_MAXIMA) }))}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {sinCodigo.length > 0 && (
            <div style={{ color: 'var(--red)', fontSize: '0.85rem' }}>
              Sin código (no se imprimen): {sinCodigo.map(p => p.sku).join(', ')}
            </div>
          )}
          <div style={{ color: 'var(--text-secondary)', fontSize: '0.85rem' }}>
            {totalEtiquetas} etiqueta(s) en {hojas.length} hoja(s).
          </div>
        </div>
      )}

      {hojas.length > 0 && (
        <div className="vista-previa" style={{ overflowX: 'auto', padding: '0.5rem 0' }}>
          <Hojas hojas={hojas.slice(0, HOJAS_EN_VISTA_PREVIA)} />
          {hojas.length > HOJAS_EN_VISTA_PREVIA && (
            <div style={{ color: 'var(--text-secondary)', fontSize: '0.875rem' }}>
              Vista previa de las primeras {HOJAS_EN_VISTA_PREVIA} hojas y {hojas.length - HOJAS_EN_VISTA_PREVIA} hojas más.
              Al imprimir salen las {hojas.length} hojas completas.
            </div>
          )}
        </div>
      )}

      {hojas.length > 0 && createPortal(
        <div className="impresion-etiquetas"><Hojas hojas={hojas} /></div>,
        document.body,
      )}
    </div>
  );
}
