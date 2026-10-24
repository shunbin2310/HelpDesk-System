import { shallowRef } from 'vue'

export const roles = {
  EMPLOYEE: 'Employee', SUPPORT_IT_STAFF: 'Support IT Staff',
  SUPPORT_ADMIN: 'Support Admin', SYSTEM_ADMIN: 'System Admin',
} as const
export type Role = keyof typeof roles
export interface UserAccount { id: number; email: string; fullName: string; role: Role; isActive: boolean }
export interface Session { accessToken: string; expiresAtUtc: string; user: UserAccount }
const key = 'helpdesk.session'

function restore(): Session | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(key) ?? 'null') as Session | null
    if (value && typeof value.accessToken === 'string' && Date.parse(value.expiresAtUtc) > Date.now()
      && value.user && value.user.role in roles && typeof value.user.fullName === 'string') return value
  } catch { /* An unavailable or malformed browser session requires a new login. */ }
  return null
}

export const session = shallowRef<Session | null>(restore())
let expiry: ReturnType<typeof setTimeout> | undefined

export function setSession(value: Session | null) {
  // shortcut: logout clears this tab only; add server revocation when individual token logout is required.
  clearTimeout(expiry)
  // Persist before updating UI: fail visibly if the browser cannot store this tab's session.
  if (value) sessionStorage.setItem(key, JSON.stringify(value))
  else { try { sessionStorage.removeItem(key) } catch { /* Memory session is still cleared. */ } }
  session.value = value
  if (value) expiry = setTimeout(() => setSession(null), Math.max(0, Date.parse(value.expiresAtUtc) - Date.now()))
}

if (session.value) expiry = setTimeout(() => setSession(null), Date.parse(session.value.expiresAtUtc) - Date.now())
