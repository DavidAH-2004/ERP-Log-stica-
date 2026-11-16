/** F660: supplier registration browser E2E against MOCK API, no external writes. */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const base = 'http://127.0.0.1:4188';
const vite = spawn(process.execPath, [
  'node_modules/vite/bin/vite.js', 'preview',
  '--host', '127.0.0.1', '--port', '4188', '--strictPort'
], { stdio: 'pipe', env: { ...process.env, VITE_BFF_AUTH_MODE: '' } });
let output = '';
vite.stdout.on('data', x => { output += x; });
vite.stderr.on('data', x => { output += x; });
let browser;
try {
  for (let i = 0; ; i++) {
    try { if ((await fetch(base)).ok) break; } catch {}
    if (i > 100 || vite.exitCode !== null) throw Error('Vite unavailable: ' + output.slice(-1000));
    await delay(200);
  }
  browser = await chromium.launch({
    headless: true, executablePath: process.env.QA_BROWSER_PATH,
    channel: process.env.QA_BROWSER_CHANNEL || 'chromium'
  });
  const page = await browser.newPage({ viewport: { width: 1320, height: 1000 } });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  const suppliers = [];
  const postBodies = [];

  await page.route('**/api/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    assert.equal(new URL(request.url()).hostname, '127.0.0.1');
    const reply = (data, status = 200) => route.fulfill({
      status, contentType: 'application/json', body: JSON.stringify(data)
    });
    if (path === '/api/auth/login') {
      const payload = Buffer.from(JSON.stringify({
        role: 'Admin', exp: Math.floor(Date.now() / 1000) + 3600
      })).toString('base64url');
      return reply({ token: 'e30.' + payload + '.local-test', role: 'Admin', rol: 'Admin',
        usuario: 'Admin QA', expiresAt: new Date(Date.now() + 3600000).toISOString() });
    }
    if (path === '/api/proveedores/logistica' && request.method() === 'GET') {
      return reply({ total: suppliers.length, conProductosCatalogo: 0, conCompraAbierta: 0,
        nombresCatalogoNoVinculados: [], items: suppliers });
    }
    if (path === '/api/proveedores' && request.method() === 'POST') {
      const body = request.postDataJSON();
      postBodies.push(body);
      // Intentionally delay the response to cover duplicate clicks while busy.
      await delay(150);
      if (suppliers.some(x => x.nombreComercial.toLowerCase() === body.nombreComercial.toLowerCase())) {
        return reply({ mensaje: 'Ya existe un proveedor con ese nombre comercial.' }, 409);
      }
      const created = { ...body, id: 'supplier-' + (suppliers.length + 1),
        plazoCreditoDias: body.diasCredito, otifHistorico: null,
        productosCatalogo: 0, productosBajoMinimo: 0,
        ordenesCompraAbiertas: 0, unidadesPendientesRecepcion: 0 };
      suppliers.push(created);
      return reply(created, 201);
    }
    if (path === '/api/compras/po') return reply({ total: 0, ordenesCompra: [] });
    if (path === '/api/compras/match/historial') return reply([]);
    if (path === '/api/compras/productos-disponibles') return reply({ total: 0, items: [] });
    return reply({ mensaje: 'Fixture endpoint unavailable' }, 503);
  });

  await page.goto(base + '/login');
  await page.getByPlaceholder('nombre@ejemplo.com').fill('admin@example.invalid');
  await page.locator('input[type=password]').fill('local-secret');
  await page.getByRole('button', { name: 'Ingresar al sistema' }).click();
  await page.waitForURL('**/dashboard');
  await page.goto(base + '/proveedores');
  await page.getByRole('button', { name: 'Nuevo Proveedor' }).click();
  const modal = page.locator('.modal').last();
  assert.equal(await modal.getByText('Sin historial').count(), 1);

  // Empty required fields should not reach the API.
  await modal.getByRole('button', { name: 'Guardar Proveedor' }).click();
  assert.equal(postBodies.length, 0);
  await modal.getByRole('textbox', { name: 'Nombre comercial' }).fill('Suministros QA');
  await modal.getByRole('textbox', { name: 'Razón social' }).fill('Suministros QA SA');
  await modal.getByRole('textbox', { name: 'NIT' }).fill('123');
  await modal.getByRole('button', { name: 'Guardar Proveedor' }).click();
  await modal.getByRole('alert').getByText(/NIT/i).waitFor();
  assert.equal(postBodies.length, 0);

  await modal.getByRole('textbox', { name: 'NIT' }).fill('0614-111111-111-1');
  await modal.getByRole('combobox', { name: 'Condiciones de pago' }).selectOption('Credito');
  await modal.getByRole('spinbutton', { name: 'Días de crédito' }).fill('45');
  await modal.getByRole('button', { name: 'Guardar Proveedor' }).click();
  await page.getByText('Suministros QA', { exact: true }).first().waitFor();
  assert.equal(postBodies.length, 1);
  assert.equal(suppliers[0].plazoCreditoDias, 45);
  assert.equal(suppliers[0].otifHistorico, null);
  assert.equal(await page.locator('.modal').count(), 0);
  await page.getByText('Sin historial', { exact: true }).first().waitFor();
  console.log('F660_REGISTER_AND_LIST_PASS');

  // Duplicate returns HTTP 409, leaves the modal open and never creates a row.
  await page.getByRole('button', { name: 'Nuevo Proveedor' }).click();
  const duplicate = page.locator('.modal').last();
  await duplicate.getByRole('textbox', { name: 'Nombre comercial' }).fill('Suministros QA');
  await duplicate.getByRole('textbox', { name: 'Razón social' }).fill('Otro nombre legal');
  await duplicate.getByRole('button', { name: 'Guardar Proveedor' }).click();
  await duplicate.getByRole('alert').getByText(/existe/i).waitFor();
  assert.equal(suppliers.length, 1);
  await duplicate.getByRole('button', { name: 'Cancelar' }).click();
  console.log('F660_DUPLICATE_UI_PASS');

  await page.goto(base + '/compras');
  await page.getByRole('button', { name: 'Nueva Orden de Compra (PO)' }).click();
  const po = page.locator('.modal').first();
  await po.getByRole('button', { name: 'Nuevo Proveedor' }).click();
  const newFromPurchase = page.locator('.modal').last();
  await newFromPurchase.getByRole('textbox', { name: 'Nombre comercial' }).fill('Proveedor Dos');
  await newFromPurchase.getByRole('textbox', { name: 'Razón social' }).fill('Proveedor Dos SL');
  await newFromPurchase.getByRole('button', { name: 'Guardar Proveedor' }).click();
  await page.locator('.modal').last().getByText('Proveedor Dos SL', { exact: true }).waitFor();
  assert.equal(suppliers.length, 2);
  assert.equal(await po.locator('select').first().inputValue(), 'supplier-2');
  assert.equal(postBodies.length, 3);
  assert.equal(await po.getByRole('button', { name: 'Crear Orden', exact: true }).isDisabled(), true);
  console.log('F660_SELECT_NEW_SUPPLIER_IN_PO_PASS');
  assert.deepEqual(errors, []);
  console.log('F660_BROWSER_E2E_PASS');
} finally {
  await browser?.close();
  vite.kill();
}
