/** Resultado de escanear un producto en una sesión de conteo (espejo de ResultadoLectura del backend). */
export type ResultadoLectura =
  | 'Coincide'
  | 'Diferencia'
  | 'AjusteCreado'
  | 'FueraDeLugar'
  | 'ElegirUbicacion'
  | 'BloqueadoPorAjuste'
  | 'YaContado'
  | 'StockCambio'
  | 'CodigoInvalido'
  | 'ProductoNoEncontrado'
  | 'SesionNoAbierta'
  | 'Rechazado';

export type TonoResultado = 'exito' | 'aviso' | 'info' | 'neutro' | 'error';

/** Cómo mostrar cada resultado: color del aviso y si el celular debe vibrar. */
export function estiloResultado(r: ResultadoLectura): { tono: TonoResultado; vibrar: boolean } {
  switch (r) {
    case 'Coincide':
    case 'YaContado':
      return { tono: 'exito', vibrar: false };
    case 'Diferencia':
    case 'StockCambio':
    case 'ElegirUbicacion':
      return { tono: 'aviso', vibrar: true };
    case 'AjusteCreado':
      return { tono: 'info', vibrar: false };
    case 'FueraDeLugar':
      return { tono: 'neutro', vibrar: false };
    default:
      return { tono: 'error', vibrar: true };
  }
}

/** Variable CSS del color de cada tono (variables existentes del tema). */
export const COLOR_TONO: Record<TonoResultado, string> = {
  exito: 'var(--green)',
  aviso: 'var(--yellow)',
  info: 'var(--accent)',
  neutro: 'var(--text-muted)',
  error: 'var(--red)',
};
