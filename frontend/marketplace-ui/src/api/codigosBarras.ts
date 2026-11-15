import { apiClient } from './client';

/** Datos mínimos imprimibles; nunca incluyen stock físico ni información financiera. */
export interface EtiquetaProducto {
  id: string;
  sku: string;
  nombre: string;
  codigoEAN: string;
}

export interface ProductoPorCodigo extends EtiquetaProducto {
  /** The exact physical EAN scanned, not the product's current EAN. */
  codigoLeido?: string;
  esAlias?: boolean;
}

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
  // F403: explícito, Admin-only en backend, jamás sustituye un EAN no vacío.
  asignarSiFalta: (id: string) =>
    apiClient.post<{ codigoEAN: string }>('/api/productos/' +
      encodeURIComponent(id) + '/codigo-barras/asignar'),
};


/** F476: Admin-only, explicitly feature-gated two-person EAN rotation. */
export interface RotacionEanEvento {
  solicitudId: string;
  version: number;
  estado: string;
  codigoAnterior: string;
  codigoNuevo: string;
  solicitadoUtc: string;
  aprobadoUtc: string | null;
  ejecutadoUtc: string | null;
}

export const rotacionesEanApi = {
  historial: (productoId: string) =>
    apiClient.get<RotacionEanEvento[]>(
      '/api/productos/' + encodeURIComponent(productoId) + '/codigo-barras/rotaciones'),
  solicitar: (productoId: string, solicitudId: string, codigoNuevo: string, motivo: string) =>
    apiClient.post<{ solicitudId: string; productoId: string; estado: string }>(
      '/api/productos/' + encodeURIComponent(productoId) + '/codigo-barras/rotaciones',
      { solicitudId, codigoNuevo, motivo }),
  aprobar: (solicitudId: string) =>
    apiClient.post<{ solicitudId: string; estado: string }>(
      '/api/productos/codigo-barras/rotaciones/' + encodeURIComponent(solicitudId) + '/aprobar'),
};
