import { createRouter, createWebHistory } from 'vue-router'
import { ApiError, request } from './services/api'
import { session, setSession, type UserAccount } from './services/session'
import LoginView from './views/LoginView.vue'
import HomeView from './views/HomeView.vue'
import UsersView from './views/UsersView.vue'
import PasswordView from './views/PasswordView.vue'
import AccessView from './views/AccessView.vue'
import WorkflowView from './views/WorkflowView.vue'
import TicketsView from './views/TicketsView.vue'
import CreateTicketView from './views/CreateTicketView.vue'
import TicketDetailView from './views/TicketDetailView.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', component: LoginView },
    { path: '/', component: HomeView },
    { path: '/tickets', component: TicketsView },
    { path: '/tickets/new', component: CreateTicketView, meta: { role: 'EMPLOYEE' } },
    { path: '/tickets/:ticketId', component: TicketDetailView },
    { path: '/assignments', component: TicketsView, meta: { roles: ['SUPPORT_ADMIN', 'SYSTEM_ADMIN'] } },
    { path: '/users', component: UsersView, meta: { role: 'SYSTEM_ADMIN' } },
    { path: '/workflow', component: WorkflowView, meta: { role: 'SYSTEM_ADMIN' } },
    { path: '/password', component: PasswordView },
    { path: '/access', component: AccessView },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach(async to => {
  if (to.path === '/login' || to.path === '/access') return true
  if (!session.value || Date.parse(session.value.expiresAtUtc) <= Date.now()) {
    setSession(null)
    return '/login'
  }
  try {
    const user = await request<UserAccount>('/auth/me')
    if (!session.value) return '/login'
    setSession({ ...session.value, user })
    if ((to.meta.role && user.role !== to.meta.role) || (Array.isArray(to.meta.roles) && !to.meta.roles.includes(user.role)))
      return { path: '/access', query: { reason: 'forbidden' } }
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return '/login'
    return { path: '/access', query: { reason: 'unavailable' } }
  }
  return true
})
