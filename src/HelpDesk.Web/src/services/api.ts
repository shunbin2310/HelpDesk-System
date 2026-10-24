import { session, setSession } from './session'

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message) }
}

export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = session.value?.accessToken
  let response: Response
  try {
    response = await fetch(`/api${path}`, {
      ...init, signal: AbortSignal.timeout(15000),
      headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init.headers },
    })
  } catch { throw new ApiError(0, 'The service could not be reached. Please try again.') }
  if (!response.ok) {
    if (response.status === 401 && token && session.value?.accessToken === token) setSession(null)
    const body = await response.json().catch(() => null) as { detail?: string; title?: string; errors?: Record<string, string[]> } | null
    const errors = body?.errors ? Object.values(body.errors).flat().join(' ') : ''
    throw new ApiError(response.status, errors || body?.detail || (response.status === 403
      ? 'You do not have permission to access this page.' : response.status === 429
        ? 'Too many attempts. Please wait a minute and try again.' : body?.title || 'The request could not be completed.'))
  }
  return response.status === 204 ? undefined as T : await response.json() as T
}

export function send<T>(path: string, method: string, body: unknown) {
  return request<T>(path, { method, body: JSON.stringify(body) })
}
