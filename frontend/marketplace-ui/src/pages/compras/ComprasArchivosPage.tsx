import { useCallback, useEffect, useState } from 'react';
import { Download, Eye, FolderArchive, Printer, RefreshCcw } from 'lucide-react';
import { comprasArchivosApi, type ArchivoCompraResumen } from '../../api/comprasArchivos';

const dateTime = (value: string) => new Date(value).toLocaleString('es-SV');
const money = (value: number) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);

export default function ComprasArchivosPage() {
  const [numeroPO, setNumeroPO] = useState('');
  const [proveedor, setProveedor] = useState('');
  const [desde, setDesde] = useState('');
  const [hasta, setHasta] = useState('');
  const [pagina, setPagina] = useState(1);
  const [archivos, setArchivos] = useState<ArchivoCompraResumen[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [archivando, setArchivando] = useState(false);
  const [error, setError] = useState('');

  const cargar = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await comprasArchivosApi.listar({ numeroPO, proveedor, desde, hasta, pagina });
      setArchivos(response.archivos);
      setTotal(response.total);
    } catch (e) {
      setArchivos([]);
      setError(e instanceof Error ? e.message : 'No se pudieron cargar los documentos de compras.');
    } finally { setLoading(false); }
  }, [numeroPO, proveedor, desde, hasta, pagina]);

  useEffect(() => { void cargar(); }, [cargar]);

  const obtenerPdf = async (po: ArchivoCompraResumen, accion: 'ver' | 'descargar' | 'imprimir') => {
    // Open the tab while the click has transient user activation, before async API fetch.
    const tab = accion !== 'descargar' ? window.open('about:blank', '_blank') : null;
    try {
      const pdf = await comprasArchivosApi.pdf(po.ordenCompraId);
      const url = URL.createObjectURL(pdf);
      if (accion === 'descargar') {
        const a = document.createElement('a');
        a.href = url;
        a.download = po.numeroPO + '.pdf';
        document.body.appendChild(a);
        a.click();
        a.remove();
      } else if (tab) {
        tab.location.replace(url);
      } else {
        setError('El navegador bloqueó la pestaña. Habilita las ventanas emergentes para este ERP.');
      }
      // Keep the URL available until the PDF viewer has loaded it.
      window.setTimeout(() => URL.revokeObjectURL(url), 120_000);
    } catch (e) {
      tab?.close();
      setError(e instanceof Error ? e.message : 'No fue posible abrir el PDF.');
    }
  };

  const archivarHistoricos = async () => {
    setArchivando(true);
    try {
      const resultado = await comprasArchivosApi.conciliar();
      setError(resultado.archivadas > 0 ? `Se archivaron ${resultado.archivadas} órdenes anteriores.` : 'No hay compras anteriores pendientes de archivar.');
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Error al conciliar compras anteriores.');
    } finally { setArchivando(false); }
  };

  const input: React.CSSProperties = { background: 'var(--bg-surface)', color: 'var(--text-primary)', border: '1px solid var(--border)', borderRadius: 7, padding: '9px 12px', minWidth: 130 };
  const btn: React.CSSProperties = { ...input, cursor: 'pointer', minWidth: 0, display: 'inline-flex', gap: 6, alignItems: 'center' };

  return <div style={{ display: 'flex', flexDirection: 'column', gap: 22 }}>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
      <div>
        <h1 style={{ margin: 0, fontSize: '1.6rem' }}>Compras Archivos</h1>
        <p style={{ color: 'var(--text-secondary)', margin: '6px 0' }}>Archivo digital de órdenes de compra · LOGIMKT</p>
      </div>
      <div style={{ display: 'flex', gap: 8 }}>
        <button style={btn} onClick={() => void archivarHistoricos()} disabled={archivando}>{archivando ? 'Archivando…' : 'Archivar compras anteriores'}</button>
        <button style={btn} onClick={() => void cargar()} disabled={loading}><RefreshCcw size={15} /> Actualizar</button>
      </div>
    </div>
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12 }}>
      <input aria-label="Buscar número PO" style={input} placeholder="Número de PO" value={numeroPO} onChange={e => { setPagina(1); setNumeroPO(e.target.value); }} />
      <input aria-label="Buscar proveedor" style={input} placeholder="Nombre del proveedor" value={proveedor} onChange={e => { setPagina(1); setProveedor(e.target.value); }} />
      <label style={{ display: 'grid', gap: 2, color: 'var(--text-secondary)', fontSize: 12 }}>Desde
        <input type="date" style={input} value={desde} onChange={e => { setPagina(1); setDesde(e.target.value); }} /></label>
      <label style={{ display: 'grid', gap: 2, color: 'var(--text-secondary)', fontSize: 12 }}>Hasta
        <input type="date" style={input} value={hasta} onChange={e => { setPagina(1); setHasta(e.target.value); }} /></label>
    </div>
    {error && <div role="status" style={{ border: '1px solid var(--border)', padding: 12, borderRadius: 8 }}>{error}</div>}
    <div style={{ border: '1px solid var(--border)', borderRadius: 10, overflowX: 'auto', background: 'var(--bg-surface)' }}>
      <table style={{ borderCollapse: 'collapse', width: '100%', minWidth: 920, textAlign: 'left' }}>
        <thead><tr style={{ background: 'var(--bg-secondary)', borderBottom: '1px solid var(--border)' }}>
          {['Número PO', 'Proveedor', 'Fecha', 'Ítems', 'Total', 'Estado', 'Archivo PDF'].map(s => <th key={s} style={{ padding: 13, fontSize: 12 }}>{s}</th>)}
        </tr></thead>
        <tbody>
          {archivos.map(po => <tr key={po.id} style={{ borderBottom: '1px solid var(--border)' }}>
            <td style={{ padding: 12, fontSize: 12 }}>{po.numeroPO}</td>
            <td style={{ padding: 12 }}>{po.proveedorNombre}</td>
            <td style={{ padding: 12, whiteSpace: 'nowrap' }}>{dateTime(po.fechaCreacionUtc)}</td>
            <td style={{ padding: 12 }}>{po.cantidadItems}</td>
            <td style={{ padding: 12 }}>{money(po.montoTotal)}</td>
            <td style={{ padding: 12 }}>{po.estado}</td>
            <td style={{ padding: 12, whiteSpace: 'nowrap' }}>
              <button style={btn} onClick={() => void obtenerPdf(po, 'ver')} title="Ver el PDF"><Eye size={14}/> Ver</button>{' '}
              <button style={btn} onClick={() => void obtenerPdf(po, 'descargar')} title="Descargar PDF"><Download size={14}/> Descargar</button>{' '}
              <button style={btn} onClick={() => void obtenerPdf(po, 'imprimir')} title="Abrir para imprimir"><Printer size={14}/> Imprimir</button>
            </td>
          </tr>)}
        </tbody>
      </table>
      {!loading && archivos.length === 0 && <div style={{ textAlign: 'center', padding: 48, color: 'var(--text-secondary)' }}>
        <FolderArchive size={30} style={{ margin: '0 auto 12px' }}/>
        <div>Aún no hay compras archivadas</div>
      </div>}
      {loading && <div style={{ padding: 25, textAlign: 'center' }}>Cargando documentos…</div>}
    </div>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', color: 'var(--text-secondary)', gap: 8 }}>
      <span>{total} documentos · Página {pagina}</span>
      <div style={{ display: 'flex', gap: 8 }}>
        <button style={btn} disabled={pagina <= 1} onClick={() => setPagina(p => p - 1)}>Anterior</button>
        <button style={btn} disabled={pagina * 25 >= total} onClick={() => setPagina(p => p + 1)}>Siguiente</button>
      </div>
    </div>
  </div>;
}
