import { apiClient, getAuthenticatedBlob } from './client';

export interface ArchivoCompraResumen {
  id: string;
  ordenCompraId: string;
  numeroPO: string;
  proveedorId: string;
  proveedorNombre: string;
  fechaCreacionUtc: string;
  fechaActualizacionUtc: string;
  cantidadItems: number;
  montoTotal: number;
  estado: string;
  versionPdf: number;
}

export interface ArchivosCompraResponse {
  total: number;
  pagina: number;
  tamano: number;
  archivos: ArchivoCompraResumen[];
}

export interface FiltrosArchivoCompra {
  numeroPO?: string;
  proveedor?: string;
  desde?: string;
  hasta?: string;
  pagina?: number;
}

export const comprasArchivosApi = {
  listar: (f: FiltrosArchivoCompra) => {
    const query = new URLSearchParams();
    if (f.numeroPO?.trim()) query.set('numeroPO', f.numeroPO.trim());
    if (f.proveedor?.trim()) query.set('proveedor', f.proveedor.trim());
    if (f.desde) query.set('desde', f.desde);
    if (f.hasta) query.set('hasta', f.hasta);
    query.set('pagina', String(f.pagina ?? 1));
    query.set('tamano', '25');
    return apiClient.get<ArchivosCompraResponse>('/api/compras/archivos?' + query);
  },
  pdf: (poId: string) => getAuthenticatedBlob('/api/compras/archivos/' + encodeURIComponent(poId) + '/pdf'),
  conciliar: () => apiClient.post<{ archivadas: number }>('/api/compras/archivos/reconciliar?limite=50'),
};
