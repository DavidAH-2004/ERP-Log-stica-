import { apiClient } from './client';
import type { ResultadoLectura } from '../utils/resultadoLectura';

export type ModoSesionConteo = 'Recorrido' | 'Planificado';
export type EstadoSesionConteo = 'Abierta' | 'Cerrada' | 'Cancelada';
export type AccionCierre = 'NoHay' | 'Pendiente';

export interface OpcionUbicacion { stockFisicoId: string; ubicacionCodigo: string; }

/** Conteo a ciegas: nunca trae cantidades del sistema. */
export interface ResultadoLecturaDto {
  resultado: ResultadoLectura;
  mensaje: string;
  sku: string | null;
  nombre: string | null;
  ubicacionCodigo: string | null;
  opciones: OpcionUbicacion[] | null;
}

export interface SesionConteo {
  id: string;
  numero: string;
  modo: ModoSesionConteo;
  pasillo: string | null;
  estado: EstadoSesionConteo;
  creadaPor: string;
  fechaInicio: string;
  fechaCierre: string | null;
  esperados: number;
  contados: number;
  hallazgos: number;
}

export interface ItemSesion { stockFisicoId: string; sku: string; nombre: string; ubicacionCodigo: string; }
export interface Hallazgo { sku: string; nombre: string; pasillo: string; cantidad: number; usuario: string; fecha: string; }

export interface ResumenSesion {
  sesion: SesionConteo;
  faltan: ItemSesion[];
  contados: ItemSesion[];
  reconteo: ItemSesion[];
  fueraDeLugar: Hallazgo[];
}

export interface DecisionCierre { stockFisicoId: string; accion: AccionCierre; }

export interface LecturaInput {
  lecturaId: string;
  codigo: string;
  cantidad: number;
  stockFisicoId?: string;
}

const BASE = '/api/inventario/conteos-ciclicos/sesiones';

export const sesionesConteoApi = {
  listar: (estado?: EstadoSesionConteo) =>
    apiClient.get<SesionConteo[]>(estado ? `${BASE}?estado=${estado}` : BASE),
  pasillos: () => apiClient.get<string[]>(`${BASE}/pasillos`),
  abrirRecorrido: (pasillo: string) => apiClient.post<SesionConteo>(`${BASE}/recorrido`, { pasillo }),
  abrirPlanificada: (pasillo?: string) => apiClient.post<SesionConteo>(`${BASE}/planificada`, { pasillo: pasillo || null }),
  resumen: (id: string) => apiClient.get<ResumenSesion>(`${BASE}/${id}`),
  leer: (id: string, lectura: LecturaInput) => apiClient.post<ResultadoLecturaDto>(`${BASE}/${id}/lecturas`, lectura),
  cerrar: (id: string, decisiones: DecisionCierre[]) => apiClient.post<ResumenSesion>(`${BASE}/${id}/cerrar`, { decisiones }),
  cancelar: (id: string) => apiClient.post<SesionConteo>(`${BASE}/${id}/cancelar`),
};
