import React, { useRef, useState } from 'react';
import Modal from '../../components/ui/Modal';
import { useToast } from '../../context/ToastContext';
import { ApiError } from '../../api/client';
import {
  proveedoresApi,
  type ProveedorCreado,
  type RegistrarProveedorRequest,
} from '../../api/proveedores';

interface Props {
  onClose: () => void;
  onCreated: (proveedor: ProveedorCreado) => void | Promise<void>;
}

const initial: RegistrarProveedorRequest = {
  nombreComercial: '',
  razonSocial: '',
  nit: '',
  nrc: '',
  categoriaSuministro: '',
  contactoPrincipal: '',
  email: '',
  telefono: '',
  direccion: '',
  condicionesPago: 'Contado',
  diasCredito: 0,
  activo: true,
};

const normalize = (text: string) => text.trim().replace(/\s+/g, ' ').toLocaleLowerCase();

export default function NuevoProveedorModal({ onClose, onCreated }: Props) {
  const [form, setForm] = useState<RegistrarProveedorRequest>(initial);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const savingRef = useRef(false);
  const toast = useToast();

  const setText = (key: keyof RegistrarProveedorRequest, value: string) => {
    setForm(previous => ({ ...previous, [key]: value }));
    setError('');
  };

  const field = (
    title: string,
    key: 'nombreComercial' | 'razonSocial' | 'nit' | 'nrc' | 'categoriaSuministro' |
      'contactoPrincipal' | 'email' | 'telefono' | 'direccion',
    options: { required?: boolean; maxLength?: number; type?: string; placeholder?: string } = {}
  ) => (
    <label style={{ display: 'grid', gap: 5, fontSize: '0.8rem', color: 'var(--text-secondary)' }}>
      {title}{options.required ? ' *' : ''}
      <input
        aria-label={title}
        type={options.type ?? 'text'}
        required={options.required}
        maxLength={options.maxLength}
        placeholder={options.placeholder}
        value={form[key]}
        onChange={event => setText(key, event.target.value)}
        style={{ width: '100%', padding: '0.58rem 0.68rem', boxSizing: 'border-box',
          border: '1px solid var(--border)', borderRadius: 6,
          background: 'var(--bg-surface)', color: 'var(--text-primary)' }}
      />
    </label>
  );

  const validate = (): string | null => {
    if (!form.nombreComercial.trim() || !form.razonSocial.trim()) {
      return 'Nombre comercial y razón social son obligatorios.';
    }
    const nit = form.nit.replace(/[\s-]/g, '');
    if (nit && !/^[0-9]{14}$/.test(nit)) {
      return 'El NIT debe contener 14 dígitos; puedes incluir guiones.';
    }
    if (form.email.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim())) {
      return 'Introduce un correo electrónico válido.';
    }
    if (form.telefono.trim() &&
        (!/^\+?[0-9()\-\s]{7,30}$/.test(form.telefono.trim()) ||
         (form.telefono.match(/[0-9]/g) ?? []).length < 7)) {
      return 'El teléfono necesita al menos 7 dígitos y un formato válido.';
    }
    if (form.condicionesPago === 'Contado' && form.diasCredito !== 0) {
      return 'El pago de contado requiere 0 días de crédito.';
    }
    if (form.condicionesPago === 'Credito' &&
        (!Number.isInteger(form.diasCredito) || form.diasCredito < 1 || form.diasCredito > 3650)) {
      return 'El plazo de crédito debe estar entre 1 y 3650 días.';
    }
    return null;
  };

  const save = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (savingRef.current) return;
    const validationError = validate();
    if (validationError) { setError(validationError); return; }
    savingRef.current = true;
    setSaving(true);
    setError('');
    try {
      const created = await proveedoresApi.crear(form);
      if (!created?.id) throw new Error('El servidor no confirmó el identificador. Consulta el listado antes de repetir.');
      toast.success('Proveedor registrado correctamente', created.nombreComercial);
      await onCreated(created);
    } catch (err) {
      const uncertain = (err instanceof ApiError && (err.status === 0 || err.status >= 500)) ||
        !(err instanceof ApiError);
      if (uncertain && form.activo) {
        // A lost HTTP response does not imply a failed write. Look for an exact
        // match before inviting another POST, so concurrent/repeated actions
        // cannot silently create duplicate master entries.
        try {
          const current = await proveedoresApi.listar();
          const found = current.items.find(item =>
            normalize(item.nombreComercial) === normalize(form.nombreComercial) &&
            normalize(item.razonSocial) === normalize(form.razonSocial));
          if (found) {
            toast.warning('Verifica el registro', 'El proveedor ya aparece en el listado; no repitas el envío.');
            await onCreated({ ...found, activo: true });
            return;
          }
        } catch { /* Keep the warning: result remains uncertain. */ }
      }
      const message = err instanceof Error ? err.message : 'No se pudo guardar el proveedor.';
      setError(uncertain
        ? message + ' Comprueba primero el maestro de proveedores antes de volver a guardar.'
        : message);
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  return (
    <Modal isOpen title="Registrar nuevo proveedor" size="lg" onClose={() => { if (!savingRef.current) onClose(); }}>
      <form onSubmit={save} style={{ display: 'grid', gap: 14 }}>
        <fieldset disabled={saving} style={{ border: 0, padding: 0, margin: 0, minWidth: 0, display: 'grid', gap: 14 }}>
          <p style={{ margin: 0, color: 'var(--text-muted)', fontSize: '0.8rem' }}>
            El nuevo registro se guardará en el maestro corporativo de proveedores. Los campos con * son obligatorios.
          </p>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(225px, 1fr))', gap: 12 }}>
            {field('Nombre comercial', 'nombreComercial', { required: true, maxLength: 200 })}
            {field('Razón social', 'razonSocial', { required: true, maxLength: 250 })}
            {field('NIT', 'nit', { maxLength: 25, placeholder: '0000-000000-000-0' })}
            {field('NRC', 'nrc', { maxLength: 50 })}
            {field('Categoría de suministro', 'categoriaSuministro', { maxLength: 100 })}
            {field('Contacto principal', 'contactoPrincipal', { maxLength: 150 })}
            {field('Correo electrónico', 'email', { type: 'email', maxLength: 254 })}
            {field('Teléfono', 'telefono', { type: 'tel', maxLength: 30 })}
          </div>
          {field('Dirección comercial', 'direccion', { maxLength: 300 })}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(225px, 1fr))', gap: 12 }}>
            <label style={{ display: 'grid', gap: 5, fontSize: '0.8rem', color: 'var(--text-secondary)' }}>
              Condiciones de pago
              <select aria-label="Condiciones de pago" value={form.condicionesPago}
                onChange={event => {
                  const value = event.target.value as 'Contado' | 'Credito';
                  setForm(previous => ({ ...previous, condicionesPago: value, diasCredito: value === 'Contado' ? 0 : 30 }));
                }}
                style={{ padding: '0.58rem', border: '1px solid var(--border)', borderRadius: 6, background: 'var(--bg-surface)', color: 'var(--text-primary)' }}>
                <option value="Contado">Contado</option>
                <option value="Credito">Crédito</option>
              </select>
            </label>
            <label style={{ display: 'grid', gap: 5, fontSize: '0.8rem', color: 'var(--text-secondary)' }}>
              Días de crédito
              <input aria-label="Días de crédito" type="number" required min={form.condicionesPago === 'Contado' ? 0 : 1}
                max={3650} step={1} disabled={form.condicionesPago === 'Contado'} value={form.diasCredito}
                onChange={event => setForm(previous => ({ ...previous, diasCredito: Number(event.target.value) }))}
                style={{ padding: '0.58rem', border: '1px solid var(--border)', borderRadius: 6, background: 'var(--bg-surface)', color: 'var(--text-primary)' }} />
            </label>
          </div>
          <label style={{ display: 'flex', gap: 8, alignItems: 'center', color: 'var(--text-secondary)', fontSize: '0.82rem' }}>
            <input type="checkbox" checked={form.activo}
              onChange={event => setForm(previous => ({ ...previous, activo: event.target.checked }))} />
            Proveedor activo
          </label>
          <div style={{ color: 'var(--text-muted)', fontSize: '0.79rem' }}>
            OTIF: <strong>Sin historial</strong> (se calcula con entregas reales; no se captura aquí).
          </div>
        </fieldset>
        {error && <p role="alert" style={{ margin: 0, color: 'var(--red)', fontSize: '0.82rem' }}>{error}</p>}
        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={saving}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={saving}>
            {saving ? 'Guardando…' : 'Guardar Proveedor'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
