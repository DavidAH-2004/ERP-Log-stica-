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
