<script setup lang="ts">
import { nextTick, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { roles, session, setSession } from './services/session'
import { canManageTickets } from './services/tickets'
const route = useRoute(),
  router = useRouter()
const titles: Record<string, string> = {
  '/users': 'User accounts',
  '/workflow': 'Ticket workflow',
  '/password': 'Change password',
  '/login': 'Sign in',
  '/tickets': 'Tickets',
  '/tickets/new': 'Submit ticket',
  '/assignments': 'Ticket assignment',
  '/delegations': 'Delegations',
}
watch(
  () => route.path,
  async () => {
    document.title = `${titles[route.path] ?? (route.path.startsWith('/tickets/') ? 'Ticket details' : 'Workspace')} · Helpdesk`
    await nextTick()
    document.getElementById('main')?.focus()
  },
)
watch(session, (value) => {
  if (!value && route.path !== '/login') void router.replace('/login')
})
async function logout() {
  setSession(null)
  await router.replace('/login')
}
</script>

<template>
  <a
    class="skip-link"
    href="#main"
    >Skip to content</a
  >
  <div :class="['app', { 'login-app': route.path === '/login' }]">
    <header class="app-header">
      <RouterLink
        class="brand"
        :to="session ? '/' : '/login'"
        ><span
          class="brand-mark"
          aria-hidden="true"
          >H</span
        ><span>Helpdesk</span></RouterLink
      >
      <nav
        v-if="session && route.path !== '/login'"
        aria-label="Main navigation"
      >
        <RouterLink
          to="/"
          exact-active-class="selected"
          >Workspace</RouterLink
        ><RouterLink
          to="/tickets"
          active-class="selected"
          >Tickets</RouterLink
        ><RouterLink
          v-if="canManageTickets(session.user.role)"
          to="/assignments"
          active-class="selected"
          >Assignments</RouterLink
        ><RouterLink
          v-if="canManageTickets(session.user.role)"
          to="/delegations"
          active-class="selected"
          >Delegations</RouterLink
        ><RouterLink
          v-if="session.user.role === 'SYSTEM_ADMIN'"
          to="/users"
          active-class="selected"
          >Users</RouterLink
        ><RouterLink
          v-if="session.user.role === 'SYSTEM_ADMIN'"
          to="/workflow"
          active-class="selected"
          >Workflow</RouterLink
        ><RouterLink
          to="/password"
          active-class="selected"
          >Password</RouterLink
        >
      </nav>
      <div
        v-if="session && route.path !== '/login'"
        class="account-menu"
      >
        <div>
          <strong>{{ session.user.fullName }}</strong
          ><span>{{ roles[session.user.role] }}</span>
        </div>
        <button @click="logout">Sign out</button>
      </div>
      <span
        v-else
        class="header-caption"
        >IT SUPPORT PORTAL</span
      >
    </header>
    <main
      id="main"
      tabindex="-1"
      :class="route.path === '/login' ? 'login-main' : 'workspace'"
    >
      <RouterView />
    </main>
    <footer>Helpdesk System <span aria-hidden="true">·</span> Company IT Support</footer>
  </div>
</template>
