const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

const root = path.resolve(__dirname, '..');

function loadClient(fetchImpl, storage) {
  const source = fs.readFileSync(path.join(root, 'src/api/client.ts'), 'utf8');
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    reportDiagnostics: true,
  });
  assert.deepEqual(compiled.diagnostics ?? [], []);
  const module = { exports: {} };
  vm.runInNewContext(compiled.outputText, {
    module, exports: module.exports,
    fetch: fetchImpl, localStorage: storage,
    window: { location: { pathname: '/dashboard', href: '' } },
    Date, JSON,
  }, { filename: 'src/api/client.ts' });
  return module.exports;
}

function fakeStorage() {
  const saved = new Map([['erp_token', 'legacy-private-jwt']]);
  const reads = [];
  return {
    getItem(key) { reads.push(key); return saved.get(key) ?? null; },
    removeItem(key) { saved.delete(key); },
    setItem(key, val) { saved.set(key, val); },
    reads, saved,
  };
}

test('cliente BFF opt-in no lee JWT del navegador y envía CSRF en las mutaciones', async () => {
  const calls = [];
  const storage = fakeStorage();
  const client = loadClient(async (url, opts) => {
    calls.push({ url, opts });
    return new Response(JSON.stringify({ ok: true }), {
      status: 200, headers: { 'Content-Type': 'application/json' },
    });
  }, storage);

  client.configureCookieBffMode(true);
  const nonce = 'a'.repeat(43);
  client.setCookieBffCsrfToken(nonce);
  assert.equal((await client.apiClient.post('/api/ordenes', { sku: 'fixture' })).ok, true);
  assert.equal((await client.apiClient.get('/api/ordenes')).ok, true);
  assert.deepEqual(storage.reads, []);
  assert.equal(calls[0].opts.credentials, 'same-origin');
  assert.equal(calls[0].opts.headers.Authorization, undefined);
  assert.equal(calls[0].opts.headers['X-ERP-CSRF'], nonce);
  assert.equal(calls[1].opts.headers['X-ERP-CSRF'], undefined);
  assert.equal(calls[1].opts.headers.Authorization, undefined);
});

test('BFF no deja CSRF activo después de desactivar el modo', async () => {
  const calls = [];
  const storage = fakeStorage();
  const client = loadClient(async (_url, opts) => {
    calls.push(opts);
    return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
  }, storage);

  client.configureCookieBffMode(true);
  client.setCookieBffCsrfToken('b'.repeat(43));
  await client.apiClient.post('/api/ordenes', {});
  client.configureCookieBffMode(false);
  await client.apiClient.post('/api/ordenes', {});
  assert.equal(calls[0].headers.Authorization, undefined);
  assert.equal(calls[1].headers.Authorization, 'Bearer legacy-private-jwt');
  assert.equal(calls[1].headers['X-ERP-CSRF'], undefined);
  assert.equal(storage.reads.includes('erp_token'), true);
});

test('logout en modo BFF envía CSRF y no un token Bearer', async () => {
  const requests = [];
  const client = loadClient(async (_url, opts) => {
    requests.push(opts);
    return new Response(null, { status: 204 });
  }, fakeStorage());
  client.configureCookieBffMode(true);
  client.setCookieBffCsrfToken('c'.repeat(43));
  await client.apiClient.post('/api/auth/logout');
  assert.equal(requests.length, 1);
  assert.equal(requests[0].headers['X-ERP-CSRF'], 'c'.repeat(43));
  assert.equal(requests[0].headers.Authorization, undefined);
  assert.equal(requests[0].credentials, 'same-origin');
});
