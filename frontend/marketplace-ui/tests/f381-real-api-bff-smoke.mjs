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

// F402: safe PostgreSQL sequence against known F105 fixture products.
// The F105 fixture is created ONLY in disposable GitHub CI PostgreSQL.
const fixtureA = 'f1050000-0000-4000-8000-000000000011';
const fixtureB = 'f1050000-0000-4000-8000-000000000012';
const fixtureGetBefore = await call(request('/api/productos/' + fixtureA, 'GET', {
  cookie: cookies.header,
}));
assert.equal(fixtureGetBefore.status, 200);
const fixtureBefore = await fixtureGetBefore.json();
assert.equal(fixtureBefore.codigoEAN, null, 'F402 must begin with NULL barcode');

const barcodePost = id => request('/api/productos/' + id + '/codigo-barras/asignar',
  'POST', { cookie: cookies.header, origin: browserOrigin,
    'x-erp-csrf': payload.csrfToken });
const issuedResponses = await Promise.all([
  call(barcodePost(fixtureA)),
  call(barcodePost(fixtureA)),
]);
for (const response of issuedResponses) {
  assert.equal(response.status, 200, 'Simultaneous Admin assignments must not fail');
}
const issued = await Promise.all(issuedResponses.map(r => r.json()));
assert.match(issued[0].codigoEAN, /^20[0-9]{11}$/);
assert.equal(issued[0].codigoEAN, issued[1].codigoEAN,
  'Concurrent requests on same product must return one persisted identifier');
const otherAssignment = await call(barcodePost(fixtureB));
assert.equal(otherAssignment.status, 200);
const otherBarcode = (await otherAssignment.json()).codigoEAN;
assert.match(otherBarcode, /^20[0-9]{11}$/);
assert.notEqual(otherBarcode, issued[0].codigoEAN,
  'Separate products must get unique sequences');
const idempotent = await call(barcodePost(fixtureA));
assert.equal(idempotent.status, 200);
assert.equal((await idempotent.json()).codigoEAN, issued[0].codigoEAN);
const searched = await call(request('/api/productos/por-codigo/' +
  issued[0].codigoEAN, 'GET', { cookie: cookies.header }));
assert.equal(searched.status, 200);
assert.equal((await searched.json()).id, fixtureA);
assert.equal((await call(request('/api/productos/por-codigo/' +
  issued[0].codigoEAN, 'GET', { cookie: sellerSession.cookies }))).status,
  404, 'Another vendor may not see barcode owner');

// F412: explicit label IDs must not bypass the seller ownership filter.
// The F105 product is owned by a different seller from F384's Seller account,
// and the same product must be visible to Admin for valid printing.
const selectedAdmin = await call(request(
  '/api/productos/etiquetas?ids=' + fixtureA, 'GET', {
    cookie: cookies.header,
  }));
assert.equal(selectedAdmin.status, 200, 'Admin may inspect the requested label');
const selectedAdminBody = await selectedAdmin.json();
assert.equal(selectedAdminBody.length, 1, 'Admin should see exactly one selected label');
assert.equal(selectedAdminBody[0].id, fixtureA);
assert.equal(selectedAdminBody[0].codigoEAN, issued[0].codigoEAN);
const selectedSeller = await call(request(
  '/api/productos/etiquetas?ids=' + fixtureA, 'GET', {
    cookie: sellerSession.cookies,
  }));
assert.equal(selectedSeller.status, 200, 'Seller may make a filtered selection');
assert.deepEqual(await selectedSeller.json(), [],
  'Seller may not extract the labels of another seller using their product IDs');
assert.equal((await call(request(
  '/api/productos/etiquetas?ids=' + fixtureA, 'GET', {
    cookie: buyerSession.cookies,
  }))).status, 403, 'Buyer cannot select internal product labels');
assert.equal((await call(request(
  '/api/productos/etiquetas?ids=not-a-uuid', 'GET', {
    cookie: cookies.header,
  }))).status, 400, 'Malformed label selections must fail validation');
console.log('F412_LABEL_SELECTION_ISOLATION_PASS ' + JSON.stringify({
  adminSelected: 1,
  sellerCrossOwnerSelected: 0,
  buyerDenied: true,
  invalidUuidDenied: true,
  database: 'CI disposable PostgreSQL',
  productionTouched: false
}));

assert.equal((await call(request('/api/productos/' + fixtureA +
  '/codigo-barras/asignar', 'POST', {
    cookie: sellerSession.cookies, origin: browserOrigin,
    'x-erp-csrf': sellerSession.csrf }))).status,
  403, 'Seller must not assign internal EAN');
assert.equal((await call(request('/api/productos/' + fixtureA +
  '/codigo-barras/asignar', 'POST', {
    cookie: buyerSession.cookies, origin: browserOrigin,
    'x-erp-csrf': buyerSession.csrf }))).status,
  403, 'Buyer must not assign internal EAN');

console.log('F402_REAL_DB_BARCODE_ASSIGNMENT_PASS ' + JSON.stringify({
  postgres: 'disposable localhost only',
  sourceEANsRewritten: false,
  sameProductConcurrent: 'one code',
  differentProducts: 'distinct codes',
  idempotent: true,
  sellerBuyerAssignment: 403,
  crossSellerRead: 404,
  productionTouched: false,
}));

// F399: full barcode-scanner cycle count against disposable PostgreSQL.
const scanBase = '/api/inventario/conteos-ciclicos/sesiones';
const scanListAdmin = await call(request(scanBase, 'GET', {cookie: cookies.header}));
assert.equal(scanListAdmin.status, 200, 'Admin may list sessions');
assert.ok(Array.isArray(await scanListAdmin.json()));
assert.equal((await call(request(scanBase, 'GET', {
  cookie: sellerSession.cookies
}))).status, 403, 'Seller must not see shared warehouse sessions');
assert.equal((await call(request(scanBase, 'GET', {
  cookie: buyerSession.cookies
}))).status, 403, 'Buyer must not see warehouse sessions');
assert.equal((await call(request(scanBase, 'GET'))).status, 401);

const f399Wms = await call(request('/api/wms/stocks?sku=F105-A-001', 'GET', {
  cookie: cookies.header,
}));
assert.equal(f399Wms.status, 200, 'Admin must resolve physical stock through WMS');
const f399Stocks = await f399Wms.json();
assert.ok(Array.isArray(f399Stocks));
const f399StockId = f399Stocks.find(s => s.productoId === fixtureA)?.id;
assert.match(f399StockId, /^[0-9a-f-]{36}$/i, 'F105 physical stock exists in temporary PostgreSQL');
const planScan = await call(request('/api/inventario/conteos-ciclicos', 'POST',
  adminMutatingHeaders, {stockFisicoId: f399StockId, observaciones:'F399 CI-only'}));
assert.equal(planScan.status, 200, 'Admin may plan a cycle count');
const startScan = await call(request(scanBase + '/planificada', 'POST',
  adminMutatingHeaders, {pasillo:null}));
assert.equal(startScan.status, 201, 'Admin may open a planned scan session');
const scanSession = await startScan.json();
assert.match(scanSession.id, /^[0-9a-f-]{36}$/i);

// F404: two simultaneous network retries of an out-of-place product must
// create exactly ONE audit finding, even across separate HTTP requests.
const f404OutBody = {lecturaId: randomUUID(), codigo:otherBarcode, cantidad:3};
const f404Path = scanBase + '/' + scanSession.id + '/lecturas';
const f404Outside = await Promise.all([
  call(request(f404Path, 'POST', adminMutatingHeaders, f404OutBody)),
  call(request(f404Path, 'POST', adminMutatingHeaders, f404OutBody)),
]);
for (const response of f404Outside) {
  assert.equal(response.status, 200, 'Outside-location retry must be an HTTP success');
  assert.equal((await response.json()).resultado, 'FueraDeLugar');
}
const f404Changed = await call(request(f404Path, 'POST', adminMutatingHeaders,
  {...f404OutBody, cantidad:7}));
assert.equal(f404Changed.status, 200);
const f404ChangedBody = await f404Changed.json();
assert.equal(f404ChangedBody.resultado, 'Rechazado',
  'The same read UUID with a different quantity must fail closed');
assert.equal(f404ChangedBody.sku ?? null, null, 'No stored product data may leak from a reused UUID');
const f404OutsideSummary = await (await call(request(
  scanBase + '/' + scanSession.id, 'GET', {cookie:cookies.header}))).json();
assert.equal(f404OutsideSummary.fueraDeLugar.length, 1,
  'A double-submit must never create duplicate inventory findings');
const readBody = {lecturaId: randomUUID(), codigo:issued[0].codigoEAN, cantidad:20};
const scanResponses = await Promise.all([
  call(request(scanBase + '/' + scanSession.id + '/lecturas', 'POST',
    adminMutatingHeaders, readBody)),
  call(request(scanBase + '/' + scanSession.id + '/lecturas', 'POST',
    adminMutatingHeaders, readBody))
]);
for (const response of scanResponses) {
  assert.equal(response.status, 200, 'Concurrent repeated read must not fail');
  assert.equal((await response.json()).resultado, 'Coincide',
    'Both identical read IDs must reconstruct the same result');
}
const scanSummaryResp = await call(request(scanBase + '/' + scanSession.id, 'GET',
  {cookie: cookies.header}));
assert.equal(scanSummaryResp.status, 200);
const scanSummary = await scanSummaryResp.json();
assert.equal(scanSummary.contados.length, 1, 'Exactly one product counted');
assert.equal(scanSummary.sesion.estado, 'Abierta');
assert.equal(Object.hasOwn(scanSummary, 'cantidadSistema'), false);
const closeScan = await call(request(scanBase + '/' + scanSession.id + '/cerrar',
  'POST', adminMutatingHeaders, {decisiones:[]}));
assert.equal(closeScan.status, 200, 'Planned count with no missing items may close');
const closeBody = await closeScan.json();
assert.equal(closeBody.sesion.estado, 'Cerrada');
assert.equal((await call(request(scanBase + '/' + scanSession.id + '/lecturas',
  'POST', adminMutatingHeaders, {...readBody,lecturaId:randomUUID()}))).status, 200,
'Closed session returns a business result, not 500');

console.log('F399_REAL_PG_SCANNER_PASS ' + JSON.stringify({
  postgresql: 'disposable CI only',
  adminCanPlanAndScan: true,
  sellerAndBuyerDenied: true,
  concurrentIdenticalRead: 'same result',
  concurrentOutsideLocation: 'one finding',
  counted: 1,
  closed: true,
  productionTouched: false,
}));

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
