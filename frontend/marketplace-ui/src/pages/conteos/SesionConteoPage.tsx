import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ArrowLeft, Camera } from 'lucide-react';
import {
  sesionesConteoApi,
  type DecisionCierre,
  type ItemSesion,
  type OpcionUbicacion,
  type ResumenSesion,
} from '../../api/sesionesConteo';
import { codigosBarrasApi } from '../../api/codigosBarras';
import { ApiError } from '../../api/client';
import { esEan13Valido, normalizarLectura } from '../../utils/ean13';
import { COLOR_TONO, estiloResultado, type ResultadoLectura } from '../../utils/resultadoLectura';
import EscanerCamaraModal from '../productos/EscanerCamaraModal';
import CierreSesionModal from './CierreSesionModal';

type Pestana = 'faltan' | 'contados' | 'reconteo' | 'fuera';

interface Aviso {
  resultado: ResultadoLectura | 'SinConexion';
  mensaje: string;
  detalle?: string;
}

interface Pendiente {
  codigo: string;
  lecturaId: string;
  /** F428: never register a quantity until the real backend confirms the product. */
  verificado: boolean;
  sku?: string;
  nombre?: string;
  esAlias?: boolean;
  codigoVigente?: string;
}

interface LecturaEnviada {
  lecturaId: string;
  codigo: string;
  cantidad: number;
  stockFisicoId?: string;
}

/** Identificador de lectura. crypto.randomUUID solo existe en HTTPS/localhost; por IP en la red local se usa getRandomValues. */
function nuevoId(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6] & 0x0f) | 0x40;
  b[8] = (b[8] & 0x3f) | 0x80;
  const h = [...b].map(x => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

const control: React.CSSProperties = {
  minHeight: 52, width: '100%', boxSizing: 'border-box', padding: '0.6rem 0.8rem', borderRadius: 10,
  border: '2px solid var(--border)', background: 'var(--bg-surface)', color: 'var(--text-primary)', fontSize: '1.15rem',
};
const boton: React.CSSProperties = { minHeight: 48, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 8 };

export default function SesionConteoPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const [resumen, setResumen] = useState<ResumenSesion | null>(null);
  const [errorCarga, setErrorCarga] = useState('');
  const [escaneo, setEscaneo] = useState('');
  const [pendiente, setPendiente] = useState<Pendiente | null>(null);
  const [cantidad, setCantidad] = useState('');
  const [opciones, setOpciones] = useState<OpcionUbicacion[] | null>(null);
  const [aviso, setAviso] = useState<Aviso | null>(null);
  const [pestana, setPestana] = useState<Pestana>('faltan');
  const [camara, setCamara] = useState(false);
  const [cierre, setCierre] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [reintentoPendiente, setReintentoPendiente] = useState(false);
  // F432: after an uncertain network outcome, retry the identical request.
  const lecturaEnviadaRef = useRef<LecturaEnviada | null>(null);
  const enviandoRef = useRef(false);
  const escaneoRef = useRef<HTMLInputElement>(null);
  const cantidadRef = useRef<HTMLInputElement>(null);
  // A stale async lookup must not clear or validate the operator's next scan.
  const lookupActualRef = useRef<string | null>(null);

  const cargar = useCallback(async () => {
    try {
      setResumen(await sesionesConteoApi.resumen(id));
      setErrorCarga('');
    } catch (e) {
      setErrorCarga(e instanceof Error ? e.message : 'No se pudo cargar la sesión.');
    }
  }, [id]);

  useEffect(() => { cargar(); }, [cargar]);

  const abierta = resumen?.sesion.estado === 'Abierta';

  const mostrar = (a: Aviso) => {
    setAviso(a);
    if (a.resultado !== 'SinConexion' && estiloResultado(a.resultado).vibrar) navigator.vibrate?.(200);
    if (a.resultado === 'SinConexion') navigator.vibrate?.(200);
  };

  const volverAEscanear = () => {
    lookupActualRef.current = null;
    lecturaEnviadaRef.current = null;
    setReintentoPendiente(false);
    setPendiente(null);
    setCantidad('');
    setOpciones(null);
    setEscaneo('');
    setTimeout(() => escaneoRef.current?.focus(), 0);
  };

  /** Pistola (13 dígitos + Enter), cámara o escritura manual. Todavía no envía nada al servidor. */
  const recibirCodigo = async (texto: string) => {
    const codigo = normalizarLectura(texto);
    setEscaneo('');
    if (!esEan13Valido(codigo)) {
      mostrar({ resultado: 'CodigoInvalido', mensaje: 'Código inválido: vuelve a escanear.' });
      setTimeout(() => escaneoRef.current?.focus(), 0);
      return;
    }
    setAviso(null);
    setOpciones(null);
    setCantidad('');
    const lecturaId = nuevoId();
    lookupActualRef.current = lecturaId;
    setPendiente({ codigo, lecturaId, verificado: false });
    try {
      const p = await codigosBarrasApi.buscarPorCodigo(codigo);
      if (lookupActualRef.current !== lecturaId) return;
      // F440: a 200 response is not enough. Never turn a partial/corrupt
      // catalog response, or a different EAN, into a verified stock write.
      // F474: current EAN must match exactly, OR the server must
      // explicitly attest an immutable historical alias of this same SKU.
      // Never trust a partial/mismatched alias response as approval to count.
      const codigoVigente = Boolean(p) && esEan13Valido(p.codigoEAN) &&
        p.codigoEAN === codigo && p.esAlias !== true &&
        (p.codigoLeido === undefined || p.codigoLeido === codigo);
      const aliasConfirmado = Boolean(p) && p.esAlias === true &&
        p.codigoLeido === codigo && p.codigoEAN !== codigo &&
        esEan13Valido(p.codigoEAN);
      if (!p || (!codigoVigente && !aliasConfirmado) ||
          typeof p.id !== 'string' ||
          !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(p.id) ||
          typeof p.sku !== 'string' || !p.sku.trim() ||
          typeof p.nombre !== 'string' || !p.nombre.trim())
        throw new Error('Respuesta de catálogo no verificable.');

      // A delayed response must not authenticate a *different* pending scan,
      // including a rescan of the same barcode after Discard.
      setPendiente(actual => (actual?.lecturaId === lecturaId
        ? { ...actual, sku: p.sku, nombre: p.nombre,
            verificado: true, esAlias: aliasConfirmado, codigoVigente: p.codigoEAN }
        : actual));
      setTimeout(() => cantidadRef.current?.focus(), 0);
    } catch (e) {
      if (lookupActualRef.current !== lecturaId) return;
      if (e instanceof ApiError && e.status === 404) {
        mostrar({ resultado: 'ProductoNoEncontrado', mensaje: `No hay producto con el código ${codigo}.` });
      } else if (e instanceof ApiError && e.status === 0) {
        mostrar({ resultado: 'SinConexion', mensaje: 'Sin conexión: no se pudo verificar el producto. Vuelve a escanear cuando se recupere.' });
      } else {
        mostrar({ resultado: 'CodigoInvalido', mensaje: 'No se pudo verificar el producto. Vuelve a intentarlo.' });
      }
      // Fail closed for 401/403/429/5xx and transport failures too:
      // no count can be recorded with an unverified EAN.
      volverAEscanear();
    }
  };

  const guardar = async (stockFisicoId?: string) => {
    if (!pendiente?.verificado || enviandoRef.current) return;
    // Once POST started its result may be uncertain. Preserve the exact
    // UUID/EAN/quantity/location; neither Enter nor another button can mutate it.
    const anterior = lecturaEnviadaRef.current;
    const n = Number(cantidad);
    if (!anterior && (cantidad.trim() === '' || !Number.isSafeInteger(n) ||
        n < 0 || n > 2147483647)) {
      cantidadRef.current?.focus();
      return;
    }
    const solicitud: LecturaEnviada = anterior ?? {
      lecturaId: pendiente.lecturaId, codigo: pendiente.codigo,
      cantidad: n, stockFisicoId,
    };
    enviandoRef.current = true;
    lecturaEnviadaRef.current = solicitud;
    setEnviando(true);
    try {
      const r = await sesionesConteoApi.leer(id, solicitud);
      lecturaEnviadaRef.current = null;
      setReintentoPendiente(false);
      if (r.resultado === 'ElegirUbicacion') {
        // La lectura no se guardó: la siguiente (con la posición elegida) lleva otro id.
        setOpciones(r.opciones ?? []);
        setPendiente(p => (p ? { ...p, lecturaId: nuevoId() } : p));
        mostrar({ resultado: r.resultado, mensaje: r.mensaje });
        return;
      }
      mostrar({
        resultado: r.resultado,
        mensaje: r.mensaje,
        detalle: [r.sku, r.nombre, r.ubicacionCodigo].filter(Boolean).join(' · '),
      });
      volverAEscanear();
      cargar();
    } catch (e) {
      if (e instanceof ApiError && (e.status === 0 || e.status === 408 || e.status >= 500)) {
        // F432/F433: a network error, timeout, or gateway/server error does NOT
        // prove the first request was rolled back. Retry the same full payload.
        setReintentoPendiente(true);
        mostrar({ resultado: 'SinConexion', mensaje: e.status === 0
          ? 'Sin conexión: reintenta guardar la misma lectura. No vuelvas a escanear.'
          : 'El servidor no confirmó la lectura: reintenta guardar la misma lectura. No vuelvas a escanear.' });
      } else {
        lecturaEnviadaRef.current = null;
        setReintentoPendiente(false);
        mostrar({ resultado: 'CodigoInvalido', mensaje: e instanceof Error ? e.message : 'No se pudo registrar la lectura.' });
        volverAEscanear();
      }
    } finally {
      enviandoRef.current = false;
      setEnviando(false);
    }
  };

  const cerrar = async (decisiones: DecisionCierre[]) => {
    // F437: a pending unknown POST must be reconciled, not closed/cancelled.
    if (reintentoPendiente || enviandoRef.current) return;
    setEnviando(true);
    try {
      setResumen(await sesionesConteoApi.cerrar(id, decisiones));
      setCierre(false);
    } catch (e) {
      mostrar({ resultado: 'SesionNoAbierta', mensaje: e instanceof Error ? e.message : 'No se pudo cerrar la sesión.' });
    } finally {
      setEnviando(false);
    }
  };

  const cancelarSesion = async () => {
    if (reintentoPendiente || enviandoRef.current) return;
    if (!window.confirm('¿Cancelar esta sesión? Los conteos ya registrados se conservan.')) return;
    try {
      await sesionesConteoApi.cancelar(id);
      cargar();
    } catch (e) {
      mostrar({ resultado: 'SesionNoAbierta', mensaje: e instanceof Error ? e.message : 'No se pudo cancelar la sesión.' });
    }
  };

  if (errorCarga) return <div role="alert" style={{ color: 'var(--red)' }}>{errorCarga}</div>;
  if (!resumen) return <div style={{ color: 'var(--text-secondary)' }}>Cargando sesión…</div>;

  const s = resumen.sesion;
  const listas: Record<Pestana, { titulo: string; filas: (ItemSesion | { sku: string; nombre: string; ubicacionCodigo: string; key: string })[] }> = {
    faltan: { titulo: 'Faltan', filas: resumen.faltan },
    contados: { titulo: 'Contados', filas: resumen.contados },
    reconteo: { titulo: 'Reconteo', filas: resumen.reconteo },
    fuera: {
      titulo: 'Fuera de lugar',
      filas: resumen.fueraDeLugar.map((h, i) => ({ sku: h.sku, nombre: h.nombre, ubicacionCodigo: `${h.cantidad} u. · ${h.usuario}`, key: `h${i}` })),
    },
  };
  const color = aviso
    ? aviso.resultado === 'SinConexion' ? COLOR_TONO.error : COLOR_TONO[estiloResultado(aviso.resultado).tono]
    : undefined;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.85rem', maxWidth: 720 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <button className="btn btn-ghost" style={boton} aria-label="Volver"
          disabled={enviando || reintentoPendiente}
          onClick={() => navigate('/conteos/escaner')}>
          <ArrowLeft size={18} />
        </button>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontWeight: 700, fontSize: '1.1rem' }}>{s.numero} · {s.pasillo ?? 'Planificados'}</div>
          <div style={{ color: 'var(--text-secondary)' }}>
            {s.contados} de {s.esperados} contados {s.estado !== 'Abierta' && <strong>· {s.estado}</strong>}
          </div>
        </div>
      </div>

      {abierta && (
        <>
          {!pendiente && (
            <div style={{ display: 'flex', gap: 8 }}>
              <input
                ref={escaneoRef}
                autoFocus
                aria-label="Código de barras"
                placeholder="Escanea o escribe el código"
                inputMode="numeric"
                style={{ ...control, flex: 1 }}
                value={escaneo}
                onChange={e => setEscaneo(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); recibirCodigo(escaneo); } }}
              />
              <button className="btn btn-secondary" style={{ ...boton, minWidth: 52 }} aria-label="Escanear con cámara" onClick={() => setCamara(true)}>
                <Camera size={20} />
              </button>
            </div>
          )}

          {pendiente && (
            <div style={{ border: '2px solid var(--accent)', borderRadius: 12, padding: '0.85rem', display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div>
                <div style={{ fontSize: '1.2rem', fontWeight: 700 }}>{pendiente.nombre ?? 'Buscando producto…'}</div>
                <div style={{ color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)' }}>{pendiente.sku ?? ''} · {pendiente.codigo}</div>
                {pendiente.esAlias && (
                  <div role="note" style={{ color: 'var(--text-primary)', marginTop: 6, fontSize: '0.85rem' }}>
                    Etiqueta histórica reconocida. EAN vigente: <strong>{pendiente.codigoVigente}</strong>.
                    La lectura conservará el código físico original; no se modifica el SKU ni el inventario automáticamente.
                  </div>
                )}
              </div>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontWeight: 600 }}>
                ¿Cuántas unidades hay?
                <input
                  ref={cantidadRef}
                  type="number"
                  min={0}
                  inputMode="numeric"
                  style={control}
                  value={cantidad}
                  disabled={!pendiente.verificado || enviando || reintentoPendiente}
                  max={2147483647}
                  onChange={e => setCantidad(e.target.value)}
                  onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); if (!opciones) guardar(); } }}
                />
              </label>
              {opciones ? (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  <div style={{ fontWeight: 600 }}>¿Qué posición estás contando?</div>
                  {opciones.map(o => (
                    <button key={o.stockFisicoId} className="btn btn-primary" style={boton}
                      disabled={enviando || !pendiente.verificado ||
                        (reintentoPendiente && lecturaEnviadaRef.current?.stockFisicoId !== o.stockFisicoId)}
                      onClick={() => guardar(o.stockFisicoId)}>
                      {reintentoPendiente && lecturaEnviadaRef.current?.stockFisicoId === o.stockFisicoId
                        ? `Reintentar ${o.ubicacionCodigo}` : o.ubicacionCodigo}
                    </button>
                  ))}
                </div>
              ) : (
                <button className="btn btn-primary" style={boton} disabled={enviando || !pendiente.verificado} onClick={() => guardar()}>
                  {enviando ? 'Guardando…' : aviso?.resultado === 'SinConexion' ? 'Reintentar guardar (Enter)' : 'Guardar (Enter)'}
                </button>
              )}
              {reintentoPendiente && (
                <div role="alert" style={{ color: 'var(--red)', fontSize: '0.85rem' }}>
                  Resultado incierto: cantidad y ubicación bloqueadas. Reintenta exactamente la misma lectura
                  antes de iniciar otro conteo para evitar duplicaciones.
                </div>
              )}
              <button className="btn btn-ghost" style={boton}
                disabled={enviando || reintentoPendiente} onClick={volverAEscanear}>Descartar</button>
            </div>
          )}
        </>
      )}

      {aviso && (
        <div role="status" style={{ borderLeft: `6px solid ${color}`, background: 'var(--bg-card)', borderRadius: 8, padding: '0.7rem 0.9rem' }}>
          <div style={{ fontWeight: 700, color }}>{aviso.mensaje}</div>
          {aviso.detalle && <div style={{ color: 'var(--text-secondary)', fontSize: '0.9rem' }}>{aviso.detalle}</div>}
        </div>
      )}

      <div role="tablist" style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 6 }}>
        {(Object.keys(listas) as Pestana[]).map(k => (
          <button key={k} role="tab" aria-selected={pestana === k} style={{ ...boton, fontSize: '0.8rem', padding: '0 4px' }}
            className={pestana === k ? 'btn btn-primary' : 'btn btn-secondary'} onClick={() => setPestana(k)}>
            {listas[k].titulo} ({listas[k].filas.length})
          </button>
        ))}
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        {listas[pestana].filas.length === 0 && <div style={{ color: 'var(--text-muted)' }}>Nada aquí.</div>}
        {listas[pestana].filas.map(f => (
          <div key={'stockFisicoId' in f ? f.stockFisicoId : f.key} style={{ border: '1px solid var(--border)', borderRadius: 8, padding: '0.5rem 0.7rem' }}>
            <div style={{ fontWeight: 600 }}>{f.nombre}</div>
            <div style={{ fontSize: '0.8rem', color: 'var(--text-muted)' }}>{f.sku} · {f.ubicacionCodigo}</div>
          </div>
        ))}
      </div>

      {abierta && (
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-primary" style={{ ...boton, flex: 1 }}
            disabled={enviando || reintentoPendiente} onClick={() => setCierre(true)}>
            {s.modo === 'Recorrido' ? 'Cerrar pasillo' : 'Cerrar sesión'}
          </button>
          <button className="btn btn-ghost" style={boton}
            disabled={enviando || reintentoPendiente} onClick={cancelarSesion}>Cancelar sesión</button>
        </div>
      )}

      <EscanerCamaraModal
        abierto={camara}
        onCerrar={() => setCamara(false)}
        onCodigo={codigo => { setCamara(false); recibirCodigo(codigo); }}
      />
      {cierre && (
        <CierreSesionModal faltan={resumen.faltan} enviando={enviando} onCancelar={() => setCierre(false)} onConfirmar={cerrar} />
      )}
    </div>
  );
}
