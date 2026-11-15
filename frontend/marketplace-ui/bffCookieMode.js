// F353 — Opt-in BFF session prototype. Disabled by default, preview-only.
// The browser must never receive the Render JWT in cookie mode.
// This is NOT a production release gate: revocation/rotation and browser E2E remain pending.

const SESSION_COOKIE = '__Host-erp_session';
const CSRF_COOKIE = '__Host-erp_csrf';
const NO_STORE = { 'Cache-Control': 'no-store', 'Content-Type': 'application/json; charset=utf-8' };
const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const ALLOWED_METHODS = new Set(['GET', 'HEAD', ...UNSAFE_METHODS]);
const REMOVED_HEADERS = [
  'authorization', 'cookie', 'host', 'connection', 'content-length',
  'keep-alive', 'transfer-encoding', 'te', 'trailer', 'upgrade',
  'proxy-authorization', 'proxy-authenticate',
  'x-forwarded-for', 'x-forwarded-host', 'x-forwarded-proto',
  'x-erp-csrf',
];

function json(value, status = 200, extra = undefined) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { ...NO_STORE, ...(extra ?? {}) },
  });
}

function readCookie(raw, name) {
  if (!raw || raw.length > 16000) return null;
  const entry = raw.split(';').map(p => p.trim()).find(p => p.startsWith(name + '='));
  const value = entry?.slice(name.length + 1) ?? null;
  return value && value.length <= 8000 && /^[A-Za-z0-9._-]+$/.test(value) ? value : null;
}

function cookie(name, value, maxAgeSeconds) {
  return name + '=' + value +
    '; Path=/; Secure; HttpOnly; SameSite=Strict; Max-Age=' + maxAgeSeconds;
}

function clearCookies(headers) {
  headers.append('Set-Cookie', cookie(SESSION_COOKIE, '', 0));
  headers.append('Set-Cookie', cookie(CSRF_COOKIE, '', 0));
}

function sameOrigin(request) {
  try {
    return request.headers.get('origin') === new URL(request.url).origin;
  } catch {
    return false;
  }
}

function matchesCsrf(actual, expected) {
  if (!expected || !actual || expected.length !== 43 || actual.length !== 43) return false;
  let diff = 0;
  for (let i = 0; i < expected.length; i++) diff |= expected.charCodeAt(i) ^ actual.charCodeAt(i);
  return diff === 0;
}

function randomCsrf() {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

function cleanedHeaders(headers) {
  const copy = new Headers(headers);
  for (const key of REMOVED_HEADERS) copy.delete(key);
  return copy;
}

function safeResponse(upstream) {
  const headers = new Headers(upstream.headers);
  for (const key of [...REMOVED_HEADERS, 'set-cookie', 'content-encoding', 'location']) {
    headers.delete(key);
  }
  headers.set('Cache-Control', 'no-store');
  return new Response([204, 205, 304].includes(upstream.status) ? null : upstream.body, {
    status: upstream.status,
    headers,
  });
}

async function upstreamFetch(upstream, incoming, bearer, fetchImpl) {
  const headers = cleanedHeaders(incoming.headers);
  if (bearer) headers.set('Authorization', 'Bearer ' + bearer);
  const method = incoming.method.toUpperCase();
  const body = method === 'GET' || method === 'HEAD' ? undefined : await incoming.arrayBuffer();
  return fetchImpl(upstream, { method, headers, body, redirect: 'manual', signal: incoming.signal });
}

function target(url, origin) {
  return new URL(url.pathname + url.search, origin);
}

function isJwtShaped(value) {
  return typeof value === 'string' && value.length <= 6000 &&
    /^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(value);
}

/**
 * Opt-in only. The Vercel middleware calls this ONLY when VERCEL_ENV=preview
 * and ERP_COOKIE_BFF_MODE=enabled. No direct client-supplied upstream origin.
 */
export async function bffCookieProxy(request, upstreamOrigin, fetchImpl = fetch) {
  const url = new URL(request.url);
  const method = request.method.toUpperCase();
  const path = url.pathname;
  if (!path.startsWith('/api/') || !ALLOWED_METHODS.has(method)) {
    return json({ mensaje: 'Método o ruta no permitida.' }, 405);
  }

  // An explicit same-origin Origin header is mandatory for every mutation,
  // including login, to reject cross-site submissions and opaque origins.
  if (UNSAFE_METHODS.has(method) && !sameOrigin(request)) {
    return json({ mensaje: 'Origen no permitido.' }, 403);
  }

  const cookieHeader = request.headers.get('cookie');
  const token = readCookie(cookieHeader, SESSION_COOKIE);
  const csrf = readCookie(cookieHeader, CSRF_COOKIE);
  const login = path === '/api/auth/login';
  const logout = path === '/api/auth/logout';
  const session = path === '/api/auth/session';

  if (login) {
    if (method !== 'POST') return json({ mensaje: 'Método no permitido.' }, 405);
    if (!(request.headers.get('content-type') ?? '').toLowerCase().startsWith('application/json')) {
      return json({ mensaje: 'Se requiere JSON.' }, 415);
    }

    try {
      // Existing bearer auth contract remains on Render; only the BFF sees the token.
      const upstream = await upstreamFetch(target(url, upstreamOrigin), request, null, fetchImpl);
      if (!upstream.ok) {
        return json({ mensaje: upstream.status === 429 ? 'Demasiados intentos.' : 'Credenciales inválidas.' },
          [400, 401, 403, 429].includes(upstream.status) ? upstream.status : 502);
      }
      const result = await upstream.json();
      const rawExpiry = Date.parse(result?.expiresAt ?? '');
      const duration = Math.floor((rawExpiry - Date.now()) / 1000);
      if (!isJwtShaped(result?.token) || !['Admin', 'Vendedor', 'Comprador'].includes(result?.rol) ||
          result.rol !== result.role || typeof result.usuario !== 'string' ||
          !Number.isFinite(duration) || duration < 1) {
        return json({ mensaje: 'Sesión no disponible.' }, 502);
      }
      const maxAge = Math.min(duration, 900);
      const safeExpiry = new Date(Date.now() + maxAge * 1000).toISOString();
      const csrfToken = randomCsrf();
      const headers = new Headers(NO_STORE);
      headers.append('Set-Cookie', cookie(SESSION_COOKIE, result.token, maxAge));
      headers.append('Set-Cookie', cookie(CSRF_COOKIE, csrfToken, maxAge));
      return new Response(JSON.stringify({
        usuario: result.usuario, rol: result.rol, role: result.role,
        expiresAt: safeExpiry, csrfToken,
      }), { status: 200, headers });
    } catch {
      return json({ mensaje: 'No se pudo contactar la API.' }, 502);
    }
  }

  if (session && method !== 'GET') return json({ mensaje: 'Método no permitido.' }, 405);

  if (!token && (session || logout || UNSAFE_METHODS.has(method))) {
    return json({ mensaje: 'Sesión requerida.' }, 401);
  }

  if (UNSAFE_METHODS.has(method)) {
    if (!matchesCsrf(request.headers.get('x-erp-csrf'), csrf)) {
      return json({ mensaje: 'CSRF inválido.' }, 403);
    }
  }

  if (logout) {
    if (method !== 'POST') return json({ mensaje: 'Método no permitido.' }, 405);
    const headers = new Headers({ 'Cache-Control': 'no-store' });
    clearCookies(headers);
    return new Response(null, { status: 204, headers });
  }

  try {
    const upstream = await upstreamFetch(target(url, upstreamOrigin), request, token, fetchImpl);
    if (session) {
      if (!upstream.ok) {
        const headers = new Headers(NO_STORE);
        if (upstream.status === 401) clearCookies(headers);
        return new Response(JSON.stringify({ mensaje: 'Sesión inválida.' }),
          { status: upstream.status === 401 ? 401 : 502, headers });
      }
      const result = await upstream.json();
      if (!result || !['Admin', 'Vendedor', 'Comprador'].includes(result.rol) ||
          result.role !== result.rol || typeof result.usuario !== 'string' ||
          !csrf || csrf.length !== 43) {
        return json({ mensaje: 'Sesión no disponible.' }, 502);
      }
      return json({
        usuario: result.usuario, rol: result.rol, role: result.role,
        email: result.email, vendedorId: result.vendedorId,
        expiresAt: result.expiresAt, csrfToken: csrf,
      });
    }
    return safeResponse(upstream);
  } catch {
    return json({ mensaje: 'No se pudo contactar la API.' }, 502);
  }
}
