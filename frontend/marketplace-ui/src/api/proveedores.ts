/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';

export interface ProveedorLogistico {
  id: string;
  nombreComercial: string;
  razonSocial: string;
  categoriaSuministro: string;
  contactoPrincipal: string;
  email: string;
  telefono: string;
  direccion: string;
  plazoCreditoDias: number;
  otifHistorico: number | null;
  productosCatalogo: number;
  productosBajoMinimo: number;
  ordenesCompraAbiertas: number;
  unidadesPendientesRecepcion: number;
}

export interface ProveedoresLogisticaResponse {
  total: number;
  conProductosCatalogo: number;
  conCompraAbierta: number;
  nombresCatalogoNoVinculados: string[];
  items: ProveedorLogistico[];
}

export interface RegistrarProveedorRequest {
  nombreComercial: string;
  razonSocial: string;
  nit: string;
  nrc: string;
  categoriaSuministro: string;
  contactoPrincipal: string;
  email: string;
  telefono: string;
  direccion: string;
  condicionesPago: 'Contado' | 'Credito';
  diasCredito: number;
  activo: boolean;
}

export interface ProveedorCreado {
  id: string;
  nombreComercial: string;
  razonSocial: string;
  plazoCreditoDias: number;
  activo: boolean;
  otifHistorico: number | null;
}

export const proveedoresApi = {
  crear: (proveedor: RegistrarProveedorRequest) =>
    apiClient.post<ProveedorCreado>('/api/proveedores', proveedor),
  listar: (buscar?: string) => {
    const q = buscar?.trim() ? `?buscar=${encodeURIComponent(buscar.trim())}` : '';
    return apiClient.get<ProveedoresLogisticaResponse>(`/api/proveedores/logistica${q}`);
  },
};
