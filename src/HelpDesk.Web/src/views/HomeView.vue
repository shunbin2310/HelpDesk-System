<script setup lang="ts">
import { session, roles } from '../services/session'
import { canManageTickets } from '../services/tickets'
</script>
<template>
  <section class="page-heading"><p class="eyebrow">YOUR WORKSPACE</p><h1>Welcome, {{ session?.user.fullName }}.</h1><p class="muted">{{ session ? roles[session.user.role] : '' }}</p></section>
  <section class="card welcome-panel"><span class="section-number" aria-hidden="true">01</span><div><h2>Your support workspace</h2><p class="muted">{{ session?.user.role === 'EMPLOYEE' ? 'Report an IT issue and follow its progress.' : session?.user.role === 'SUPPORT_IT_STAFF' ? 'View the tickets currently assigned to you and continue helping employees.' : 'Oversee tickets, assign IT Staff, and follow the support process.' }}</p><div class="actions"><RouterLink v-if="session?.user.role === 'EMPLOYEE'" class="button primary" to="/tickets/new">Submit ticket</RouterLink><RouterLink :class="['button', { primary: session?.user.role !== 'EMPLOYEE' }]" to="/tickets">{{ session?.user.role === 'SUPPORT_IT_STAFF' ? 'Assigned tickets' : 'View tickets' }}</RouterLink><RouterLink v-if="canManageTickets(session?.user.role)" class="button" to="/assignments">Assign tickets</RouterLink><RouterLink v-if="session?.user.role === 'SYSTEM_ADMIN'" class="button" to="/users">Manage users</RouterLink><RouterLink v-if="session?.user.role === 'SYSTEM_ADMIN'" class="button" to="/workflow">Ticket workflow</RouterLink></div></div></section>
</template>
