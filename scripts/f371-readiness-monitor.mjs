// F371: read-only health/ready probe, restricted to Render Development.
// No credentials, no mutable endpoints, no production target and no response body logging.
import assert from 'node:assert/strict';

const ORIGIN = 'https://marketplaceerp-api-staging.onrender.com';
const ENDPOINTS = ['/health', '/health/ready'];
const TIMEOUT_MS = 30_000;
const SLOW_MS = 15_000;

async function one(fetcher, path) {
  const started = performance.now();
  try {
    const response = await fetcher(ORIGIN + path, {
      method: 'GET',
      redirect: 'error',
      cache: 'no-store',
      signal: AbortSignal.timeout(TIMEOUT_MS),
      headers: { Accept: 'application/json' },
    });
    return { path, status: response.status, elapsed_ms: Math.round(performance.now() - started), ok: response.status === 200 };
  } catch (error) {
    // Never log URLs, headers or arbitrary provider error messages.
    return { path, status: 0, elapsed_ms: Math.round(performance.now() - started), ok: false, failure: error?.name === 'TimeoutError' ? 'timeout' : 'network' };
  }
}

async function probe(fetcher, samples = 2) {
  const rows = [];
  for (let i = 0; i < samples; i++) {
    for (const path of ENDPOINTS) rows.push(await one(fetcher, path));
  }
  const latency = rows.map(x => x.elapsed_ms).sort((a,b) => a-b);
  const p95 = latency[Math.max(0, Math.ceil(latency.length * 0.95) - 1)];
  return { rows, p95_ms: p95, healthy: rows.every(x => x.ok), slow: rows.some(x => x.elapsed_ms > SLOW_MS) };
}

async function selfTest() {
  const success = await probe(async (_url, opts) => {
    assert.equal(opts.method, 'GET');
    assert.equal(opts.redirect, 'error');
    assert.equal(opts.headers.Accept, 'application/json');
    assert.ok(ENDPOINTS.some(path => _url === ORIGIN + path));
    return { status: 200 };
  });
  assert.equal(success.healthy, true);
  assert.equal(success.rows.length, 4);
  const unavailable = await probe(async (url) => ({ status: url.endsWith('/ready') ? 503 : 200 }), 1);
  assert.equal(unavailable.healthy, false);
  assert.equal(unavailable.rows[1].status, 503);
  const transport = await probe(async () => { throw new Error('private-token-DO-NOT-LOG'); }, 1);
  assert.equal(transport.healthy, false);
  assert.ok(transport.rows.every(x => x.failure === 'network'));
  assert.ok(!JSON.stringify(transport).includes('private-token'));
  process.stdout.write('F371_SELF_TEST_PASS (200, 503, network; no secrets)\n');
}

if (process.argv.slice(2).join(' ') === '--self-test') {
  await selfTest();
} else if (process.argv.length === 2) {
  const result = await probe(fetch);
  process.stdout.write(JSON.stringify({ service: 'render-development', ...result }) + '\n');
  if (!result.healthy || result.slow) process.exitCode = 1;
} else {
  process.stderr.write('Usage: node scripts/f371-readiness-monitor.mjs [--self-test]\n');
  process.exitCode = 2;
}
