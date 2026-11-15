/**
 * F355 — HTTPS Chromium verification of Preview-only HttpOnly BFF.
 * CI only, in-memory mock of Render; never touches Neon or real payments.
 */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { spawnSync } from 'node:child_process';
import https from 'node:https';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { bffCookieProxy } from '../bffCookieMode.js';

if (process.env.CI !== 'true' || process.env.VITE_BFF_AUTH_MODE !== 'enabled') {
  throw new Error('F355 requires CI and VITE_BFF_AUTH_MODE=enabled');
}

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dist = path.resolve(root, 'dist');
const port = 4178;
const origin = 'https://127.0.0.1:' + port;
const fixturePassword = 'f355-ephemeral-fixture-only';
const sessions = new Map();
const calls = [];
const expiry = () => new Date(Date.now() + 8 * 60_000).toISOString();

function makeJwt(email, role) {
  const payload = Buffer.from(JSON.stringify({
    email, role, iat: Math.floor(Date.now() / 1000),
    exp: Math.floor(Date.now() / 1000) + 480,
  })).toString('base64url');
  return 'e30.' + payload + '.f355-fixture-signature';
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' },
  });
}

async function fakeRenderFetch(upstream, options) {
  const uri = new URL(upstream);
  const authorization = options.headers.get('authorization') ?? '';
  assert.equal(options.headers.get('cookie'), null, 'Never forward browser cookies to Render');
  calls.push({ path: uri.pathname, method: options.method, hasBearer: authorization.startsWith('Bearer ') });

  if (uri.pathname === '/api/auth/login') {
    const body = JSON.parse(Buffer.from(options.body ?? []).toString('utf8'));
    if (body.password !== fixturePassword) return json({ mensaje: 'Credenciales inválidas.' }, 401);
    const email = String(body.email ?? '').toLowerCase();
    const role = email.startsWith('seller') ? 'Vendedor'
      : email.startsWith('buyer') ? 'Comprador' : 'Admin';
    const token = makeJwt(email, role);
    sessions.set(token, { email, role, expiresAt: expiry() });
    return json({ token, rol: role, role, usuario: role + ' F355', expiresAt: expiry() });
  }

  const token = authorization.startsWith('Bearer ') ? authorization.slice(7) : '';
  const identity = sessions.get(token);
  if (!identity) return json({ mensaje: 'JWT inválido (fixture)' }, 401);

  if (uri.pathname === '/api/auth/session') {
    return json({
      usuario: identity.role + ' F355', rol: identity.role, role: identity.role,
      email: identity.email, vendedorId: identity.role === 'Vendedor'
        ? '11111111-1111-4111-8111-111111111111' : null,
      expiresAt: identity.expiresAt,
    });
  }
  if (uri.pathname === '/api/compras/po') {
    return identity.role === 'Admin' ? json({ items: [], total: 0 }) : json({ mensaje: 'Denegado' }, 403);
  }
  if (uri.pathname === '/api/ordenes' && options.method === 'POST') {
    return new Response(null, { status: identity.role === 'Admin' ? 204 : 403 });
  }
  if (uri.pathname.startsWith('/api/kpis-dashboard')) {
    return json({ mensaje: 'KPI no disponible en fixture' }, 503);
  }
  if (uri.pathname.startsWith('/api/productos')) {
    return json({ total: 0, pagina: 1, tamano: 50, items: [] });
  }
  if (uri.pathname.startsWith('/api/ordenes')) {
    return json({ total: 0, pagina: 1, tamano: 20, items: [], ordenes: [] });
  }
  return json({ items: [], total: 0 });
}

const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff2': 'font/woff2',
};

async function serveStatic(req, res, pathname) {
  const decoded = decodeURIComponent(pathname);
  const relative = decoded === '/' ? 'index.html' : decoded.slice(1);
  const target = path.resolve(dist, relative);
  if (!target.startsWith(dist + path.sep)) {
    res.writeHead(403); res.end(); return;
  }
  let bytes;
  let extension = path.extname(target);
  try {
    bytes = await readFile(target);
  } catch {
    bytes = await readFile(path.join(dist, 'index.html'));
    extension = '.html';
  }
  res.writeHead(200, {
    'Content-Type': types[extension] ?? 'application/octet-stream',
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  });
  res.end(req.method === 'HEAD' ? undefined : bytes);
}

async function handle(req, res) {
  const url = new URL(req.url ?? '/', origin);
  if (!url.pathname.startsWith('/api/')) return serveStatic(req, res, url.pathname);

  const parts = [];
  for await (const part of req) parts.push(part);
  const body = Buffer.concat(parts);
  const headers = new Headers();
  for (const [name, value] of Object.entries(req.headers)) {
    if (Array.isArray(value)) headers.set(name, value.join(', '));
    else if (value !== undefined) headers.set(name, value);
  }
  const original = new Request(url, {
    method: req.method,
    headers,
    body: body.length ? body : undefined,
  });
  const outgoing = await bffCookieProxy(original, 'https://render.local.invalid', fakeRenderFetch);
  const responseHeaders = {};
  for (const [name, value] of outgoing.headers) {
    if (name !== 'set-cookie') responseHeaders[name] = value;
  }
  const setCookies = outgoing.headers.getSetCookie?.() ?? [];
  if (setCookies.length) responseHeaders['set-cookie'] = setCookies;
  res.writeHead(outgoing.status, responseHeaders);
  res.end(Buffer.from(await outgoing.arrayBuffer()));
}

async function signIn(page, email) {
  await page.getByPlaceholder('nombre@ejemplo.com').fill(email);
  await page.locator('input[type=password]').fill(fixturePassword);
  await page.getByRole('button', { name: 'Ingresar al sistema' }).click();
}

const temp = await mkdtemp(path.join(tmpdir(), 'erp-f355-'));
let server;
let browser;
try {
  const cert = path.join(temp, 'cert.pem');
  const key = path.join(temp, 'key.pem');
  const generated = spawnSync('openssl', [
    'req', '-x509', '-newkey', 'rsa:2048', '-nodes',
    '-keyout', key, '-out', cert, '-days', '1',
    '-subj', '/CN=localhost',
    '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1',
  ], { stdio: 'pipe', timeout: 10000 });
  if (generated.status !== 0) throw new Error('Could not generate local QA certificate');

  server = https.createServer({ key: await readFile(key), cert: await readFile(cert) },
    (req, res) => {
      void handle(req, res).catch(() => {
        if (!res.headersSent) res.writeHead(500);
        res.end('QA server error');
      });
    });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, '127.0.0.1', resolve);
  });

  browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));

  await page.goto(origin + '/dashboard');
  await page.waitForURL('**/login', { timeout: 15000 });
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
  assert.equal(await page.evaluate(async () => (await fetch('/api/compras/po')).status), 401);

  await signIn(page, 'admin-f355@example.invalid');
  await page.waitForURL('**/dashboard', { timeout: 15000 });
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
  assert.equal(await page.evaluate(() => document.cookie.includes('__Host-erp_session')), false);
  const cookies = await context.cookies();
  const sessionCookie = cookies.find(c => c.name === '__Host-erp_session');
  const csrfCookie = cookies.find(c => c.name === '__Host-erp_csrf');
  assert.ok(sessionCookie?.httpOnly && sessionCookie.secure && sessionCookie.sameSite === 'Strict');
  assert.ok(csrfCookie?.httpOnly && csrfCookie.secure && csrfCookie.sameSite === 'Strict');
  assert.equal(await page.evaluate(async () => (await fetch('/api/compras/po')).status), 200);

  await page.reload();
  await page.waitForURL('**/dashboard', { timeout: 15000 });
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);

  const missingCsrf = await page.evaluate(async () => (await fetch('/api/ordenes', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
  })).status);
  assert.equal(missingCsrf, 403);

  const validCsrf = await page.evaluate(async () => {
    const data = await (await fetch('/api/auth/session')).json();
    return (await fetch('/api/ordenes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-ERP-CSRF': data.csrfToken },
      body: '{}',
    })).status;
  });
  assert.equal(validCsrf, 204);
  console.log('F355 Admin PASS: secure HttpOnly cookies, browser JWT absent, reload, CSRF');

  await page.getByRole('button', { name: 'Cerrar sesión' }).click();
  await page.waitForURL('**/login', { timeout: 15000 });
  assert.equal((await context.cookies()).some(c => c.name === '__Host-erp_session'), false);

  await signIn(page, 'seller-f355@example.invalid');
  await page.waitForURL('**/productos', { timeout: 15000 });
  await page.goto(origin + '/wms');
  await page.getByRole('heading', { name: 'Acceso no autorizado' }).waitFor({ timeout: 15000 });
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
  await page.getByRole('button', { name: 'Cerrar sesión' }).click();
  await page.waitForURL('**/login', { timeout: 15000 });

  await signIn(page, 'buyer-f355@example.invalid');
  await page.waitForURL('**/checkout', { timeout: 15000 });
  await page.goto(origin + '/dashboard');
  await page.getByRole('heading', { name: 'Acceso no autorizado' }).waitFor({ timeout: 15000 });
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);

  assert.equal(pageErrors.length, 0, 'Browser errors: ' + pageErrors.join(' | '));
  assert.ok(calls.some(c => c.path === '/api/auth/session' && c.hasBearer));
  assert.ok(calls.some(c => c.path === '/api/compras/po' && c.hasBearer));
  assert.equal(calls.some(c => c.path === '/api/auth/login' && c.hasBearer), false);
  console.log('F355_COOKIE_BROWSER_EVIDENCE=' + JSON.stringify({
    completed: true, roles: ['Admin', 'Vendedor', 'Comprador'],
    jwtInLocalStorage: false, httpOnlySecureCookies: true,
    csrfNegative: 403, csrfPositive: 204,
    mode: 'https-chromium-mocked-upstream-disposable-ci',
    realPayments: false, stagingTouched: false, productionTouched: false,
  }));
} finally {
  if (browser) await browser.close();
  if (server) await new Promise(resolve => server.close(resolve));
  await rm(temp, { recursive: true, force: true });
}
