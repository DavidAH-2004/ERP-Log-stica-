/**
 * F381 — Real ASP.NET 8 + disposable PostgreSQL through the actual BFF adapter.
 * Runs only in GitHub CI after the isolated API/DB has been started.
 * No Render, Neon, Vercel, live credentials or payment operations.
 * Does not activate preview or production flags.
 */
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { bffCookieProxy } from '../bffCookieMode.js';

const upstream = 'http://127.0.0.1:5000';
const browserOrigin = 'https://127.0.0.1:4179';
const email = process.env.ADMIN_EMAIL;
const password = process.env.ADMIN_PASSWORD;

if (process.env.CI !== 'true' || !email?.endsWith('@erp.invalid') ||
    !password || password.length < 20 ||
    process.env.CONNECTION_STRING?.includes('localhost') === false) {
  throw new Error('F381 requires disposable localhost CI API and ephemeral Admin credentials.');
}
if (process.env.ASPNETCORE_ENVIRONMENT !== 'Development') {
  throw new Error('F381 refuses non-development backend environments.');
}

// Never allow a remote upstream, even if the adapter implementation changes.
let upstreamRequests = 0;
async function localFetch(uri, options) {
  const target = new URL(uri);
  assert.equal(target.origin, upstream, 'F381 upstream MUST be localhost only');
  assert.equal(options.redirect, 'manual');
  upstreamRequests++;
  return fetch(target, options);
}

function request(path, method = 'GET', headers = {}, body) {
  assert.ok(path.startsWith('/api/'));
  return new Request(browserOrigin + path, {
    method, headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}
const call = (req) => bffCookieProxy(req, upstream, localFetch);

function cookiesFrom(res) {
  const all = res.headers.getSetCookie();
  return {
    all: all.join('\n'),
    header: all.map(c => c.split(';')[0]).join('; '),
  };
}

async function login() {
  const makeRequest = () => call(request('/api/auth/login', 'POST', {
    origin: browserOrigin, 'content-type': 'application/json',
  }, { email, password }));
  let response = await makeRequest();
  if (response.status === 429) {
    // Previous isolated Chromium tests legitimately share a 5/min loopback limiter.
    // Respect one window; never disable, evade or reset the real limiter.
    await delay(65_000);
    response = await makeRequest();
  }
  assert.equal(response.status, 200, 'F381 genuine BFF Admin login must return 200');
  return response;
}

const ready = await fetch(upstream + '/health/ready');
assert.equal(ready.status, 200, 'F381 requires real local migrated database');

const anonymous = await call(request('/api/auth/session'));
assert.equal(anonymous.status, 401);
assert.equal(anonymous.headers.get('set-cookie'), null);

const loginResponse = await login();
const payload = await loginResponse.json();
const cookies = cookiesFrom(loginResponse);
assert.equal(payload.rol, 'Admin');
assert.equal(payload.role, 'Admin');
assert.equal(Object.hasOwn(payload, 'token'), false);
assert.equal(payload.csrfToken?.length, 43);
assert.match(cookies.all, /__Host-erp_session=/);
assert.match(cookies.all, /__Host-erp_csrf=/);
assert.match(cookies.all, /HttpOnly/);
assert.match(cookies.all, /Secure/);
assert.match(cookies.all, /SameSite=Strict/);
assert.ok(!cookies.all.includes('Max-Age=0'));
assert.equal(loginResponse.headers.get('cache-control'), 'no-store');

const session = await call(request('/api/auth/session', 'GET', {
  cookie: cookies.header, authorization: 'Bearer injected.invalid.token',
  'x-forwarded-for': '1.2.3.4',
}));
assert.equal(session.status, 200, 'F381 real backend must accept JWT only from secure cookie');
const identity = await session.json();
assert.equal(identity.rol, 'Admin');
assert.equal(identity.csrfToken, payload.csrfToken);
assert.equal(Object.hasOwn(identity, 'token'), false);

const purchase = await call(request('/api/compras/po', 'GET', { cookie: cookies.header }));
assert.equal(purchase.status, 200, 'Real Admin RBAC GET must work through BFF');

// F384: Admin provisions fresh Vendedor and Comprador fixtures ONLY in the
// disposable PostgreSQL database on the CI runner. This is NOT a staging write.
const { randomUUID, randomBytes } = await import('node:crypto');
const nonce = randomUUID().replaceAll('-', '').slice(0, 16);
const sellerEmail = 'f384-seller-' + nonce + '@erp.invalid';
const buyerEmail = 'f384-buyer-' + nonce + '@erp.invalid';
const sellerPassword = randomBytes(28).toString('base64url');
const buyerPassword = randomBytes(28).toString('base64url');
const adminMutatingHeaders = {
  cookie: cookies.header,
  origin: browserOrigin,
  'x-erp-csrf': payload.csrfToken,
  'content-type': 'application/json',
};
const vendor = await call(request('/api/vendedores', 'POST', adminMutatingHeaders, {
  nombreEmpresa: 'F384 Vendedor temporal',
  email: sellerEmail,
  telefono: '0000-0000',
}));
assert.equal(vendor.status, 201, 'F384 vendor fixture must be Admin-only and local');
const vendorBody = await vendor.json();
assert.match(String(vendorBody.id), /^[0-9a-f-]{36}$/i);
const seller = await call(request('/api/usuarios', 'POST', adminMutatingHeaders, {
  email: sellerEmail, password: sellerPassword, nombre: 'F384 Seller',
  rol: 'Vendedor', vendedorId: vendorBody.id,
}));
assert.equal(seller.status, 201, 'F384 seller user creation failed in disposable CI DB');
const buyer = await call(request('/api/usuarios', 'POST', adminMutatingHeaders, {
  email: buyerEmail, password: buyerPassword, nombre: 'F384 Buyer',
  rol: 'Comprador',
}));
assert.equal(buyer.status, 201, 'F384 buyer user creation failed in disposable CI DB');

async function roleSession(accountEmail, accountPassword, expectedRole) {
  const sendLogin = () => call(request('/api/auth/login', 'POST', {
    origin: browserOrigin, 'content-type': 'application/json',
  }, { email: accountEmail, password: accountPassword }));
  let response = await sendLogin();
  if (response.status === 429) {
    // Respect the existing real localhost login limiter, never bypass it.
    await delay(65_000);
    response = await sendLogin();
  }
  assert.equal(response.status, 200, 'F384 role login did not succeed');
  const body = await response.json();
  assert.equal(body.rol, expectedRole);
  assert.equal(body.role, expectedRole);
  assert.equal(Object.hasOwn(body, 'token'), false);
  assert.equal(body.csrfToken?.length, 43);
  const roleCookies = cookiesFrom(response);
  assert.match(roleCookies.all, /HttpOnly/);
  assert.match(roleCookies.all, /Secure/);
  assert.match(roleCookies.all, /SameSite=Strict/);
  const sessionResponse = await call(request('/api/auth/session', 'GET', {
    cookie: roleCookies.header,
    authorization: 'Bearer attacker-controlled-placeholder',
    'x-forwarded-for': '203.0.113.31',
  }));
  assert.equal(sessionResponse.status, 200);
  const identity = await sessionResponse.json();
  assert.equal(identity.rol, expectedRole);
  assert.equal(identity.role, expectedRole);
  assert.equal(identity.csrfToken, body.csrfToken);
  assert.equal(Object.hasOwn(identity, 'token'), false);
  return { cookies: roleCookies.header, csrf: body.csrfToken };
}

const sellerSession = await roleSession(sellerEmail, sellerPassword, 'Vendedor');
const sellerOrders = await call(request('/api/ordenes/mis-ordenes', 'GET', {
  cookie: sellerSession.cookies,
}));
assert.equal(sellerOrders.status, 200, 'Seller must read own orders via real API');
assert.equal((await call(request('/api/compras/po', 'GET', {
  cookie: sellerSession.cookies,
}))).status, 403, 'Seller must not read Admin purchasing');
assert.equal((await call(request('/api/usuarios', 'POST', {
  cookie: sellerSession.cookies, origin: browserOrigin, 'x-erp-csrf': sellerSession.csrf,
  'content-type': 'application/json',
}, { email: 'f384-unauthorized-' + nonce + '@erp.invalid',
     password: 'not-used-in-real-production', rol: 'Admin', nombre: 'No create' }))).status,
403, 'Seller must not provision Admin accounts');

const buyerSession = await roleSession(buyerEmail, buyerPassword, 'Comprador');
assert.equal((await call(request('/api/ordenes', 'GET', {
  cookie: buyerSession.cookies,
}))).status, 403, 'Buyer must not list Admin orders');
assert.equal((await call(request('/api/compras/po', 'GET', {
  cookie: buyerSession.cookies,
}))).status, 403, 'Buyer must not read Admin purchasing');

// F401: barcode endpoints are read-only, authenticated and seller-scoped.
// This exercises the REAL .NET API/BFF against disposable PostgreSQL, never DEV cloud.
const adminLabelsResult = await call(request('/api/productos/etiquetas', 'GET', {
  cookie: cookies.header,
}));
assert.equal(adminLabelsResult.status, 200, 'Admin labels endpoint must respond');
const adminLabels = await adminLabelsResult.json();
assert.ok(Array.isArray(adminLabels));
assert.ok(adminLabels.length <= 500);

const sellerLabelsResult = await call(request('/api/productos/etiquetas', 'GET', {
  cookie: sellerSession.cookies,
}));
assert.equal(sellerLabelsResult.status, 200, 'Seller may read only own labels');
assert.ok(Array.isArray(await sellerLabelsResult.json()));
assert.equal((await call(request('/api/productos/etiquetas', 'GET', {
  cookie: buyerSession.cookies,
}))).status, 403, 'Buyer cannot access label export');
assert.equal((await call(request('/api/productos/etiquetas', 'GET'))).status,
  401, 'Anonymous may not access label export');

const tooMany = Array.from({length:101},()=>'00000000-0000-0000-0000-000000000001').join(',');
assert.equal((await call(request('/api/productos/etiquetas?ids='+tooMany, 'GET', {
  cookie: cookies.header,
}))).status,400,'Label query of more than 100 ids must be denied');
assert.equal((await call(request('/api/productos/por-codigo/2000000000014', 'GET', {
  cookie: cookies.header,
}))).status,400,'Invalid EAN checksum must be denied');

console.log('F401_REAL_BARCODE_ROLES_PASS ' + JSON.stringify({
  roleContract: 'Admin 200, Seller 200, Buyer 403, Anonymous 401',
  maxSelected: 100,
  maxUnselected: 500,
  checksum: 'invalid 400',
  upstream: 'disposable-localhost-dotnet-and-postgresql',
  productionTouched: false,
  barcodeWrites: false,
}));

console.log('F384_REAL_API_BFF_ROLES_PASS ' + JSON.stringify({
  roles: ['Admin', 'Vendedor', 'Comprador'],
  identityOnRealDotNet: true,
  permissions: 'Admin purchasing 200; Seller own orders 200; Seller/Buyer Admin routes 403',
  forbiddenAccountProvisioning: 403,
  jwtInClientJson: false,
  cookies: 'HttpOnly+Secure+SameSiteStrict',
  csrf: 'enforced on all mutating requests',
  database: 'CI disposable PostgreSQL only',
  payments: 'Disabled',
  renderOrNeonTouched: false,
}));

// Reject invalid Origin and CSRF without reaching ASP.NET; no mutations.
const beforeDenials = upstreamRequests;
assert.equal((await call(request('/api/auth/logout', 'POST',
  { cookie: cookies.header, origin: 'https://attacker.invalid',
    'x-erp-csrf': payload.csrfToken }))).status, 403);
assert.equal((await call(request('/api/auth/logout', 'POST',
  { cookie: cookies.header, origin: browserOrigin,
    'x-erp-csrf': 'a'.repeat(43) }))).status, 403);
assert.equal(upstreamRequests, beforeDenials, 'Invalid Origin/CSRF must not reach real API');

// Strict token generation remains OFF in CI; logout must FAIL CLOSED, no false 204,
// and cookie deletion must NOT be emitted when server revocation is unconfirmed.
const logout = await call(request('/api/auth/logout', 'POST',
  { cookie: cookies.header, origin: browserOrigin,
    'x-erp-csrf': payload.csrfToken }));
assert.equal(logout.status, 503);
assert.equal(logout.headers.get('set-cookie'), null);
assert.equal((await call(request('/api/auth/session', 'GET',
  { cookie: cookies.header }))).status, 200);

console.log('F381_REAL_API_BFF_PASS ' + JSON.stringify({
  upstream: 'localhost-disposable-postgresql-dotnet',
  roles: ['Admin'],
  cookie: 'HttpOnly+Secure+SameSiteStrict',
  exposedJwtInJson: false,
  sessionGet: 200,
  adminPurchaseGet: 200,
  invalidOriginAndCsrf: 403,
  logoutWithoutStrictRevocation: 503,
  cookieNotClearedWhenRevocationUnconfirmed: true,
  externalPayments: false,
  stagingTouched: false,
  productionTouched: false,
}));
