/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { ClasificacionRMA, DevolucionRMA } from '../types';
import type { PaginaOrdenesSeleccion } from './fulfillment';

export interface RegistrarRmaRequest {
  ordenId: string;
  productoId: string;
  cantidad: number;
  motivoCliente: string;
}

export interface DictaminarRmaRequest {
  clasificacion: ClasificacionRMA;
  inspector: string;
  dictamenTecnico: string;
  descuentoRemate?: number;
  ubicacionDestinoId?: string;
  operacionId?: string;
}

export interface ProductoDevolvible {
  productoId: string;
  sku: string;
  nombre: string;
  cantidadVendida: number;
  cantidadDisponible: number;
}

export const rmaApi = {
  ordenesElegibles: (buscar = '', pagina = 1, tamano = 25) => {
    const q = new URLSearchParams({ buscar, pagina: String(pagina), tamano: String(tamano) });
    return apiClient.get<PaginaOrdenesSeleccion>(`/api/rma/ordenes-elegibles?${q}`);
  },
  productosDevolvibles: (ordenId: string) =>
    apiClient.get<{ ordenId: string; items: ProductoDevolvible[] }>(`/api/rma/ordenes/${encodeURIComponent(ordenId)}/productos-devolvibles`),
  listar: (estado?: string) =>
    apiClient.get<DevolucionRMA[]>(`/api/rma${estado ? `?estado=${encodeURIComponent(estado)}` : ''}`),

  solicitar: (req: RegistrarRmaRequest) =>
    apiClient.post<DevolucionRMA>('/api/rma', req),

  inspeccionar: (id: string, req: DictaminarRmaRequest) =>
    apiClient.post<DevolucionRMA>(`/api/rma/${id}/inspeccionar`, req),
};
