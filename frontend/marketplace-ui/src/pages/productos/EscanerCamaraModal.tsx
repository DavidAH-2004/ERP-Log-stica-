import { useEffect, useRef, useState } from 'react';
import { BrowserMultiFormatReader, type IScannerControls } from '@zxing/browser';
import { BarcodeFormat, DecodeHintType } from '@zxing/library';
import Modal from '../../components/ui/Modal';
import { esEan13Valido, normalizarLectura } from '../../utils/ean13';

interface EscanerCamaraModalProps {
  abierto: boolean;
  onCerrar: () => void;
  /** Recibe el primer EAN-13 válido leído; la cámara ya está detenida. */
  onCodigo: (codigo: string) => void;
}

function mensajeDeError(error: unknown): string {
  const nombre = error instanceof DOMException || error instanceof Error ? error.name : '';
  if (nombre === 'NotAllowedError' || nombre === 'SecurityError') return 'Permiso de cámara denegado.';
  if (nombre === 'NotFoundError' || nombre === 'OverconstrainedError') return 'No se encontró una cámara.';
  if (nombre === 'NotReadableError') return 'La cámara está siendo usada por otra aplicación.';
  return 'No se pudo iniciar la cámara.';
}

/** Lectura de códigos EAN-13 con la cámara del dispositivo (solo identifica; no mueve inventario). */
export default function EscanerCamaraModal({ abierto, onCerrar, onCodigo }: EscanerCamaraModalProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [error, setError] = useState('');

  // Referencias estables para no reiniciar la cámara si el padre vuelve a renderizar.
  const onCodigoRef = useRef(onCodigo);
  onCodigoRef.current = onCodigo;

  useEffect(() => {
    if (!abierto) return;
    setError('');

    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      setError('La cámara solo funciona en localhost o HTTPS.');
      return;
    }

    const hints = new Map<DecodeHintType, unknown>([[DecodeHintType.POSSIBLE_FORMATS, [BarcodeFormat.EAN_13]]]);
    const lector = new BrowserMultiFormatReader(hints);
    let controles: IScannerControls | null = null;
    let terminado = false;

    const detener = () => {
      terminado = true;
      controles?.stop();
    };

    lector
      .decodeFromConstraints(
        { video: { facingMode: { ideal: 'environment' } } }, // cámara trasera en celulares
        videoRef.current ?? undefined,
        (resultado, _err, ctrl) => {
          if (terminado || !resultado) return;
          const codigo = normalizarLectura(resultado.getText());
          if (!esEan13Valido(codigo)) return; // lectura parcial: seguir intentando
          detener();
          ctrl.stop();
          onCodigoRef.current(codigo);
        },
      )
      .then(ctrl => {
        controles = ctrl;
        if (terminado) ctrl.stop(); // se cerró antes de que la cámara terminara de abrir
      })
      .catch(e => {
        if (!terminado) setError(mensajeDeError(e));
      });

    return detener;
  }, [abierto]);

  if (!abierto) return null;

  return (
    <Modal title="Escanear con cámara" isOpen={true} onClose={onCerrar}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
        {error ? (
          <div style={{ color: 'var(--red)', fontSize: '0.9rem' }}>{error}</div>
        ) : (
          <>
            <video
              ref={videoRef}
              muted
              playsInline
              style={{ width: '100%', maxHeight: '60vh', borderRadius: '8px', background: '#000' }}
            />
            <div style={{ color: 'var(--text-secondary)', fontSize: '0.85rem' }}>
              Apunta la cámara al código de barras. Se abrirá el producto al leerlo.
            </div>
          </>
        )}
        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <button type="button" className="btn btn-secondary" onClick={onCerrar}>Cerrar</button>
        </div>
      </div>
    </Modal>
  );
}
