<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { request } from '../services/api'
import { session } from '../services/session'
import { statuses, canManageTickets, formatTicketDate, type TicketPage } from '../services/tickets'

const route = useRoute(),
  router = useRouter()
const assignments = computed(() => route.path === '/assignments')
const admin = computed(() => canManageTickets(session.value?.user.role))
const data = ref<TicketPage>({ items: [], total: 0, page: 1 })
const loading = ref(true),
  error = ref(''),
  status = ref(''),
  unassigned = ref(false)
const errorBox = ref<HTMLElement>()
let generation = 0
async function load() {
  const version = ++generation
  loading.value = true
  error.value = ''
  status.value = typeof route.query.status === 'string' ? route.query.status : ''
  unassigned.value =
    route.query.unassigned === '1' || (assignments.value && route.query.unassigned === undefined)
  const page = Math.min(1000000, Math.max(1, Math.trunc(Number(route.query.page) || 1)))
  const query = new URLSearchParams({
    page: String(page),
    status: status.value,
    onlyUnassigned: String(unassigned.value),
  })
  try {
    const result = await request<TicketPage>(`/tickets?${query}`)
    if (version === generation) data.value = result
  } catch (cause) {
    if (version === generation) {
      error.value = cause instanceof Error ? cause.message : 'Unable to load tickets.'
      await nextTick()
      errorBox.value?.focus()
    }
  } finally {
    if (version === generation) loading.value = false
  }
}
async function filter(page = 1) {
  const target = {
    path: route.path,
    query: {
      status: status.value || undefined,
      unassigned: unassigned.value ? '1' : '0',
      page: String(page),
    },
  }
  if (router.resolve(target).fullPath === route.fullPath) await load()
  else await router.replace(target)
}
watch(() => route.fullPath, load, { immediate: true })
</script>

<template>
  <section class="page-heading heading-row">
    <div>
      <p class="eyebrow">IT SUPPORT</p>
      <h1>
        {{
          assignments
            ? 'Ticket assignment'
            : session?.user.role === 'EMPLOYEE'
              ? 'My tickets'
              : session?.user.role === 'SUPPORT_IT_STAFF'
                ? 'Assigned tickets'
                : 'All tickets'
        }}
      </h1>
      <p class="muted">
        {{
          assignments
            ? 'Choose a ticket to assign or reassign an IT Staff member.'
            : 'Follow your support requests and their latest progress.'
        }}
      </p>
    </div>
    <RouterLink
      v-if="session?.user.role === 'EMPLOYEE'"
      class="button primary"
      to="/tickets/new"
      >Submit ticket</RouterLink
    >
  </section>
  <form
    class="ticket-filters"
    @submit.prevent="filter()"
  >
    <div>
      <label for="status-filter">Status</label
      ><select
        id="status-filter"
        v-model="status"
      >
        <option value="">All statuses</option>
        <option
          v-for="(label, code) in statuses"
          :key="code"
          :value="code"
        >
          {{ label }}
        </option>
      </select>
    </div>
    <label
      v-if="admin"
      class="checkbox-label"
      ><input
        v-model="unassigned"
        type="checkbox"
      />
      Unassigned only</label
    ><button :disabled="loading">Apply filters</button>
  </form>
  <div
    v-if="error"
    ref="errorBox"
    class="error"
    role="alert"
    tabindex="-1"
  >
    {{ error }}
    <button
      :disabled="loading"
      @click="load"
    >
      Try again
    </button>
  </div>
  <section
    class="card tickets-card"
    aria-labelledby="tickets-heading"
    :aria-busy="loading"
  >
    <div class="list-heading">
      <h2 id="tickets-heading">
        Tickets <span class="count">{{ data.total }}</span>
      </h2>
      <span class="muted">{{
        assignments ? 'Assignment preserves the current status' : 'Most recently updated first'
      }}</span>
    </div>
    <p
      v-if="loading"
      class="empty"
      role="status"
    >
      Loading tickets…
    </p>
    <template v-else-if="!error"
      ><div
        v-if="!data.items.length"
        class="ticket-empty"
      >
        <h2>No tickets to show</h2>
        <p class="muted">
          {{
            session?.user.role === 'EMPLOYEE'
              ? 'Submit an IT issue or change the status filter.'
              : 'Try another filter. Assigned tickets will appear here when available.'
          }}
        </p>
      </div>
      <ul
        v-else
        class="ticket-list"
      >
        <li
          v-for="ticket in data.items"
          :key="ticket.id"
          class="ticket-row"
        >
          <div class="ticket-summary">
            <RouterLink :to="`/tickets/${ticket.id}`"
              ><span class="ticket-number">{{ ticket.ticketNumber }}</span
              ><strong>{{ ticket.title }}</strong></RouterLink
            >
            <p>{{ ticket.category }} · Requested by {{ ticket.requesterName }}</p>
          </div>
          <div class="ticket-badges">
            <span :class="['status-badge', `status-${ticket.status}`]">{{
              statuses[ticket.status] ?? ticket.status
            }}</span
            ><span :class="['priority-badge', `priority-${ticket.priority}`]">{{
              ticket.priority
            }}</span>
          </div>
          <div class="ticket-owner">
            <strong>{{ ticket.assigneeName ?? 'Unassigned' }}</strong
            ><time :datetime="ticket.updatedAtUtc">{{
              formatTicketDate(ticket.updatedAtUtc)
            }}</time>
          </div>
        </li>
      </ul>
      <div class="pagination">
        <span class="muted"
          >Page {{ data.page }} of {{ Math.max(1, Math.ceil(data.total / 25)) }}</span
        >
        <div class="actions">
          <button
            :disabled="loading || data.page <= 1"
            @click="filter(data.page - 1)"
          >
            Previous</button
          ><button
            :disabled="loading || data.page * 25 >= data.total"
            @click="filter(data.page + 1)"
          >
            Next
          </button>
        </div>
      </div>
    </template>
  </section>
</template>
