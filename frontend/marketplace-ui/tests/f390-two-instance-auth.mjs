/**
 * F390: two REAL ASP.NET instances (ports 5001/5002) against the SAME
 * disposable CI PostgreSQL. Validate strict JWT revocation across instances
 * and distributed, atomic per-account login throttling without Render/Neon.
 *
 * Run ONLY in CI with Auth flags true on BOTH API instances and all hosts
 * constrained to loopback. Payments remain disabled.
 */
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { bffCookieProxy } from '../bffCookieMode.js';

const origins = ['http://127.0.0.1:5001', 'http://127.0.0.1:5002'];
const site = 'https://127.0.0.1:4180';
const admin = process.env.ADMIN_EMAIL;
const password = process.env.ADMIN_PASSWORD;

if (process.env.CI !== 'true' ||
    process.env.ASPNETCORE_ENVIRONMENT !== 'Development' ||
    process.env.Auth__EnableSessionVersionCheck !== 'true' ||
    process.env.Auth__EnableDistributedAccountLoginThrottle !== 'true' ||
    !process.env.CONNECTION_STRING?.includes('Host=localhost;') ||
    !admin?.endsWith('@erp.invalid') || !password || password.length < 20 ||
    process.env.Payments__Provider !== 'Disabled') {
  throw new Error('F390 requires disposable local CI, both strict flags and disabled payments');
}

function request(path, method = 'GET', headers = {}, data) {
  return new Request(site + path, {
    method, headers,
    body: data === undefined ? undefined : JSON.stringify(data),
  });
}
let totalUpstream = 0;
const adapter = n => async incoming => bffCookieProxy(incoming, origins[n], (uri, opts) => {
  const target = new URL(uri);
  assert.equal(target.origin, origins[n], 'No non-loopback calls in F390');
  totalUpstream++;
  return fetch(target, opts);
});

for (const origin of origins) {
  assert.equal((await fetch(origin + '/health/ready')).status, 200);
}
const apiA = adapter(0);
const apiB = adapter(1);
const login = await apiA(request('/api/auth/login', 'POST', {
  origin: site, 'content-type': 'application/json',
}, { email: admin, password }));
assert.equal(login.status, 200, 'F390 strict login on first instance');
const result = await login.json();
assert.equal(result.rol, 'Admin');
assert.ok(!Object.hasOwn(result, 'token'));
const rawCookies = login.headers.getSetCookie();
const cookieHeader = rawCookies.map(c => c.split(';')[0]).join('; ');
assert.match(rawCookies.join('\n'), /HttpOnly/);
assert.match(rawCookies.join('\n'), /Secure/);
assert.match(rawCookies.join('\n'), /SameSite=Strict/);
assert.equal(result.csrfToken?.length, 43);

const before = await apiB(request('/api/auth/session', 'GET', { cookie: cookieHeader }));
assert.equal(before.status, 200, 'JWT issued on API A must verify on API B');
const identity = await before.json();
assert.equal(identity.rol, 'Admin');
assert.equal(Object.hasOwn(identity, 'token'), false);

const revoke = await apiB(request('/api/auth/logout', 'POST', {
  cookie: cookieHeader, origin: site, 'x-erp-csrf': result.csrfToken,
}));
assert.equal(revoke.status, 204, 'API B must commit session-generation increment');
const cleared = revoke.headers.getSetCookie().join('\n');
assert.match(cleared, /__Host-erp_session=;.*Max-Age=0/);
assert.match(cleared, /__Host-erp_csrf=;.*Max-Age=0/);
assert.equal((await apiA(request('/api/auth/session', 'GET',
  { cookie: cookieHeader }))).status, 401,
'API A must reject JWT revoked by B');
assert.equal((await apiB(request('/api/auth/session', 'GET',
  { cookie: cookieHeader }))).status, 401,
'API B must reject JWT revoked on itself');

// Account HMAC stored in core.login_throttle shared by both API processes.
// 3 requests per process remain below their separate per-IP 5/min limit.
// The 6th attempt must be rejected by the DATABASE-wide account counter.
const badEmail = 'f390-invalid-' + randomUUID() + '@erp.invalid';
const statuses = [];
for (let i = 0; i < 6; i++) {
  const sender = i < 3 ? apiA : apiB;
  const response = await sender(request('/api/auth/login', 'POST', {
    origin: site, 'content-type': 'application/json',
  }, { email: badEmail, password: 'f390-invalid-not-a-real-user' }));
  statuses.push(response.status);
}
assert.deepEqual(statuses, [401, 401, 401, 401, 401, 429],
  'Shared PostgreSQL account throttle must enforce 5/min across instances');
assert.ok(totalUpstream >= 10);
console.log('F390_TWO_INSTANCE_SECURITY_PASS ' + JSON.stringify({
  apiProcesses: 2,
  database: 'disposable localhost PostgreSQL',
  strictJwt: 'issued A, accepted B, revoked B, rejected A/B',
  bffLogout: '204+cookie-clear-only-after-confirmed-revocation',
  distributedAccountLimiter: '5 invalid 401 then sixth 429, split 3+3 across A/B',
  ipLimiter: 'per-instance unchanged',
  payments: 'Disabled',
  productionTouched: false, developmentCloudTouched: false,
}));
