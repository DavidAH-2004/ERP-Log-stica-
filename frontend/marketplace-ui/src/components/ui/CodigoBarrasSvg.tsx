import { useEffect, useRef } from 'react';
import JsBarcode from 'jsbarcode';
import { esEan13Valido } from '../../utils/ean13';

interface CodigoBarrasSvgProps {
  codigo: string;
  /** Alto de las barras en unidades del SVG (1 módulo = 2 unidades). */
  alto?: number;
  /**
   * Si es true, el SVG se escala al tamaño que le dé el CSS (width/height), manteniendo la proporción.
   * Se usa en las etiquetas para fijar el ancho físico en milímetros.
   */
  escalable?: boolean;
  className?: string;
  style?: React.CSSProperties;
}

// Ancho de un módulo (la barra más delgada) en unidades del SVG.
const ANCHO_MODULO = 2;
// Zonas en blanco obligatorias del EAN-13: 11 módulos a la izquierda y 7 a la derecha.
const ZONA_IZQUIERDA = 11 * ANCHO_MODULO;
const ZONA_DERECHA = 7 * ANCHO_MODULO;

/** Dibuja un código EAN-13 en SVG. Si el código falta o es inválido muestra "Sin código". */
export default function CodigoBarrasSvg({ codigo, alto = 50, escalable = false, className, style }: CodigoBarrasSvgProps) {
  const svgRef = useRef<SVGSVGElement>(null);
  const valido = esEan13Valido(codigo);

  useEffect(() => {
    const svg = svgRef.current;
    if (!valido || !svg) return;

    JsBarcode(svg, codigo, {
      format: 'EAN13',
      displayValue: true,
      height: alto,
      width: ANCHO_MODULO,
      margin: 0,
      marginLeft: ZONA_IZQUIERDA,
      marginRight: ZONA_DERECHA,
      fontSize: 16,
      textMargin: 2,
      background: '#ffffff',
      lineColor: '#000000',
    });

    if (escalable) {
      const ancho = parseFloat(svg.getAttribute('width') ?? '0');
      const altoSvg = parseFloat(svg.getAttribute('height') ?? '0');
      svg.setAttribute('viewBox', `0 0 ${ancho} ${altoSvg}`);
      svg.removeAttribute('width');
      svg.removeAttribute('height');
    }
  }, [codigo, alto, valido, escalable]);

  if (!valido) {
    return (
      <span className={className} style={{ color: 'var(--text-muted)', fontSize: '0.85rem', ...style }}>
        Sin código
      </span>
    );
  }

  return <svg ref={svgRef} className={className} style={style} role="img" aria-label={`Código de barras ${codigo}`} />;
}
