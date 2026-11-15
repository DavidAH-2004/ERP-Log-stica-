/**
 * Validación de códigos de barras EAN-13 en el cliente.
 * El backend es la autoridad; esto solo evita enviar lecturas evidentemente erróneas.
 */

/** Indica si el texto es un EAN-13 válido (13 dígitos y dígito verificador correcto). No recorta. */
export function esEan13Valido(codigo: string): boolean {
  if (!/^[0-9]{13}$/.test(codigo)) return false;

  let suma = 0;
  for (let i = 0; i < 12; i++) {
    const digito = codigo.charCodeAt(i) - 48;
    suma += i % 2 === 0 ? digito : digito * 3;
  }
  return (10 - (suma % 10)) % 10 === codigo.charCodeAt(12) - 48;
}

/** Limpia una lectura de pistola o cámara: quita espacios, tabulaciones y saltos de línea. */
export function normalizarLectura(texto: string): string {
  return texto.replace(/\s+/g, '');
}
