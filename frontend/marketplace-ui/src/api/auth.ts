/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { AuthResponse, LoginRequest } from '../types';

export interface CookieSessionResponse {
  usuario: string;
  role: 'Admin' | 'Vendedor' | 'Comprador';
  rol: 'Admin' | 'Vendedor' | 'Comprador';
  email?: string;
  vendedorId?: string | null;
  expiresAt: string;
  csrfToken: string;
}

export const authApi = {
  login: (data: LoginRequest) =>
    apiClient.post<AuthResponse>('/api/auth/login', data),

  // Available only behind the BFF preview flag; returns no JWT.
  session: () => apiClient.get<CookieSessionResponse>('/api/auth/session'),
  logout: () => apiClient.post<void>('/api/auth/logout'),

  register: (data: { nombre: string; email: string; password: string; rol?: string }) =>
    apiClient.post<AuthResponse>('/api/auth/register', data),
};
