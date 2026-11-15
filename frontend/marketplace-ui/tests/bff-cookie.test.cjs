const test = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const previewOrigin = 'https://erp-preview.example';
const stagingOrigin = 'https://marketplaceerp-api-staging.onrender.com';
const jwt = 'e30.' + Buffer.from(JSON.stringify({
  role: 'Admin', iat: Math.floor(Date.now() / 1000),
  exp: Math.floor(Date.now() / 1000) + 900,
})).toString('base64url') + '.signature';

async function withBff(fakeFetch, action, environment = 'preview', flag = 'enabled') {
  const previous = {
    fetch: globalThis.fetch,
    vercel: process.env.VERCEL_ENV,
    origin: process.env.API_STAGING_ORIGIN,
    mode: process.env.ERP_COOKIE_BFF_MODE,
  };
  globalThis.fetch = fakeFetch;
  process.env.VERCEL_ENV = environment;
  process.env.API_STAGING_ORIGIN = stagingOrigin;
  if (flag === undefined) delete process.env.ERP_COOKIE_BFF_MODE;
  else process.env.ERP_COOKIE_BFF_MODE = flag;
  try {
    const module = await import(pathToFileURL(path.join(root, 'middleware.js')).href);
    await action(module.default);
  } finally {
    globalThis.fetch = previous.fetch;
    for (const [key, value] of [['VERCEL_ENV', previous.vercel],
      ['API_STAGING_ORIGIN', previous.origin],
      ['ERP_COOKIE_BFF_MODE', previous.mode]]) {
      if (value === undefined) delete process.env[key];
      else process.env[key] = value;
    }
  }
}

function request(uri, method, headers = {}, body) {
  return new Request(previewOrigin + uri, {
    method, headers, body: body === undefined ? undefined : JSON.stringify(body),
  });
}

function cookiesFrom(response) {
  const headers = response.headers.getSetCookie?.() ?? [response.headers.get('set-cookie')];
  const cookieHeader = headers.filter(Boolean).map(item => item.split(';')[0]).join('; ');
  return { all: headers.join('\n'), cookieHeader };
}

function goodLogin(expiresAt = new Date(Date.now() + 600_000).toISOString()) {
  return new Response(JSON.stringify({
    token: jwt, role: 'Admin', rol: 'Admin',
    usuario: 'Administrador', expiresAt,
  }), { status: 200, headers: { 'content-type': 'application/json', 'set-cookie': 'backend=must-not-leak' } });
}

test('BFF preview opt-in: login oculta JWT, emite dos cookies HttpOnly y token CSRF', async () => {
  const calls = [];
  await withBff(async (url, options) => {
    calls.push({ url: url.toString(), options });
    return goodLogin();
  }, async proxy => {
    const res = await proxy(request('/api/auth/login', 'POST',
      { origin: previewOrigin, 'content-type': 'application/json', cookie: 'legacy=browser' },
      { email: 'admin@example.test', password: 'local-test-only' }));
    assert.equal(res.status, 200);
    const data = await res.json();
    assert.equal(data.usuario, 'Administrador');
    assert.equal(data.role, 'Admin');
    assert.equal(data.rol, 'Admin');
    assert.equal(typeof data.csrfToken, 'string');
    assert.equal(data.csrfToken.length, 43);
    assert.equal(Object.hasOwn(data, 'token'), false);
    const c = cookiesFrom(res);
    assert.match(c.all, /__Host-erp_session=/);
    assert.match(c.all, /__Host-erp_csrf=/);
    assert.match(c.all, /HttpOnly/);
    assert.match(c.all, /Secure/);
    assert.match(c.all, /SameSite=Strict/);
    assert.doesNotMatch(c.all, /backend=must-not-leak/);
    assert.equal(res.headers.get('cache-control'), 'no-store');
    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, stagingOrigin + '/api/auth/login');
    assert.equal(calls[0].options.headers.get('cookie'), null);
    assert.equal(calls[0].options.headers.get('authorization'), null);
  });
});

test('BFF session valida bearer contra backend y devuelve identidad sin JWT', async () => {
  let loginCookies = null;
  let csrfToken = null;
  const calls = [];
  await withBff(async (url, options) => {
    calls.push({ url: url.toString(), options });
    if (url.pathname === '/api/auth/login') return goodLogin();
    if (url.pathname === '/api/auth/session') {
      return new Response(JSON.stringify({
        usuario: 'Administrador', rol: 'Admin', role: 'Admin', email: 'admin@example.test',
        expiresAt: new Date(Date.now() + 200_000).toISOString(),
      }), { headers: { 'Content-Type': 'application/json' } });
    }
    return new Response(null, { status: 204 });
  }, async proxy => {
    const login = await proxy(request('/api/auth/login', 'POST',
      { origin: previewOrigin, 'content-type': 'application/json' }, { email: 'a', password: 'b' }));
    loginCookies = cookiesFrom(login).cookieHeader;
    csrfToken = (await login.json()).csrfToken;
    const session = await proxy(request('/api/auth/session', 'GET', { cookie: loginCookies }));
    const data = await session.json();
    assert.equal(session.status, 200);
    assert.equal(data.rol, 'Admin');
    assert.equal(data.csrfToken, csrfToken);
    assert.equal(Object.hasOwn(data, 'token'), false);
    assert.equal(calls[1].url, stagingOrigin + '/api/auth/session');
    assert.equal(calls[1].options.headers.get('authorization'), 'Bearer ' + jwt);
    assert.equal(calls[1].options.headers.get('cookie'), null);
    assert.equal(calls[1].options.headers.get('x-erp-csrf'), null);
  });
});

test('BFF niega operaciones cruzadas y peticiones sin CSRF antes de contactar Render', async () => {
  let hits = 0;
  await withBff(async () => { hits++; return new Response(null, { status: 204 }); }, async proxy => {
    assert.equal((await proxy(request('/api/ordenes', 'POST',
      { origin: 'https://evil.example', 'x-erp-csrf': 'x' }, {}))).status, 403);
    assert.equal((await proxy(request('/api/ordenes', 'POST',
      { origin: previewOrigin }, {}))).status, 401);
    assert.equal((await proxy(request('/api/ordenes', 'POST',
      { origin: previewOrigin, cookie: '__Host-erp_session=' + jwt }, {}))).status, 403);
    assert.equal((await proxy(request('/api/ordenes', 'POST',
      { cookie: '__Host-erp_session=' + jwt }, {}))).status, 403);
    assert.equal(hits, 0);
  });
});

test('BFF requiere CSRF válido y reemplaza Authorization inyectado por JWT del cookie', async () => {
  const calls = [];
  await withBff(async (url, options) => {
    calls.push({ url: url.toString(), options });
    return new Response(null, { status: 204 });
  }, async proxy => {
    const csrfToken = 'a'.repeat(43);
    const c = '__Host-erp_session=' + jwt + '; __Host-erp_csrf=' + csrfToken;
    const res = await proxy(request('/api/ordenes?limit=5', 'POST', {
      origin: previewOrigin, cookie: c,
      authorization: 'Bearer injected-jwt', 'x-erp-csrf': csrfToken,
      'x-forwarded-for': '127.0.0.1', 'content-type': 'application/json',
    }, { sku: 'synthetic' }));
    assert.equal(res.status, 204);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, stagingOrigin + '/api/ordenes?limit=5');
    assert.equal(calls[0].options.headers.get('authorization'), 'Bearer ' + jwt);
    assert.equal(calls[0].options.headers.get('cookie'), null);
    assert.equal(calls[0].options.headers.get('x-forwarded-for'), null);
    assert.equal(calls[0].options.headers.get('x-erp-csrf'), null);
  });
});

test('BFF logout solo con Origin + CSRF y elimina ambas cookies', async () => {
  await withBff(async () => { throw new Error('logout should not contact upstream'); },
    async proxy => {
      const csrf = 'b'.repeat(43);
      const c = '__Host-erp_session=' + jwt + '; __Host-erp_csrf=' + csrf;
      const bad = await proxy(request('/api/auth/logout', 'POST',
        { cookie: c, origin: previewOrigin, 'x-erp-csrf': 'wrong' }));
      assert.equal(bad.status, 403);
      const good = await proxy(request('/api/auth/logout', 'POST',
        { cookie: c, origin: previewOrigin, 'x-erp-csrf': csrf }));
      assert.equal(good.status, 204);
      const headers = cookiesFrom(good).all;
      assert.match(headers, /__Host-erp_session=;.*Max-Age=0/);
      assert.match(headers, /__Host-erp_csrf=;.*Max-Age=0/);
    });
});

test('BFF falla cerrado con login incompleto, origen faltante y sesión inválida', async () => {
  await withBff(async () => new Response(JSON.stringify({ role: 'Admin', usuario: 'A' })),
    async proxy => {
      const bad = await proxy(request('/api/auth/login', 'POST',
        { origin: previewOrigin, 'content-type': 'application/json' }, {}));
      assert.equal(bad.status, 502);
      assert.equal(bad.headers.get('set-cookie'), null);
      assert.equal((await proxy(request('/api/auth/login', 'POST',
        { 'content-type': 'application/json' }, {}))).status, 403);
      assert.equal((await proxy(request('/api/auth/session', 'GET'))).status, 401);
    });
});

test('BFF opt-in cannot activate on Vercel Production (legacy route stays unchanged)', async () => {
  const calls = [];
  await withBff(async (url, options) => {
    calls.push(url.toString());
    return goodLogin();
  }, async proxy => {
    const res = await proxy(new Request('https://erp-log-stica.vercel.app/api/auth/login', {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ email: 'fixture', password: 'fixture' }),
    }));
    assert.equal(res.status, 200);
    const data = await res.json();
    assert.equal(data.token, jwt);
    assert.equal(res.headers.get('set-cookie'), null);
    assert.equal(calls[0], 'https://marketplaceerp-api.onrender.com/api/auth/login');
  }, 'production', 'enabled');
});


test('BFF rehidratado no extiende el TTL más allá de iat + 15 minutos', async () => {
  const now = Math.floor(Date.now() / 1000);
  const oldJwt = 'e30.' + Buffer.from(JSON.stringify({
    role: 'Admin', iat: now - 600, exp: now + 1800,
  })).toString('base64url') + '.signature';
  const csrf = 'c'.repeat(43);
  await withBff(async (_url, options) => {
    assert.equal(options.headers.get('authorization'), 'Bearer ' + oldJwt);
    return new Response(JSON.stringify({
      usuario: 'Administrador', rol: 'Admin', role: 'Admin',
      expiresAt: new Date((now + 1800) * 1000).toISOString(),
    }), { headers: { 'Content-Type': 'application/json' } });
  }, async proxy => {
    const response = await proxy(request('/api/auth/session', 'GET', {
      cookie: '__Host-erp_session=' + oldJwt + '; __Host-erp_csrf=' + csrf,
    }));
    assert.equal(response.status, 200);
    const data = await response.json();
    const remainingMs = Date.parse(data.expiresAt) - Date.now();
    assert.ok(remainingMs > 230_000 && remainingMs <= 300_000,
      'A 10-minute-old JWT has at most 5 remaining BFF minutes');
    assert.equal(Object.hasOwn(data, 'token'), false);
  });
});
