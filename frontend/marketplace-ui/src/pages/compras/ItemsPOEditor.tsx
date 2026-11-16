import { useEffect, useState } from 'react';
import type { ItemPORequest } from '../../api/compras';
import ProductoPOSelector from './ProductoPOSelector';

const vacio = (): ItemPORequest => ({ sku: '', nombreProducto: '', cantidad: 1, precioUnitario: 0 });
const centavos = (precio: number) => Math.round(precio * 100);

export function itemPOValido(item: ItemPORequest): boolean {
  return !!item.sku.trim() && !!item.nombreProducto.trim() &&
    Number.isInteger(item.cantidad) && item.cantidad > 0 && item.cantidad <= 2147483647 &&
    Number.isFinite(item.precioUnitario) && item.precioUnitario > 0 &&
    item.precioUnitario === Number(item.precioUnitario.toFixed(2)) &&
    Number.isSafeInteger(centavos(item.precioUnitario) * item.cantidad);
}

export function totalesPO(items: ItemPORequest[]) {
  const subtotal = items.reduce((total, item) => total + centavos(item.precioUnitario) * item.cantidad, 0);
  // Igual que decimal.Round del API: los medios centavos se redondean al par.
  const baseIva = Math.floor(subtotal * 13 / 100);
  const resto = (subtotal * 13) % 100;
  const iva = baseIva + (resto > 50 || (resto === 50 && baseIva % 2 !== 0) ? 1 : 0);
  return { subtotal: subtotal / 100, iva: iva / 100, total: (subtotal + iva) / 100 };
}

interface Props {
  items: ItemPORequest[];
  onChange: (items: ItemPORequest[]) => void;
  onPendingChange: (pending: boolean) => void;
  disabled: boolean;
}

export default function ItemsPOEditor({ items, onChange, onPendingChange, disabled }: Props) {
  const [draft, setDraft] = useState<ItemPORequest>(vacio);
  const [editando, setEditando] = useState<number | null>(null);
  const [error, setError] = useState('');
  const pendiente = editando !== null || !!draft.sku || draft.precioUnitario !== 0 || draft.cantidad !== 1;
  useEffect(() => onPendingChange(pendiente), [pendiente, onPendingChange]);
  const limpiar = () => { setDraft(vacio()); setEditando(null); setError(''); };
  const agregar = () => {
    if (!itemPOValido(draft)) {
      setError('Selecciona un producto y escribe una cantidad entera positiva y un precio mayor que cero con máximo dos decimales.');
      return;
    }
    if (items.some((item, index) => index !== editando && item.sku.trim().toLowerCase() === draft.sku.trim().toLowerCase())) {
      setError('Este producto ya está en la lista. Edita su cantidad en la línea existente.');
      return;
    }
    if (editando === null && items.length >= 10) {
      setError('Llegaste al límite de 10 productos por orden.');
      return;
    }
    const nuevos = editando === null ? [...items, draft] : items.map((item, index) => index === editando ? draft : item);
    if (nuevos.reduce((sum, item) => sum + item.cantidad, 0) > 2147483647 ||
        nuevos.reduce((sum, item) => sum + centavos(item.precioUnitario) * item.cantidad, 0) > Number.MAX_SAFE_INTEGER / 113) {
      setError('La cantidad o el importe total supera el máximo admitido.');
      return;
    }
    onChange(nuevos);
    limpiar();
  };
  const totales = totalesPO(items);
  const dinero = (valor: number) => valor.toLocaleString('es-SV', { style: 'currency', currency: 'USD' });
  const estilo = { width: '100%', padding: '0.5rem', borderRadius: 6, background: 'var(--bg-card)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' as const };

  return <section aria-label="Ítems de la Orden" style={{ padding: '0.75rem', border: '1px solid var(--border)', borderRadius: 8 }}>
    <h3 style={{ margin: '0 0 0.75rem', fontSize: '1rem' }}>Ítems de la Orden ({items.length}/10)</h3>
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: '0.5rem' }}>
      <div><span>Producto</span><ProductoPOSelector sku={draft.sku} nombre={draft.nombreProducto} disabled={disabled}
        onSeleccionar={producto => setDraft(item => ({ ...item, sku: producto.sku, nombreProducto: producto.nombre, precioUnitario: producto.precioCompraReferencia ?? 0 }))} /></div>
      <label>Cantidad<input aria-label="Cantidad del producto" type="number" min="1" step="1" value={draft.cantidad || ''}
        disabled={disabled} onChange={e => setDraft(item => ({ ...item, cantidad: Number(e.target.value) }))} style={estilo} /></label>
      <label>Precio unitario ($)<input aria-label="Precio unitario del producto" type="number" min="0.01" step="0.01" value={draft.precioUnitario || ''}
        disabled={disabled} onChange={e => setDraft(item => ({ ...item, precioUnitario: Number(e.target.value) }))} style={estilo} /></label>
    </div>
    <p style={{ fontSize: 12, color: 'var(--text-secondary)' }}>El precio sugerido corresponde a la última compra. Puedes modificarlo para esta orden.</p>
    <div style={{ display: 'flex', gap: 8 }}>
      <button type="button" className="btn btn-secondary" disabled={disabled || (items.length >= 10 && editando === null)} onClick={agregar}>
        {editando === null ? '+ Agregar a la lista' : 'Guardar ítem'}
      </button>
      {pendiente && <button type="button" className="btn btn-secondary" disabled={disabled} onClick={limpiar}>Cancelar ítem</button>}
    </div>
    {error && <p role="alert" style={{ color: 'var(--red)' }}>{error}</p>}
    {items.length === 10 && <p role="status">Límite alcanzado: 10 productos por orden. Puedes editar o eliminar ítems.</p>}
    {items.length === 0 ? <p>Agrega al menos un producto a la lista para crear la orden.</p> : <div style={{ overflow: 'auto', maxHeight: 260, marginTop: 12 }}>
      <table aria-label="Productos de la orden" style={{ width: '100%', textAlign: 'left', borderCollapse: 'collapse' }}>
        <thead><tr>{['Producto', 'Cantidad', 'Precio', 'Subtotal', 'Acciones'].map(t => <th key={t} style={{ padding: 6, position: 'sticky', top: 0, background: 'var(--bg-surface)' }}>{t}</th>)}</tr></thead>
        <tbody>{items.map((item, index) => <tr key={item.sku} style={{ borderTop: '1px solid var(--border)' }}>
          <td style={{ padding: 6 }}>{item.nombreProducto}<br /><small>{item.sku}</small></td>
          <td>{item.cantidad}</td><td>{dinero(item.precioUnitario)}</td><td>{dinero(centavos(item.precioUnitario) * item.cantidad / 100)}</td>
          <td><button type="button" className="btn btn-secondary btn-sm" disabled={disabled || pendiente} aria-label={`Editar ${item.sku}`} onClick={() => { setDraft({ ...item }); setEditando(index); setError(''); }}>Editar</button>{' '}
            <button type="button" className="btn btn-secondary btn-sm" disabled={disabled || pendiente} aria-label={`Eliminar ${item.sku}`} onClick={() => onChange(items.filter((_, i) => i !== index))}>Eliminar</button></td>
        </tr>)}</tbody>
      </table>
    </div>}
    <div aria-live="polite" style={{ textAlign: 'right', marginTop: 12 }}>
      <div>Subtotal: {dinero(totales.subtotal)} · IVA (13%): {dinero(totales.iva)}</div>
      <strong>Total PO (con IVA): {dinero(totales.total)}</strong>
    </div>
    {pendiente && <p style={{ fontSize: 12 }}>Agrega o guarda el ítem pendiente, o cancélalo, antes de crear la orden.</p>}
  </section>;
}
