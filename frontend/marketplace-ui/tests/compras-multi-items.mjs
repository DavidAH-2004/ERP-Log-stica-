/** UI de Compras con API local simulada; nunca utiliza servicios o datos reales. */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const base = 'http://127.0.0.1:4186';
const vite = spawn(process.execPath, ['node_modules/vite/bin/vite.js', 'preview', '--host', '127.0.0.1', '--port', '4186', '--strictPort'], {
  stdio: 'pipe', env: { ...process.env, VITE_API_BASE_URL: '', VITE_BFF_AUTH_MODE: '' },
});
let output = '';
vite.stdout.on('data', x => { output += x; });
vite.stderr.on('data', x => { output += x; });
let browser;
let page;
try {
  for (let i = 0; ; i++) {
    try { if ((await fetch(base)).ok) break; } catch {}
    if (i > 100 || vite.exitCode !== null) throw Error(output);
    await delay(200);
  }
  browser = await chromium.launch({ headless: true, executablePath: process.env.QA_BROWSER_PATH,
    channel: process.env.QA_BROWSER_CHANNEL || 'chromium' });
  page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
  const errors = [];
  page.on('pageerror', e => { errors.push(e.message); console.error(e.message); });
  const products = Array.from({ length: 11 }, (_, i) => ({ id: `p${i}`, sku: `SKU-${i + 1}`, nombre: `Producto ${i + 1}`, precioCompraReferencia: null }));
  const orders = [];
  const posts = [];
  let invoice;
  let failNext = false;
  let failMatchOnce = false;
  await page.route('**/api/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    assert.equal(new URL(request.url()).hostname, '127.0.0.1');
    const json = (data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
    if (path === '/api/auth/login') {
      const expiresAt = new Date(Date.now() + 3600000).toISOString();
      const body = Buffer.from(JSON.stringify({ role: 'Admin', exp: Math.floor(Date.now() / 1000) + 3600 })).toString('base64url');
      return json({ token: `e30.${body}.local-test`, role: 'Admin', rol: 'Admin', usuario: 'QA Compras', expiresAt });
    }
    if (path === '/api/proveedores/logistica') return json({ items: [{ id: 'proveedor-1', nombreComercial: 'Proveedor de prueba', razonSocial: 'Proveedor de prueba', plazoCreditoDias: 20, otifHistorico: 99 }] });
    if (path === '/api/compras/productos-disponibles') return json({ total: products.length, pagina: 1, tamano: 25, items: products });
    if (path === '/api/compras/match/historial') {
      if (failMatchOnce) { failMatchOnce = false; return json({ mensaje: 'Historial temporalmente no disponible' }, 503); }
      return json([]);
    }
    if (path === '/api/compras/po' && request.method() === 'POST') {
      const body = request.postDataJSON();
      posts.push(body);
      if (failNext) { failNext = false; return json({ mensaje: 'Error temporal de prueba' }, 500); }
      const subtotal = body.items.reduce((s, x) => s + Math.round(x.precioUnitario * 100) * x.cantidad, 0) / 100;
      orders.push({ id: 'po-1', numeroPO: 'PO-MULTI-QA', proveedorNombre: body.proveedorNombre,
        estado: 'Enviada', montoTotal: Math.round(subtotal * 1.13 * 100) / 100, moneda: 'USD', terminosPago: body.terminosPago,
        fechaEmision: '2026-11-15T00:00:00Z', items: body.items.map((x, i) => ({ id: `item-${i}`, sku: x.sku, nombreProducto: x.nombreProducto, cantidadSolicitada: x.cantidad, cantidadRecibida: 0, precioUnitarioAcordado: x.precioUnitario, totalLinea: x.cantidad * x.precioUnitario })) });
      // Reproduce el caso QA real: orden persistida + HTTP 201 sin cuerpo.
      return route.fulfill({ status: 201, contentType: 'application/json', body: '' });
    }
    if (path === '/api/compras/po') return json({ total: orders.length, ordenesCompra: orders });
    if (path === '/api/compras/facturas') {
      invoice = request.postDataJSON();
      return route.fulfill({ status: 201, contentType: 'application/json', body: '' });
    }
    return json({ mensaje: 'Endpoint fuera del fixture' }, 503);
  });
  await page.goto(base + '/login');
  await page.getByPlaceholder('nombre@ejemplo.com').fill('admin@example.invalid');
  await page.locator('input[type=password]').fill('local-password');
  await page.getByRole('button', { name: 'Ingresar al sistema' }).click();
  await page.waitForURL('**/dashboard');
  await page.goto(base + '/compras');
  await page.getByRole('button', { name: 'Nueva Orden de Compra (PO)' }).click();
  const modal = page.locator('.modal');
  await modal.locator('select').first().selectOption('proveedor-1');
  assert.equal(await modal.getByLabel('Días de crédito').inputValue(), '20');
  assert.equal(await modal.getByRole('button', { name: 'Crear Orden', exact: true }).isDisabled(), true);
  const choose = async sku => {
    await modal.getByRole('button', { name: 'Seleccionar producto registrado', exact: true }).click();
    await modal.getByRole('option').filter({ has: page.getByText(sku, { exact: true }) }).click();
  };
  const add = async (sku, quantity = '2', price = '2.55') => {
    await choose(sku);
    await modal.getByLabel('Cantidad del producto', { exact: true }).fill(quantity);
    await modal.getByLabel('Precio unitario del producto', { exact: true }).fill(price);
    await modal.getByRole('button', { name: '+ Agregar a la lista', exact: true }).click();
  };
  await add('SKU-1');
  assert.equal(posts.length, 0, 'Agregar no debe hacer POST');
  await modal.getByText('Total PO (con IVA): $5.76', { exact: true }).waitFor();
  await add('SKU-1');
  await modal.getByRole('alert').getByText(/ya está en la lista/).waitFor();
  await modal.getByRole('button', { name: 'Cancelar ítem', exact: true }).click();
  await modal.getByRole('button', { name: 'Editar SKU-1', exact: true }).click();
  await modal.getByLabel('Cantidad del producto', { exact: true }).fill('3');
  await modal.getByRole('button', { name: 'Guardar ítem' }).click();
  await modal.getByText('Total PO (con IVA): $8.64', { exact: true }).waitFor();
  await modal.getByRole('button', { name: 'Eliminar SKU-1', exact: true }).click();
  assert.equal(await modal.getByRole('button', { name: 'Crear Orden', exact: true }).isDisabled(), true);
  await add('SKU-1', '1', '0.50');
  await modal.getByText('Total PO (con IVA): $0.56', { exact: true }).waitFor();
  await modal.getByRole('button', { name: 'Eliminar SKU-1', exact: true }).click();
  await add('SKU-1', '1.5', '2.55');
  await modal.getByRole('alert').getByText(/cantidad entera positiva/).waitFor();
  await modal.getByRole('button', { name: 'Cancelar ítem', exact: true }).click();
  for (let i = 1; i <= 10; i++) await add(`SKU-${i}`);
  await modal.getByText(/Límite alcanzado: 10 productos/).waitFor();
  assert.equal(await modal.getByRole('button', { name: '+ Agregar a la lista', exact: true }).isDisabled(), true);
  assert.equal(await modal.getByRole('table', { name: 'Productos de la orden' }).locator('tbody tr').count(), 10);
  await modal.getByText('Total PO (con IVA): $57.63', { exact: true }).waitFor();
  await modal.getByLabel('Fecha estimada de entrega').fill('2026-12-01');
  await modal.getByLabel('Días de crédito').fill('25');
  await modal.getByLabel('Días de crédito').press('Enter');
  assert.equal(posts.length, 0, 'Enter en un campo no crea la PO');
  if (process.env.QA_SCREENSHOT) await page.screenshot({ path: process.env.QA_SCREENSHOT, fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await modal.evaluate(el => el.scrollWidth <= el.clientWidth + 1), true, 'Modal sin desbordamiento horizontal en móvil');
  await page.setViewportSize({ width: 1280, height: 1000 });
  failNext = true;
  await modal.getByRole('button', { name: 'Crear Orden', exact: true }).click();
  await page.getByText('Error temporal de prueba', { exact: true }).waitFor();
  assert.equal(await modal.getByRole('table', { name: 'Productos de la orden' }).locator('tbody tr').count(), 10, 'Error debe conservar lista');
  await modal.getByRole('button', { name: 'Crear Orden', exact: true }).click();
  await modal.waitFor({ state: 'hidden' });
  assert.equal(posts.length, 2, 'Un POST por cada intento explícito');
  assert.equal(orders.length, 1);
  assert.equal(posts[1].items.length, 10);
  assert.equal(posts[1].fechaEntregaEsperada, '2026-12-01');
  assert.equal(posts[1].terminosPago, 'Crédito 25 días');
  await page.getByText('PO-MULTI-QA', { exact: true }).waitFor();
  await page.getByText('Orden de Compra Generada', { exact: true }).waitFor();
  // El listado debe seguir mostrando las PO si falla solamente el historial.
  failMatchOnce = true;
  await page.getByRole('button', { name: 'Refrescar', exact: true }).click();
  await page.getByText('No se pudo cargar el historial de 3-Way Match.', { exact: false }).waitFor();
  await page.getByText('PO-MULTI-QA', { exact: true }).waitFor();
  await page.getByRole('button', { name: /Factura/ }).click();
  const invoiceModal = page.locator('.modal');
  await invoiceModal.locator('input[type=text]').fill('FACT-QA-10');
  for (let i = 1; i <= 10; i++) {
    await invoiceModal.getByLabel(`Cantidad facturada SKU-${i}`, { exact: true }).fill('2');
    await invoiceModal.getByLabel(`Precio facturado SKU-${i}`, { exact: true }).fill('2.55');
  }
  await invoiceModal.getByRole('button', { name: 'Registrar Factura', exact: true }).click();
  await invoiceModal.waitFor({ state: 'hidden' });
  assert.equal(invoice.lineas.length, 10);
  assert.equal(invoice.subTotal, 51);
  assert.equal(invoice.montoTotal, 57.63);
  assert.equal(new Set(invoice.lineas.map(x => x.itemOrdenCompraId)).size, 10);
  assert.deepEqual(errors, []);
  console.log('OK: 201 sin JSON, no hay falsa falla ni PO duplicada, lista parcial, edición, 10 productos y factura.');
} catch (error) {
  console.error('URL:', page?.url(), 'Body:', await page?.locator('body').innerText().catch(() => ''), 'Vite:', output.slice(-1500));
  throw error;
} finally {
  await browser?.close();
  vite.kill();
}
