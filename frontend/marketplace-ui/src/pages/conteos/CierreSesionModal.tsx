import { useState } from 'react';
import Modal from '../../components/ui/Modal';
import type { AccionCierre, DecisionCierre, ItemSesion } from '../../api/sesionesConteo';

interface CierreSesionModalProps {
  faltan: ItemSesion[];
  enviando: boolean;
  onCancelar: () => void;
  onConfirmar: (decisiones: DecisionCierre[]) => void;
}

const boton: React.CSSProperties = { minHeight: 44, flex: 1 };

/** Cerrar pasillo: decidir, uno por uno, qué pasa con lo que nadie escaneó. */
export default function CierreSesionModal({ faltan, enviando, onCancelar, onConfirmar }: CierreSesionModalProps) {
  const [decisiones, setDecisiones] = useState<Record<string, AccionCierre>>({});
  const decidir = (id: string, accion: AccionCierre) => setDecisiones(d => ({ ...d, [id]: accion }));
  const todasDecididas = faltan.every(f => decisiones[f.stockFisicoId]);

  return (
    <Modal title="Cerrar sesión" isOpen={true} onClose={onCancelar}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
        {faltan.length === 0 ? (
          <p style={{ margin: 0 }}>Se contaron todos los productos esperados.</p>
        ) : (
          <>
            <p style={{ margin: 0, color: 'var(--text-secondary)' }}>
              Estos productos no se escanearon. «No hay ninguno» registra 0 (si no coincide con el sistema, pasa a reconteo);
              «Pendiente» lo deja para otra sesión.
            </p>
            <button type="button" className="btn btn-ghost" style={{ minHeight: 44 }}
              onClick={() => setDecisiones(Object.fromEntries(faltan.map(f => [f.stockFisicoId, 'Pendiente' as AccionCierre])))}>
              Marcar todos como pendientes
            </button>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', maxHeight: '50vh', overflowY: 'auto' }}>
              {faltan.map(f => {
                const actual = decisiones[f.stockFisicoId];
                return (
                  <div key={f.stockFisicoId} style={{ border: '1px solid var(--border)', borderRadius: 8, padding: '0.5rem' }}>
                    <div style={{ fontWeight: 600 }}>{f.nombre}</div>
                    <div style={{ fontSize: '0.8rem', color: 'var(--text-muted)' }}>{f.sku} · {f.ubicacionCodigo}</div>
                    <div style={{ display: 'flex', gap: 8, marginTop: 6 }}>
                      <button type="button" style={boton} aria-pressed={actual === 'NoHay'}
                        className={actual === 'NoHay' ? 'btn btn-primary' : 'btn btn-secondary'}
                        onClick={() => decidir(f.stockFisicoId, 'NoHay')}>No hay ninguno</button>
                      <button type="button" style={boton} aria-pressed={actual === 'Pendiente'}
                        className={actual === 'Pendiente' ? 'btn btn-primary' : 'btn btn-secondary'}
                        onClick={() => decidir(f.stockFisicoId, 'Pendiente')}>Pendiente</button>
                    </div>
                  </div>
                );
              })}
            </div>
          </>
        )}
        <div style={{ display: 'flex', gap: 8 }}>
          <button type="button" className="btn btn-secondary" style={boton} onClick={onCancelar}>Volver</button>
          <button type="button" className="btn btn-primary" style={boton} disabled={!todasDecididas || enviando}
            onClick={() => onConfirmar(faltan.map(f => ({ stockFisicoId: f.stockFisicoId, accion: decisiones[f.stockFisicoId] })))}>
            {enviando ? 'Cerrando…' : 'Confirmar cierre'}
          </button>
        </div>
      </div>
    </Modal>
  );
}
