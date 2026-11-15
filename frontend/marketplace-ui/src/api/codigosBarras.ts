import { apiClient } from './client';

/** Datos mínimos imprimibles; nunca incluyen stock físico ni información financiera. */
export interface EtiquetaProducto {
  id: string;
  sku: string;
  nombre: string;
  codigoEAN: string;
}

export interface ProductoPorCodigo extends EtiquetaProducto {}

export const codigosBarrasApi = {
  etiquetas: (ids?: string[]) => {
    if (ids && (ids.length < 1 || ids.length > 100))
      throw new Error('Selecciona entre 1 y 100 productos para imprimir.');
    const query = ids
      ? '?ids=' + ids.map(encodeURIComponent).join(',')
      : '';
    return apiClient.get<EtiquetaProducto[]>('/api/productos/etiquetas' + query);
  },
  buscarPorCodigo: (codigo: string) =>
    apiClient.get<ProductoPorCodigo>('/api/productos/por-codigo/' + encodeURIComponent(codigo)),
};
