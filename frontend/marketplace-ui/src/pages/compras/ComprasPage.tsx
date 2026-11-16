/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { comprasArchivosApi } from '../../api/comprasArchivos';
import React, { useEffect, useState, useCallback, useRef } from 'react';
import { ShoppingCart, FileText, CheckCircle2, AlertTriangle, Plus, RefreshCw, Send, ShieldCheck, PackageCheck } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { useToast } from '../../context/ToastContext';
import { comprasApi, type CrearPORequest, type RegistrarFacturaRequest, type Validar3WayMatchRequest, type RecibirMercaderiaRequest } from '../../api/compras';
import { wmsApi } from '../../api/wms';
import { proveedoresApi, type ProveedorLogistico } from '../../api/proveedores';
import ItemsPOEditor, { itemPOValido, totalesPO } from './ItemsPOEditor';
import type { OrdenCompra, FacturaProveedor, MatchTridireccional, UbicacionAlmacen } from '../../types';

type RecepcionLinea = {
  itemOrdenCompraId: string;
  sku: string;
  descripcion: string;
  cantidadSolicitada: number;
  cantidadRecibidaBase: number;
  cantidadRecibir: number;
  ubicacionAlmacenId: string;
  operacionId: string;
};

export default function ComprasPage() {
  const [activeTab, setActiveTab] = useState<'po' | 'facturas' | 'match'>('po');
  const [ordenesCompra, setOrdenesCompra] = useState<OrdenCompra[]>([]);
  const [matches, setMatches] = useState<MatchTridireccional[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorCarga, setErrorCarga] = useState('');

  // Crear PO Modal
  const [poModalOpen, setPoModalOpen] = useState(false);
  const [poForm, setPoForm] = useState<CrearPORequest>({
    proveedorId: '',
    proveedorNombre: '',
    terminosPago: 'Crédito 30 días',
    observaciones: '',
    fechaEntregaEsperada: new Date(Date.now() + 15 * 86400000).toISOString().split('T')[0],
    items: [],
  });
  const [savingPo, setSavingPo] = useState(false);
  const [itemPendiente, setItemPendiente] = useState(false);
  const creandoPo = useRef(false);
  const [proveedores, setProveedores] = useState<ProveedorLogistico[]>([]);
  const [loadingProveedores, setLoadingProveedores] = useState(true);

  // Registrar Factura Modal
  const [facturaModalOpen, setFacturaModalOpen] = useState(false);
  const [facturaForm, setFacturaForm] = useState<RegistrarFacturaRequest>({
    ordenCompraId: '',
    numeroFactura: '',
    subTotal: 0,
    montoTotal: 0,
    fechaEmision: new Date().toISOString().split('T')[0],
  });
  const [savingFactura, setSavingFactura] = useState(false);
  const [facturaPO, setFacturaPO] = useState<OrdenCompra | null>(null);

  const actualizarLineaFactura = (index: number, campo: 'cantidadFacturada' | 'precioUnitarioFacturado', valor: number) => {
    setFacturaForm(f => {
      const lineas = (f.lineas ?? []).map((linea, i) => i === index ? { ...linea, [campo]: valor } : linea);
      const totales = totalesPO(lineas.map(l => ({ sku: '', nombreProducto: '', cantidad: l.cantidadFacturada, precioUnitario: l.precioUnitarioFacturado })));
      return { ...f, lineas, subTotal: totales.subtotal, montoTotal: totales.total };
    });
  };

  // 3-Way Match Modal / State
  const [matchModalOpen, setMatchModalOpen] = useState(false);
  const [matchForm, setMatchForm] = useState<Validar3WayMatchRequest>({
    ordenCompraId: '',
    facturaProveedorId: '',
    cantidadRecibidaBodega: 0,
    toleranciaPorcentaje: 2.0,
  });
  const [facturasMatch, setFacturasMatch] = useState<FacturaProveedor[]>([]);
  const [loadingMatchPO, setLoadingMatchPO] = useState(false);
  const matchPoRequestId = useRef(0);

  useEffect(() => () => {
    matchPoRequestId.current += 1;
  }, []);
  const [matchResult, setMatchResult] = useState<MatchTridireccional | null>(null);
  const [runningMatch, setRunningMatch] = useState(false);

  // Recepción Inbound / WMS
  const [recepcionModalOpen, setRecepcionModalOpen] = useState(false);
  const [recepcionPO, setRecepcionPO] = useState<OrdenCompra | null>(null);
  const [recepcionItems, setRecepcionItems] = useState<RecepcionLinea[]>([]);
  const [ubicacionesWms, setUbicacionesWms] = useState<UbicacionAlmacen[]>([]);
  const [recepcionNotas, setRecepcionNotas] = useState('');
  const [loadingRecepcion, setLoadingRecepcion] = useState(false);
  const [savingRecepcion, setSavingRecepcion] = useState(false);

  const loadData = useCallback(async () => {
    setLoading(true);
    setErrorCarga('');
    try {
      const [poRes, matchRes] = await Promise.all([
        comprasApi.listarPO({ tamano: 50 }),
        comprasApi.historialMatch(),
      ]);
      setOrdenesCompra(poRes.ordenesCompra || []);
      setMatches(matchRes || []);
    } catch (error) {
      setOrdenesCompra([]);
      setMatches([]);
      setErrorCarga(error instanceof Error ? error.message : 'No se pudieron cargar Compras y 3-Way Match desde la API.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  useEffect(() => {
    let activo = true;
    setLoadingProveedores(true);
    proveedoresApi.listar()
      .then(res => { if (activo) setProveedores(res.items ?? []); })
      .catch((error) => { if (activo) { setProveedores([]); setErrorCarga(error instanceof Error ? error.message : 'No se pudo cargar el maestro de proveedores.'); } })
      .finally(() => { if (activo) setLoadingProveedores(false); });
    return () => { activo = false; };
  }, []);

  const toast = useToast();

  const cargarPOParaMatch = async (ordenCompraId: string) => {
    const requestId = ++matchPoRequestId.current;

    if (!ordenCompraId) {
      setLoadingMatchPO(false);
      setFacturasMatch([]);
      setMatchForm(f => ({
        ...f,
        ordenCompraId: '',
        facturaProveedorId: '',
        cantidadRecibidaBodega: 0,
      }));
      return;
    }

    setLoadingMatchPO(true);
    setMatchResult(null);
    try {
      // El listado de PO no incluye facturas; el detalle sí. Cargarlo evita
      // enviar facturaProveedorId vacío o asociar una factura de otra PO.
      const po = await comprasApi.obtenerPO(ordenCompraId);
      if (requestId !== matchPoRequestId.current) return;

      const facturas = po.facturas ?? [];
      const facturaDisponible = facturas.find(f => !f.aprobadaParaPago && !f.pagada);
      const cantidadRecibida = po.items.reduce(
        (total, item) => total + (item.cantidadRecibida ?? 0),
        0
      );

      setFacturasMatch(facturas);
      setMatchForm(f => ({
        ...f,
        ordenCompraId: po.id,
        facturaProveedorId: facturaDisponible?.id ?? '',
        cantidadRecibidaBodega: cantidadRecibida,
      }));
    } catch (error) {
      if (requestId !== matchPoRequestId.current) return;

      setFacturasMatch([]);
      setMatchForm(f => ({
        ...f,
        ordenCompraId,
        facturaProveedorId: '',
        cantidadRecibidaBodega: 0,
      }));
      toast.error(
        'No se pudo cargar la factura',
        error instanceof Error ? error.message : 'No fue posible cargar el detalle de la PO.'
      );
    } finally {
      if (requestId === matchPoRequestId.current) {
        setLoadingMatchPO(false);
      }
    }
  };
  const handleCrearPO = async () => {
    if (creandoPo.current) return;
    if (itemPendiente || poForm.items.length < 1 || poForm.items.length > 10 || !poForm.items.every(itemPOValido)) {
      toast.warning('Revisa los ítems', 'Agrega entre 1 y 10 productos válidos y guarda o cancela el ítem pendiente.');
      return;
    }
    if (!poForm.proveedorId) {
      toast.warning('Proveedor requerido', 'Selecciona un proveedor activo del maestro corporativo.');
      return;
    }

    creandoPo.current = true;
    setSavingPo(true);
    try {
      await comprasApi.crearPO(poForm);
      toast.success('Orden de Compra Generada', `PO creada para ${poForm.proveedorNombre}.`);
      setPoModalOpen(false);
      await loadData();
    } catch (error) {
      const mensaje = error instanceof Error ? error.message : 'No fue posible crear la Orden de Compra.';
      toast.error('Error al crear PO', mensaje);
    } finally {
      creandoPo.current = false;
      setSavingPo(false);
    }
  };

  const descargarArchivoPO = async (poId: string, numero: string) => {
    try {
      const pdf = await comprasArchivosApi.pdf(poId);
      const blobUrl = URL.createObjectURL(pdf);
      const anchor = document.createElement('a');
      anchor.href = blobUrl;
      anchor.download = numero + '.pdf';
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(blobUrl), 120_000);
    } catch (error) {
      toast.error('No se pudo descargar el PDF', error instanceof Error ? error.message : 'Archivo no disponible.');
    }
  };

  const handleEnviarPO = async (poId: string) => {
    try {
      await comprasApi.enviarPO(poId);
      toast.info('PO Transmitida', 'La orden de compra ha sido enviada al proveedor.');
      await loadData();
    } catch (error) {
      const mensaje = error instanceof Error ? error.message : 'No fue posible enviar la Orden de Compra.';
      toast.error('Error al enviar PO', mensaje);
    }
  };

  const abrirRecepcion = async (po: OrdenCompra) => {
    setLoadingRecepcion(true);
    try {
      const ubicaciones = await wmsApi.listarUbicaciones();
      setUbicacionesWms(ubicaciones);
      setRecepcionPO(po);
      setRecepcionNotas('');
      setRecepcionItems(
        po.items
          .filter(item => (item.cantidadRecibida ?? 0) < item.cantidad)
          .map(item => ({
            itemOrdenCompraId: item.id,
            sku: item.skuReferencia ?? '',
            descripcion: item.descripcion,
            cantidadSolicitada: item.cantidad,
            cantidadRecibidaBase: item.cantidadRecibida ?? 0,
            cantidadRecibir: 0,
            ubicacionAlmacenId: '',
            operacionId: crypto.randomUUID(),
          }))
      );
      setRecepcionModalOpen(true);
    } catch (error) {
      const mensaje = error instanceof Error ? error.message : 'No fue posible cargar las ubicaciones WMS.';
      toast.error('No se pudo abrir la recepción', mensaje);
    } finally {
      setLoadingRecepcion(false);
    }
  };

  const handleRecibirMercaderia = async () => {
    if (!recepcionPO) return;

    const seleccionados = recepcionItems.filter(item => item.cantidadRecibir > 0);
    if (seleccionados.length === 0) {
      toast.warning('Recepción incompleta', 'Ingresa al menos una cantidad mayor que cero.');
      return;
    }

    const sinUbicacion = seleccionados.find(item => !item.ubicacionAlmacenId);
    if (sinUbicacion) {
      toast.warning('Ubicación requerida', `Selecciona una ubicación WMS para ${sinUbicacion.sku || sinUbicacion.descripcion}.`);
      return;
    }

    const excedido = seleccionados.find(item =>
      item.cantidadRecibidaBase + item.cantidadRecibir > item.cantidadSolicitada
    );
    if (excedido) {
      toast.warning('Cantidad inválida', `La recepción de ${excedido.sku || excedido.descripcion} excede lo solicitado.`);
      return;
    }

    const request: RecibirMercaderiaRequest = {
      items: seleccionados.map(item => ({
        itemOrdenCompraId: item.itemOrdenCompraId,
        cantidadRecibida: item.cantidadRecibir,
        cantidadAcumuladaEsperada: item.cantidadRecibidaBase + item.cantidadRecibir,
        ubicacionAlmacenId: item.ubicacionAlmacenId,
        operacionId: item.operacionId,
      })),
      notas: recepcionNotas.trim() || undefined,
    };

    setSavingRecepcion(true);
    try {
      await comprasApi.recibirMercancia(recepcionPO.id, request);
      toast.success('Recepción registrada', 'El inventario físico y la Orden de Compra fueron actualizados.');
      setRecepcionModalOpen(false);
      setRecepcionPO(null);
      await loadData();
    } catch (error) {
      const mensaje = error instanceof Error ? error.message : 'No fue posible registrar la recepción.';
      toast.error('Error de recepción', mensaje);
    } finally {
      setSavingRecepcion(false);
    }
  };

  const handleRegistrarFactura = async () => {
    if (savingFactura) return;
    if (!facturaForm.lineas?.length || facturaForm.lineas.some(l => !itemPOValido({
      sku: l.itemOrdenCompraId, nombreProducto: 'Factura', cantidad: l.cantidadFacturada, precioUnitario: l.precioUnitarioFacturado,
    }))) {
      toast.warning('Detalle requerido', 'Captura la cantidad y el precio de cada producto según la factura del proveedor.');
      return;
    }
    setSavingFactura(true);
    try {
      await comprasApi.registrarFactura(facturaForm);
      toast.success('Factura Registrada', `Comprobante ${facturaForm.numeroFactura} cargado al sistema.`);
      setFacturaModalOpen(false);
      await loadData();
    } catch (error) {
      toast.error('Error al registrar factura', error instanceof Error ? error.message : 'No fue posible registrar la factura.');
    } finally {
      setSavingFactura(false);
    }
  };

  const handleEjecutarMatch = async () => {
    if (!matchForm.ordenCompraId || !matchForm.facturaProveedorId) {
      toast.warning('Factura requerida', 'Selecciona una factura pendiente asociada a la Orden de Compra.');
      return;
    }
    if (matchForm.cantidadRecibidaBodega <= 0) {
      toast.warning('Recepción requerida', 'La cantidad física recibida debe ser mayor que cero.');
      return;
    }

    setRunningMatch(true);
    try {
      const res = await comprasApi.ejecutarMatch(matchForm);
      setMatchResult(res);
      setMatches(prev => [res, ...prev.filter(m => m.id !== res.id)]);
      if (res.resultado === 'Conforme') {
        toast.success('3-Way Match Conforme', 'Cotejo exitoso: orden autorizada para pago.');
      } else {
        toast.warning('3-Way Match Discrepante', 'Existe diferencia fuera de tolerancia entre PO y recepción.');
      }
    } catch (error) {
      toast.error('Error en 3-Way Match', error instanceof Error ? error.message : 'No fue posible ejecutar el 3-Way Match.');
    } finally {
      setRunningMatch(false);
    }
  };

  const proveedorSeleccionado = proveedores.find(p => p.id === poForm.proveedorId);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Compras & 3-Way Match
          </h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.875rem', marginTop: '0.25rem' }}>
            Auditoría tridireccional automatizada: Orden de Compra (PO) vs Recepción Bodega vs Factura de Proveedor.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.75rem' }}>
          <button
            onClick={loadData}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem',
              padding: '0.5rem 0.85rem',
              borderRadius: '8px',
              border: '1px solid var(--border)',
              background: 'var(--bg-surface)',
              color: 'var(--text-primary)',
              cursor: 'pointer',
            }}
          >
            <RefreshCw size={15} />
            Refrescar
          </button>
          <button
            onClick={() => {
              setPoForm({
                proveedorId: '', proveedorNombre: '', terminosPago: 'Crédito 30 días',
                observaciones: '', fechaEntregaEsperada: new Date(Date.now() + 15 * 86400000).toISOString().split('T')[0],
                items: [],
              });
              setItemPendiente(false);
              setPoModalOpen(true);
            }}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem',
              padding: '0.5rem 1rem',
              borderRadius: '8px',
              border: 'none',
              background: 'var(--accent)',
              color: '#fff',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            <Plus size={16} />
            Nueva Orden de Compra (PO)
          </button>
        </div>
      </div>

      {errorCarga && (
        <AlertBanner variant="danger" title="Compras no disponible" message={errorCarga} action={{ label: 'Reintentar', onClick: loadData }} />
      )}

      {/* Attention List (Efferd Dashboard #6: Items requiring operational attention) */}
      {matches.some(m => m.resultado === 'Discrepante') && (
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '12px 16px',
          background: 'rgba(239, 68, 68, 0.08)',
          border: '1px solid rgba(239, 68, 68, 0.25)',
          borderRadius: 'var(--radius-lg)',
          gap: 12
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div style={{
              width: 32,
              height: 32,
              borderRadius: 'var(--radius-md)',
              background: 'var(--red-bg)',
              color: 'var(--red)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0
            }}>
              <AlertTriangle size={16} />
            </div>
            <div>
              <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-primary)' }}>
                {matches.filter(m => m.resultado === 'Discrepante').length} Discrepancia(s) en 3-Way Match Requieren Atención
              </div>
              <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
                Se detectaron diferencias entre factura y recepción física de bodega. Los pagos correspondientes han sido retenidos por control interno.
              </div>
            </div>
          </div>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => setActiveTab('match')}
            style={{ borderColor: 'rgba(239, 68, 68, 0.35)', color: 'var(--red)', flexShrink: 0 }}
          >
            Ver Auditorías Retenidas →
          </button>
        </div>
      )}

      {/* Tabs */}
      <div style={{ display: 'flex', gap: '0.5rem', borderBottom: '1px solid var(--border)', paddingBottom: '0.5rem' }}>
        <button
          onClick={() => setActiveTab('po')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'po' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'po' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'po' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <ShoppingCart size={16} />
          Órdenes de Compra ({ordenesCompra.length})
        </button>

        <button
          onClick={() => setActiveTab('match')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'match' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'match' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'match' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <ShieldCheck size={16} />
          Auditoría 3-Way Match ({matches.length})
        </button>
      </div>

      {/* Tab: POs */}
      {activeTab === 'po' && (
        <DataTable
          columns={[
            {
              header: 'N° Orden Compra',
              accessor: 'numeroPO',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.numeroPO}</span>,
            },
            {
              header: 'Proveedor',
              accessor: 'nombreProveedor',
              render: (row) => <span style={{ fontWeight: 600 }}>{row.nombreProveedor}</span>,
            },
            {
              header: 'Productos',
              accessor: 'items',
              render: (row) => <span>{row.items.length}</span>,
            },
            {
              header: 'Monto Total',
              accessor: 'montoTotal',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--green)' }}>
                  ${row.montoTotal.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
                </span>
              ),
            },
            {
              header: 'Condición de Pago',
              accessor: 'condicionPago',
            },
            {
              header: 'Fecha Emisión',
              accessor: 'fechaEmision',
              render: (row) => <span style={{ fontSize: '0.85rem' }}>{new Date(row.fechaEmision).toLocaleDateString('es-SV')}</span>,
            },
            {
              header: 'Estado',
              accessor: 'estado',
              render: (row) => <StatusBadge status={row.estado} />,
            },
            {
              header: 'Acciones',
              accessor: 'id',
              render: (row) => (
                <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
                  <button
                    onClick={() => void descargarArchivoPO(row.id, row.numeroPO)}
                    style={{ border: '1px solid var(--border)', borderRadius: 6, padding: '0.3rem 0.6rem', background: 'var(--bg-surface)', color: 'var(--text-primary)', cursor: 'pointer', fontSize: '0.75rem' }}
                  >
                    Descargar PDF
                  </button>
                  {row.estado === 'Borrador' && (
                    <button
                      onClick={() => handleEnviarPO(row.id)}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.3rem',
                        padding: '0.3rem 0.6rem',
                        borderRadius: '6px',
                        border: '1px solid var(--accent)',
                        background: 'var(--accent-glow)',
                        color: 'var(--accent)',
                        fontSize: '0.75rem',
                        fontWeight: 600,
                        cursor: 'pointer',
                      }}
                    >
                      <Send size={12} />
                      Emitir a Proveedor
                    </button>
                  )}
                  {(row.estado === 'Emitida' || row.estado === 'Recibida') && row.items.some(item => (item.cantidadRecibida ?? 0) < item.cantidad) && (
                    <button
                      onClick={() => abrirRecepcion(row)}
                      disabled={loadingRecepcion}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.3rem',
                        padding: '0.3rem 0.6rem',
                        borderRadius: '6px',
                        border: '1px solid var(--green)',
                        background: 'rgba(34, 197, 94, 0.08)',
                        color: 'var(--green)',
                        fontSize: '0.75rem',
                        fontWeight: 600,
                        cursor: loadingRecepcion ? 'not-allowed' : 'pointer',
                      }}
                    >
                      <PackageCheck size={12} />
                      Recibir mercadería
                    </button>
                  )}
                  <button
                    onClick={() => {
                      setFacturaPO(row);
                      setFacturaForm({ ordenCompraId: row.id, numeroFactura: '', subTotal: 0, montoTotal: 0,
                        fechaEmision: new Date().toISOString().split('T')[0],
                        lineas: row.items.map(item => ({ itemOrdenCompraId: item.id, cantidadFacturada: 0, precioUnitarioFacturado: 0 })),
                      });
                      setFacturaModalOpen(true);
                    }}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '0.3rem',
                      padding: '0.3rem 0.6rem',
                      borderRadius: '6px',
                      border: '1px solid var(--border)',
                      background: 'var(--bg-surface)',
                      color: 'var(--text-primary)',
                      fontSize: '0.75rem',
                      fontWeight: 500,
                      cursor: 'pointer',
                    }}
                  >
                    <FileText size={12} />
                    Asociar Factura
                  </button>
                </div>
              ),
            },
          ]}
          data={ordenesCompra}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay órdenes de compra registradas."
        />
      )}

      {/* Tab: 3-Way Match */}
      {activeTab === 'match' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
          {/* Action trigger banner */}
          <div
            style={{
              padding: '1.25rem',
              borderRadius: '12px',
              border: '1px solid var(--border)',
              background: 'var(--accent-gradient-subtle)',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
            }}
          >
            <div>
              <h3 style={{ margin: 0, fontSize: '1.1rem', fontWeight: 600, color: 'var(--text-primary)' }}>
                Ejecutar Validación 3-Way Match
              </h3>
              <p style={{ margin: '0.25rem 0 0 0', fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
                Verifica cantidades, precios unitarios y montos de facturas contra recepciones de bodega con tolerancia configurada (±2%).
              </p>
            </div>
            <button
              onClick={() => {
                setMatchResult(null);
                if (ordenesCompra.length > 0) {
                  void cargarPOParaMatch(ordenesCompra[0].id);
                } else {
                  setFacturasMatch([]);
                  setMatchForm(f => ({
                    ...f,
                    ordenCompraId: '',
                    facturaProveedorId: '',
                    cantidadRecibidaBodega: 0,
                  }));
                }
                setMatchModalOpen(true);
              }}
              disabled={ordenesCompra.length === 0}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '0.5rem',
                padding: '0.65rem 1.25rem',
                borderRadius: '8px',
                border: 'none',
                background: 'var(--accent)',
                color: '#fff',
                fontWeight: 600,
                cursor: 'pointer',
              }}
            >
              <ShieldCheck size={18} />
              Auditar Nueva Orden
            </button>
          </div>

          {/* Historial de Auditorías */}
          <DataTable
            columns={[
              {
                header: 'ID Auditoría',
                accessor: 'id',
                render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.8rem', color: 'var(--text-secondary)' }}>{row.id.slice(0, 8)}...</span>,
              },
              {
                header: 'Resultado 3-Way Match',
                accessor: 'resultado',
                render: (row) => (
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                    {row.resultado === 'Conforme' ? (
                      <CheckCircle2 size={16} style={{ color: 'var(--green)' }} />
                    ) : (
                      <AlertTriangle size={16} style={{ color: 'var(--red)' }} />
                    )}
                    <span style={{ fontWeight: 700, color: row.resultado === 'Conforme' ? 'var(--green)' : 'var(--red)' }}>
                      {row.resultado}
                    </span>
                  </div>
                ),
              },
              {
                header: 'Dif. Cantidad',
                accessor: 'diferenciaCantidad',
                render: (row) => <span>{row.diferenciaCantidad} unid.</span>,
              },
              {
                header: 'Dif. Monto',
                accessor: 'diferenciaMonto',
                render: (row) => (
                  <span style={{ fontFamily: 'var(--font-mono)' }}>
                    ${row.diferenciaMonto.toFixed(2)}
                  </span>
                ),
              },
              {
                header: 'Observaciones Técnicas',
                accessor: 'observaciones',
                render: (row) => <span style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>{row.observaciones}</span>,
              },
              {
                header: 'Fecha Auditoría',
                accessor: 'fechaAuditoria',
                render: (row) => <span style={{ fontSize: '0.8rem' }}>{new Date(row.fechaAuditoria).toLocaleString('es-SV')}</span>,
              },
            ]}
            data={matches}
            keyExtractor={(item) => item.id}
            loading={loading}
            emptyText="No hay historial de auditorías 3-Way Match."
          />
        </div>
      )}

      {/* Modal Crear PO */}
      {poModalOpen && (
        <Modal
          title="Crear Nueva Orden de Compra (PO)"
          size="lg"
          isOpen={true}
          onClose={() => { if (!creandoPo.current) setPoModalOpen(false); }}
        >
          <form
            onKeyDown={e => { if (e.key === 'Enter' && e.target instanceof HTMLInputElement) e.preventDefault(); }}
            onSubmit={(e) => {
              e.preventDefault();
              // Enter en los campos del ítem nunca debe crear una orden accidental.
              if ((e.nativeEvent as SubmitEvent).submitter?.getAttribute('data-create-po') === 'true') void handleCrearPO();
            }}
            style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}
          >
            <fieldset disabled={savingPo} style={{ border: 0, padding: 0, margin: 0, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                Proveedor del maestro corporativo
              </label>
              <select
                value={poForm.proveedorId}
                onChange={e => {
                  const seleccionado = proveedores.find(p => p.id === e.target.value);
                  setPoForm(f => ({
                    ...f,
                    proveedorId: seleccionado?.id ?? '',
                    proveedorNombre: seleccionado?.razonSocial || seleccionado?.nombreComercial || '',
                    terminosPago: seleccionado
                      ? (seleccionado.plazoCreditoDias > 0 ? `Crédito ${seleccionado.plazoCreditoDias} días` : 'Contado')
                      : f.terminosPago,
                  }));
                }}
                required
                disabled={loadingProveedores}
                style={{
                  width: '100%',
                  padding: '0.5rem 0.75rem',
                  borderRadius: '6px',
                  background: 'var(--bg-surface)',
                  border: '1px solid var(--border)',
                  color: 'var(--text-primary)',
                  boxSizing: 'border-box',
                }}
              >
                <option value="">{loadingProveedores ? 'Cargando proveedores…' : 'Seleccionar proveedor…'}</option>
                {proveedores.map(proveedor => (
                  <option key={proveedor.id} value={proveedor.id}>
                    {proveedor.nombreComercial} — OTIF {proveedor.otifHistorico.toFixed(1)}%
                  </option>
                ))}
              </select>
              {proveedorSeleccionado && (
                <div style={{ marginTop: 6, padding: '0.55rem 0.65rem', borderRadius: 6, background: 'var(--bg-surface)', border: '1px solid var(--border)', fontSize: '0.75rem', color: 'var(--text-secondary)' }}>
                  <strong style={{ color: 'var(--text-primary)' }}>{proveedorSeleccionado.razonSocial}</strong><br />
                  OTIF {proveedorSeleccionado.otifHistorico.toFixed(1)}% · {proveedorSeleccionado.plazoCreditoDias > 0 ? `${proveedorSeleccionado.plazoCreditoDias} días de crédito` : 'Contado'}
                  {proveedorSeleccionado.contactoPrincipal && <> · Contacto: {proveedorSeleccionado.contactoPrincipal}</>}
                </div>
              )}
              {!loadingProveedores && proveedores.length === 0 && (
                <div style={{ marginTop: 5, fontSize: '0.75rem', color: 'var(--red)' }}>
                  No hay proveedores activos disponibles en core.proveedores.
                </div>
              )}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Términos de Pago
                </label>
                <select aria-label="Tipo de pago" value={poForm.terminosPago === 'Contado' ? 'Contado' : 'Crédito'}
                  style={{ width: '100%', padding: '0.5rem', borderRadius: 6, border: '1px solid var(--border)', background: 'var(--bg-surface)', color: 'var(--text-primary)' }}
                  onChange={e => setPoForm(f => ({ ...f, terminosPago: e.target.value === 'Contado' ? 'Contado' : 'Crédito 30 días' }))}>
                  <option value="Contado">Contado</option><option value="Crédito">Crédito</option>
                </select>
                {poForm.terminosPago !== 'Contado' && <label style={{ display: 'block', marginTop: 8 }}>Días de crédito
                  <input aria-label="Días de crédito" type="number" min="1" max="3650" step="1" required
                    style={{ display: 'block', width: '100%', padding: '0.5rem', marginTop: 4, borderRadius: 6, boxSizing: 'border-box', border: '1px solid var(--border)', background: 'var(--bg-surface)', color: 'var(--text-primary)' }}
                    value={poForm.terminosPago?.match(/\d+/)?.[0] ?? ''}
                    onChange={e => setPoForm(f => ({ ...f, terminosPago: 'Crédito ' + e.target.value + ' días' }))} />
                </label>}
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Fecha Entrega Esperada
                </label>
                <input
                  type="date"
                  aria-label="Fecha estimada de entrega"
                  required
                  value={poForm.fechaEntregaEsperada}
                  onChange={e => setPoForm(f => ({ ...f, fechaEntregaEsperada: e.target.value }))}
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>
            </div>

            <ItemsPOEditor items={poForm.items} disabled={savingPo} onPendingChange={setItemPendiente}
              onChange={items => setPoForm(f => ({ ...f, items }))} />

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setPoModalOpen(false)}
                style={{
                  padding: '0.5rem 1rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                }}
              >
                Cancelar
              </button>
              <button
                type="submit"
                data-create-po="true"
                disabled={savingPo || !poForm.proveedorId || poForm.items.length === 0 || itemPendiente}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: savingPo || itemPendiente ? 'not-allowed' : 'pointer',
                }}
              >
                {savingPo ? 'Guardando...' : 'Crear Orden'}
              </button>
            </div>
            </fieldset>
          </form>
        </Modal>
      )}

      {/* Modal Recepción Inbound */}
      {recepcionModalOpen && recepcionPO && (
        <Modal
          title={`Recepción Inbound — ${recepcionPO.numeroPO}`}
          isOpen={true}
          onClose={() => {
            if (!savingRecepcion) {
              setRecepcionModalOpen(false);
              setRecepcionPO(null);
            }
          }}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <p style={{ margin: 0, fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
              Registra únicamente las unidades físicamente recibidas y asigna una ubicación WMS para cada SKU.
            </p>

            {recepcionItems.map((linea, index) => {
              const pendiente = linea.cantidadSolicitada - linea.cantidadRecibidaBase;
              return (
                <div
                  key={linea.itemOrdenCompraId}
                  style={{
                    padding: '0.9rem',
                    border: '1px solid var(--border)',
                    borderRadius: '8px',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '0.65rem',
                  }}
                >
                  <div>
                    <strong style={{ color: 'var(--text-primary)' }}>{linea.sku || 'SKU'}</strong>
                    <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>{linea.descripcion}</div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-secondary)', marginTop: '0.2rem' }}>
                      Solicitado: {linea.cantidadSolicitada} · Recibido: {linea.cantidadRecibidaBase} · Pendiente: {pendiente}
                    </div>
                  </div>

                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: '0.75rem' }}>
                    <div>
                      <label style={{ display: 'block', fontSize: '0.78rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                        Cantidad recibida
                      </label>
                      <input
                        type="number"
                        min={0}
                        max={pendiente}
                        value={linea.cantidadRecibir}
                        onChange={e => {
                          const value = Math.max(0, Math.min(pendiente, parseInt(e.target.value) || 0));
                          setRecepcionItems(prev => prev.map((item, i) => i === index ? { ...item, cantidadRecibir: value } : item));
                        }}
                        style={{
                          width: '100%',
                          padding: '0.5rem 0.65rem',
                          borderRadius: '6px',
                          background: 'var(--bg-surface)',
                          border: '1px solid var(--border)',
                          color: 'var(--text-primary)',
                          boxSizing: 'border-box',
                        }}
                      />
                    </div>

                    <div>
                      <label style={{ display: 'block', fontSize: '0.78rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                        Ubicación WMS
                      </label>
                      <select
                        value={linea.ubicacionAlmacenId}
                        onChange={e => setRecepcionItems(prev => prev.map((item, i) => i === index ? { ...item, ubicacionAlmacenId: e.target.value } : item))}
                        style={{
                          width: '100%',
                          padding: '0.5rem 0.65rem',
                          borderRadius: '6px',
                          background: 'var(--bg-surface)',
                          border: '1px solid var(--border)',
                          color: 'var(--text-primary)',
                          boxSizing: 'border-box',
                        }}
                      >
                        <option value="">Seleccionar ubicación...</option>
                        {ubicacionesWms.map(ubicacion => (
                          <option key={ubicacion.id} value={ubicacion.id}>
                            {ubicacion.codigo} — {ubicacion.zona} (cap. {ubicacion.capacidadMaxima})
                          </option>
                        ))}
                      </select>
                    </div>
                  </div>
                </div>
              );
            })}

            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                Notas de recepción
              </label>
              <textarea
                value={recepcionNotas}
                onChange={e => setRecepcionNotas(e.target.value)}
                rows={3}
                placeholder="Opcional"
                style={{
                  width: '100%',
                  padding: '0.55rem 0.75rem',
                  borderRadius: '6px',
                  background: 'var(--bg-surface)',
                  border: '1px solid var(--border)',
                  color: 'var(--text-primary)',
                  boxSizing: 'border-box',
                  resize: 'vertical',
                }}
              />
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem' }}>
              <button
                type="button"
                disabled={savingRecepcion}
                onClick={() => {
                  setRecepcionModalOpen(false);
                  setRecepcionPO(null);
                }}
                style={{
                  padding: '0.5rem 1rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: savingRecepcion ? 'not-allowed' : 'pointer',
                }}
              >
                Cancelar
              </button>
              <button
                type="button"
                disabled={savingRecepcion}
                onClick={handleRecibirMercaderia}
                style={{
                  padding: '0.5rem 1.2rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--green)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: savingRecepcion ? 'not-allowed' : 'pointer',
                }}
              >
                {savingRecepcion ? 'Registrando...' : 'Confirmar recepción'}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* Modal Registrar Factura */}
      {facturaModalOpen && (
        <Modal
          title="Registrar Factura de Proveedor"
          size="lg"
          isOpen={true}
          onClose={() => { if (!savingFactura) setFacturaModalOpen(false); }}
        >
          <form
            onSubmit={(e) => {
              e.preventDefault();
              handleRegistrarFactura();
            }}
            style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}
          >
            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                N° de Factura Fiscal
              </label>
              <input
                type="text"
                value={facturaForm.numeroFactura}
                onChange={e => setFacturaForm(f => ({ ...f, numeroFactura: e.target.value }))}
                required
                style={{
                  width: '100%',
                  padding: '0.5rem 0.75rem',
                  borderRadius: '6px',
                  background: 'var(--bg-surface)',
                  border: '1px solid var(--border)',
                  color: 'var(--text-primary)',
                  boxSizing: 'border-box',
                }}
              />
            </div>

            <p>Captura las cantidades y precios del documento del proveedor. El 3-Way Match los comparará con la orden y la recepción.</p>
            <div style={{ overflowX: 'auto' }}>
              <table aria-label="Detalle de factura" style={{ width: '100%', textAlign: 'left' }}>
                <thead><tr><th>Producto</th><th>Cantidad facturada</th><th>Precio facturado ($)</th></tr></thead>
                <tbody>{facturaPO?.items.map((item, index) => <tr key={item.id}>
                  <td>{item.descripcion}<br /><small>{item.skuReferencia}</small></td>
                  <td><input aria-label={`Cantidad facturada ${item.skuReferencia}`} type="number" min="1" step="1" required disabled={savingFactura}
                    value={facturaForm.lineas?.[index]?.cantidadFacturada || ''}
                    onChange={e => actualizarLineaFactura(index, 'cantidadFacturada', Number(e.target.value))} style={{ width: 100 }} /></td>
                  <td><input aria-label={`Precio facturado ${item.skuReferencia}`} type="number" min="0.01" step="0.01" required disabled={savingFactura}
                    value={facturaForm.lineas?.[index]?.precioUnitarioFacturado || ''}
                    onChange={e => actualizarLineaFactura(index, 'precioUnitarioFacturado', Number(e.target.value))} style={{ width: 120 }} /></td>
                </tr>)}</tbody>
              </table>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Sub-Total ($)
                </label>
                <input
                  type="number"
                  step="0.01"
                  value={facturaForm.subTotal}
                  readOnly
                  required
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Monto Total con IVA ($)
                </label>
                <input
                  type="number"
                  step="0.01"
                  value={facturaForm.montoTotal}
                  readOnly
                  required
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setFacturaModalOpen(false)}
                style={{
                  padding: '0.5rem 1rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                }}
              >
                Cancelar
              </button>
              <button
                type="submit"
                disabled={savingFactura}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: savingFactura ? 'not-allowed' : 'pointer',
                }}
              >
                {savingFactura ? 'Registrando...' : 'Registrar Factura'}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {/* Modal 3-Way Match */}
      {matchModalOpen && (
        <Modal
          title="Auditoría 3-Way Match (PO ↔ Recepción Bodega ↔ Factura)"
          isOpen={true}
          onClose={() => {
            setMatchModalOpen(false);
            setMatchResult(null);
          }}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                Seleccione Orden de Compra (PO)
              </label>
              <select
                value={matchForm.ordenCompraId}
                onChange={e => { void cargarPOParaMatch(e.target.value); }}
                style={{
                  width: '100%',
                  padding: '0.5rem 0.75rem',
                  borderRadius: '6px',
                  background: 'var(--bg-surface)',
                  border: '1px solid var(--border)',
                  color: 'var(--text-primary)',
                  boxSizing: 'border-box',
                }}
              >
                {ordenesCompra.map(po => (
                  <option key={po.id} value={po.id}>
                    {po.numeroPO} — {po.nombreProveedor} (${po.montoTotal.toFixed(2)})
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                Factura de proveedor
              </label>
              <select
                value={matchForm.facturaProveedorId}
                onChange={e => setMatchForm(f => ({ ...f, facturaProveedorId: e.target.value }))}
                disabled={loadingMatchPO || facturasMatch.length === 0}
                style={{
                  width: '100%',
                  padding: '0.5rem 0.75rem',
                  borderRadius: '6px',
                  background: 'var(--bg-surface)',
                  border: '1px solid var(--border)',
                  color: 'var(--text-primary)',
                  boxSizing: 'border-box',
                }}
              >
                <option value="">
                  {loadingMatchPO ? 'Cargando facturas...' : 'Seleccione una factura pendiente'}
                </option>
                {facturasMatch.map(factura => (
                  <option
                    key={factura.id}
                    value={factura.id}
                    disabled={factura.aprobadaParaPago || factura.pagada}
                  >
                    {factura.numeroFactura} — ${factura.montoTotal.toFixed(2)}
                    {factura.pagada ? ' (Pagada)' : factura.aprobadaParaPago ? ' (Aprobada)' : ''}
                  </option>
                ))}
              </select>
              {!loadingMatchPO && facturasMatch.length === 0 && (
                <p style={{ margin: '0.35rem 0 0', fontSize: '0.75rem', color: 'var(--text-secondary)' }}>
                  La orden seleccionada no tiene facturas registradas.
                </p>
              )}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Cantidad Física Recibida en Bodega
                </label>
                <input
                  type="number"
                  value={matchForm.cantidadRecibidaBodega}
                  readOnly
                  aria-label="Cantidad recibida según recepciones WMS"
                  title="Cantidad obtenida automáticamente de recepciones WMS registradas"
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Tolerancia Permitida (%)
                </label>
                <input
                  type="number"
                  step="0.1"
                  value={matchForm.toleranciaPorcentaje}
                  onChange={e => setMatchForm(f => ({ ...f, toleranciaPorcentaje: parseFloat(e.target.value) || 0 }))}
                  style={{
                    width: '100%',
                    padding: '0.5rem 0.75rem',
                    borderRadius: '6px',
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    color: 'var(--text-primary)',
                    boxSizing: 'border-box',
                  }}
                />
              </div>
            </div>

            {/* Resultado si se ejecutó */}
            {matchResult && (
              <div
                style={{
                  padding: '1rem',
                  borderRadius: '8px',
                  border: `1px solid ${matchResult.resultado === 'Conforme' ? 'var(--green)' : 'var(--red)'}`,
                  background: matchResult.resultado === 'Conforme' ? 'rgba(34, 197, 94, 0.08)' : 'rgba(239, 68, 68, 0.08)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: '0.5rem',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontWeight: 700, color: matchResult.resultado === 'Conforme' ? 'var(--green)' : 'var(--red)' }}>
                  {matchResult.resultado === 'Conforme' ? <CheckCircle2 size={18} /> : <AlertTriangle size={18} />}
                  Resultado: {matchResult.resultado.toUpperCase()}
                </div>
                <div style={{ fontSize: '0.85rem', color: 'var(--text-primary)' }}>
                  {matchResult.observaciones}
                </div>
                <div style={{ display: 'flex', gap: '1.5rem', fontSize: '0.8rem', color: 'var(--text-secondary)', marginTop: '0.25rem' }}>
                  <span>Diferencia Cantidad: <strong>{matchResult.diferenciaCantidad} unid.</strong></span>
                  <span>Diferencia Monto: <strong>${matchResult.diferenciaMonto.toFixed(2)}</strong></span>
                </div>
              </div>
            )}

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => {
                  setMatchModalOpen(false);
                  setMatchResult(null);
                }}
                style={{
                  padding: '0.5rem 1rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                }}
              >
                Cerrar
              </button>
              <button
                type="button"
                onClick={handleEjecutarMatch}
                disabled={runningMatch || loadingMatchPO || !matchForm.facturaProveedorId || matchForm.cantidadRecibidaBodega <= 0}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: runningMatch || loadingMatchPO || !matchForm.facturaProveedorId || matchForm.cantidadRecibidaBodega <= 0 ? 'not-allowed' : 'pointer',
                }}
              >
                {runningMatch ? 'Auditando...' : 'Ejecutar Validación'}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
