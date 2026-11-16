/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import type { Usuario } from '../types';
import { authApi, type CookieSessionResponse } from '../api/auth';
import { configureCookieBffMode, setCookieBffCsrfToken, setUnauthorizedHandler } from '../api/client';
import { clearStoredSession, persistSession, readStoredSession, sessionFromLogin, sessionRemainingMs, type AuthSession } from './authSession';

// F354: an opt-in Vite Preview build only. Keep legacy behavior unchanged until
// the browser E2E matrix and the Vercel/Render release gate are approved.
// A matching server flag ERP_COOKIE_BFF_MODE=enabled is ALSO required.
const COOKIE_BFF_PREVIEW = import.meta.env.VITE_BFF_AUTH_MODE === 'enabled';

interface AuthContextType {
  usuario: Usuario | null;
  token: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (email: string, password: string) => Promise<Usuario>;
  logout: () => void;
}

interface CookieState {
  usuario: Usuario;
  expiresAt: string;
}

const AuthContext = createContext<AuthContextType | null>(null);

function cookieSessionFromResponse(res: CookieSessionResponse, requestedEmail: string): CookieState {
  const validRole = res?.role === 'Admin' || res?.role === 'Vendedor' || res?.role === 'Comprador';
  const validExpiry = Date.parse(res?.expiresAt ?? '');
  const email = (res?.email || requestedEmail).trim().toLowerCase();
  if (!validRole || res.rol !== res.role || typeof res.usuario !== 'string' ||
      !res.usuario.trim() || !email || !Number.isFinite(validExpiry) || validExpiry <= Date.now() ||
      typeof res.csrfToken !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(res.csrfToken)) {
    throw new Error('Respuesta de sesión del BFF inválida.');
  }
  return {
    usuario: {
      nombre: res.usuario,
      email,
      rol: res.rol,
      vendedorId: res.rol === 'Vendedor' && typeof res.vendedorId === 'string'
        ? res.vendedorId : undefined,
    },
    expiresAt: res.expiresAt,
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthSession | null>(
    () => COOKIE_BFF_PREVIEW ? null : readStoredSession(localStorage)
  );
  const [cookieSession, setCookieSession] = useState<CookieState | null>(null);
  const [isLoading, setLoading] = useState(COOKIE_BFF_PREVIEW);
  const usuario = COOKIE_BFF_PREVIEW ? cookieSession?.usuario ?? null : session?.usuario ?? null;
  const token = COOKIE_BFF_PREVIEW ? null : session?.token ?? null;

  const invalidate = useCallback(() => {
    if (COOKIE_BFF_PREVIEW) {
      setCookieBffCsrfToken(null);
      setCookieSession(null);
    } else {
      clearStoredSession(localStorage);
      setSession(null);
    }
  }, []);

  // In cookie mode the browser has no JWT. Identity is verified by the API
  // through the BFF using its HttpOnly cookie on every app reload.
  useEffect(() => {
    if (!COOKIE_BFF_PREVIEW) {
      configureCookieBffMode(false);
      return;
    }
    configureCookieBffMode(true);
    clearStoredSession(localStorage);
    let active = true;
    const restore = async () => {
      try {
        const response = await fetch('/api/auth/session', {
          method: 'GET', credentials: 'same-origin', cache: 'no-store',
          headers: { Accept: 'application/json' },
        });
        if (response.status !== 200) return;
        const data = await response.json() as CookieSessionResponse;
        const restored = cookieSessionFromResponse(data, data.email ?? '');
        if (active) {
          setCookieBffCsrfToken(data.csrfToken);
          setCookieSession(restored);
        }
      } catch {
        // A missing/unreachable BFF must never create a browser-side session.
      } finally {
        if (active) setLoading(false);
      }
    };
    void restore();
    return () => { active = false; setCookieBffCsrfToken(null); };
  }, []);

  // Automatically expire the UI session. The cookie lifetime is enforced by
  // the browser and the upstream JWT by ASP.NET authentication middleware.
  useEffect(() => {
    const activeSession = COOKIE_BFF_PREVIEW ? cookieSession : session;
    if (!activeSession) return;
    const remaining = COOKIE_BFF_PREVIEW
      ? Date.parse(activeSession.expiresAt) - Date.now()
      : sessionRemainingMs(session!);
    if (!Number.isFinite(remaining) || remaining <= 0) {
      invalidate();
      return;
    }
    const timeout = window.setTimeout(invalidate, Math.min(remaining, 2_147_483_647));
    return () => window.clearTimeout(timeout);
  }, [cookieSession, session, invalidate]);

  useEffect(() => {
    setUnauthorizedHandler(() => {
      // All 401 responses invalidate locally. In cookie mode the opaque cookie
      // is never read or reconstructed in JavaScript.
      invalidate();
    });
  }, [invalidate]);

  const login = useCallback(async (email: string, password: string): Promise<Usuario> => {
    const res = await authApi.login({ email, password });
    if (!COOKIE_BFF_PREVIEW) {
      const nextSession = sessionFromLogin(res, email);
      persistSession(localStorage, nextSession);
      setSession(nextSession);
      return nextSession.usuario;
    }

    // BFF returns a cookie and CSRF nonce, not the JWT. Confirm account state
    // and role via the authenticated backend session endpoint before showing UI.
    const cookieLogin = res as unknown as CookieSessionResponse;
    if (typeof cookieLogin?.csrfToken !== 'string' ||
        !/^[A-Za-z0-9_-]{43}$/.test(cookieLogin.csrfToken)) {
      throw new Error('No se recibió una sesión segura del BFF.');
    }
    setCookieBffCsrfToken(cookieLogin.csrfToken);
    try {
      const profile = await authApi.session();
      const result = cookieSessionFromResponse(profile, email);
      setCookieBffCsrfToken(profile.csrfToken);
      setCookieSession(result);
      return result.usuario;
    } catch (error) {
      try { await authApi.logout(); } catch { /* Fail closed on UI. */ }
      invalidate();
      throw error;
    }
  }, [invalidate]);

  const logout = useCallback(() => {
    if (!COOKIE_BFF_PREVIEW) {
      invalidate();
      return;
    }
    // A cookie logout MUST receive HTTP 204 before the UI treats it as closed.
    // Network failure retains the UI state instead of claiming invalidation.
    void authApi.logout()
      .then(() => invalidate())
      .catch(() => {
        window.alert('No se pudo confirmar el cierre de sesión. Comprueba tu conexión y vuelve a intentarlo.');
      });
  }, [invalidate]);

  return (
    <AuthContext.Provider value={{
      usuario, token, isAuthenticated: COOKIE_BFF_PREVIEW ? !!cookieSession : !!token && !!usuario,
      isLoading, login, logout,
    }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
