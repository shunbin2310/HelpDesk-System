<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { request, send } from '../services/api'
import {
  delegationStatuses,
  returnOutcomes,
  malaysiaDate,
  malaysiaInput,
  delegationPeriod,
  type DelegationPage,
  type DelegationDetail,
  type DelegationItem,
} from '../services/delegations'
import { statuses } from '../services/tickets'

const list = ref<DelegationPage>({ items: [], total: 0, page: 1 }),
  detail = ref<DelegationDetail | null>(null)
const staff = ref<{ id: number; fullName: string }[]>([]),
  source = ref<number | ''>(''),
  target = ref<number | ''>('')
const today = ref(malaysiaInput(new Date())),
  start = ref(today.value),
  end = ref(today.value),
  reason = ref('')
const startError = computed(() =>
  start.value && start.value < today.value ? 'Start date cannot be before today in Malaysia.' : '',
)
const endMinimum = computed(() => (start.value > today.value ? start.value : today.value))
const endError = computed(() =>
  end.value && end.value < endMinimum.value
    ? 'End date must be on or after the start date and today.'
    : '',
)
function updateToday() {
  today.value = malaysiaInput(new Date())
}
const loading = ref(false),
  busy = ref(false),
  error = ref(''),
  notice = ref(''),
  errorBox = ref<HTMLElement>()
const stopDialog = ref<HTMLDialogElement>()
const stopSelection = ref<{ item: DelegationItem; action: 'cancel' | 'end-early' } | null>(null)
let generation = 0
async function report(cause: unknown) {
  error.value = cause instanceof Error ? cause.message : 'Unable to complete the request.'
  await nextTick()
  errorBox.value?.focus()
}
async function refresh(page = list.value.page) {
  updateToday()
  const version = ++generation
  loading.value = true
  error.value = ''
  try {
    const [items, people] = await Promise.all([
      request<DelegationPage>(`/delegations?page=${page}`),
      request<{ id: number; fullName: string }[]>('/tickets/assignees'),
    ])
    const selected = detail.value
      ? await request<DelegationDetail>(
          `/delegations/${detail.value.delegation.id}?page=${detail.value.page}`,
        )
      : null
    if (version === generation) {
      list.value = items
      staff.value = people
      detail.value = selected
    }
  } catch (cause) {
    if (version === generation) await report(cause)
  } finally {
    if (version === generation) loading.value = false
  }
}
async function select(item: DelegationItem, page = 1) {
  busy.value = true
  error.value = ''
  try {
    detail.value = await request<DelegationDetail>(`/delegations/${item.id}?page=${page}`)
  } catch (cause) {
    await report(cause)
  } finally {
    busy.value = false
  }
}
async function create() {
  updateToday()
  if (startError.value || endError.value) {
    await report(new Error('Check the delegation dates below.'))
    return
  }
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    const created = await send<DelegationItem>('/delegations', 'POST', {
      sourceUserId: source.value,
      targetUserId: target.value,
      startDate: start.value,
      endDate: end.value,
      reason: reason.value.trim() || null,
    })
    notice.value = 'Delegation created. Refresh to see background processing updates.'
    source.value = ''
    target.value = ''
    reason.value = ''
    detail.value = null
    await refresh(1)
    await select(created)
  } catch (cause) {
    await report(cause)
  } finally {
    busy.value = false
  }
}
async function confirmStop(item: DelegationItem, action: 'cancel' | 'end-early') {
  stopSelection.value = { item, action }
  await nextTick()
  stopDialog.value?.showModal()
}
async function stopDelegation() {
  const selected = stopSelection.value
  if (!selected) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    const result = await send<DelegationItem>(
      `/delegations/${selected.item.id}/${selected.action}`,
      'POST',
      {
        rowVersion: selected.item.rowVersion,
      },
    )
    stopDialog.value?.close()
    notice.value =
      selected.action === 'cancel'
        ? 'Delegation cancelled. No tickets were handed over.'
        : result.pendingTickets > 0
          ? 'New handovers have stopped. Some tickets are still awaiting return; review the warning below.'
          : 'Delegation ended early. Eligible tickets have been returned.'
    await refresh()
  } catch (cause) {
    stopDialog.value?.close()
    await report(cause)
  } finally {
    busy.value = false
  }
}
watch(source, () => {
  if (source.value === target.value) target.value = ''
})
void refresh()
</script>

<template>
  <section class="page-heading">
    <p class="eyebrow">IT SUPPORT ADMINISTRATION</p>
    <h1>Delegations</h1>
    <p class="muted">
      Temporarily hand over a staff member’s tickets, then return them automatically.
    </p>
  </section>
  <div
    v-if="error"
    ref="errorBox"
    class="error"
    role="alert"
    tabindex="-1"
  >
    {{ error }}
    <ul v-if="startError || endError">
      <li v-if="startError">
        <a href="#delegation-start">{{ startError }}</a>
      </li>
      <li v-if="endError">
        <a href="#delegation-end">{{ endError }}</a>
      </li>
    </ul>
  </div>
  <p
    v-if="notice"
    class="notice"
    role="status"
  >
    {{ notice }}
  </p>
  <section
    class="card"
    aria-labelledby="delegation-create"
  >
    <h2 id="delegation-create">New delegation</h2>
    <p class="form-help">
      Only Open and In Progress tickets move at the start. At the end, eligible Open, In Progress
      and Resolved tickets return. Closed tickets and later admin assignments are kept.
    </p>
    <form
      :aria-busy="busy"
      @submit.prevent="create"
    >
      <fieldset
        :disabled="busy || loading"
        class="delegation-fields"
      >
        <legend class="sr-only">Delegation details</legend>
        <div>
          <label for="delegation-source">Original IT Staff (A)</label
          ><select
            id="delegation-source"
            v-model.number="source"
            required
          >
            <option
              disabled
              value=""
            >
              Select original staff
            </option>
            <option
              v-for="person in staff"
              :key="person.id"
              :value="person.id"
            >
              {{ person.fullName }}
            </option>
          </select>
        </div>
        <div>
          <label for="delegation-target">Delegate (B)</label
          ><select
            id="delegation-target"
            v-model.number="target"
            required
          >
            <option
              disabled
              value=""
            >
              Select delegate
            </option>
            <option
              v-for="person in staff.filter((item) => item.id !== source)"
              :key="person.id"
              :value="person.id"
            >
              {{ person.fullName }}
            </option>
          </select>
        </div>
        <div>
          <label for="delegation-start">Start date (Malaysia, UTC+8)</label>
          <input
            id="delegation-start"
            v-model="start"
            type="date"
            :min="today"
            max="9999-12-30"
            :aria-invalid="!!startError"
            aria-describedby="delegation-start-help delegation-start-error"
            required
            @focus="updateToday"
          />
          <p
            id="delegation-start-help"
            class="form-help"
          >
            Today or later. Today starts immediately; future dates start at 00:00.
          </p>
          <p
            id="delegation-start-error"
            class="file-error"
            aria-live="polite"
          >
            {{ startError }}
          </p>
        </div>
        <div>
          <label for="delegation-end">End date (inclusive)</label
          ><input
            id="delegation-end"
            v-model="end"
            type="date"
            :min="endMinimum"
            max="9999-12-30"
            :aria-invalid="!!endError"
            aria-describedby="delegation-end-help delegation-end-error"
            required
            @focus="updateToday"
          />
          <p
            id="delegation-end-help"
            class="form-help"
          >
            Same day or later. Active through this date; returns start the next day at 00:00 MYT.
          </p>
          <p
            id="delegation-end-error"
            class="file-error"
            aria-live="polite"
          >
            {{ endError }}
          </p>
        </div>
        <div class="delegation-full">
          <label for="delegation-reason">Reason (optional)</label
          ><textarea
            id="delegation-reason"
            v-model="reason"
            rows="2"
            maxlength="1000"
            placeholder="For example, annual leave"
          ></textarea>
        </div>
      </fieldset>
      <p class="form-help">
        A staff member can participate in only one delegation at a time. Overlapping periods and
        chained delegations are unavailable. Scheduled delegations can be cancelled; active
        delegations can end early. Both actions keep the history.
      </p>
      <div class="actions">
        <button
          class="primary"
          :disabled="busy || loading || staff.length < 2"
        >
          {{ busy ? 'Saving…' : 'Create delegation' }}</button
        ><span
          v-if="!loading && staff.length < 2"
          class="form-help"
          >Create at least two active IT Staff accounts first.</span
        >
      </div>
    </form>
  </section>
  <section
    class="card delegation-section"
    aria-labelledby="delegation-list"
  >
    <div class="delegation-section-heading">
      <h2 id="delegation-list">
        Delegation history <span class="count">{{ list.total }}</span>
      </h2>
      <button
        :disabled="busy || loading"
        @click="refresh()"
      >
        {{ loading ? 'Loading…' : 'Refresh' }}
      </button>
    </div>
    <p class="form-help">
      Automatic processing runs when the API starts and every 30 seconds. If a staff account becomes
      unavailable, review the warning and restore the account or manually reassign tickets.
    </p>
    <p
      v-if="loading"
      role="status"
    >
      Loading delegations…
    </p>
    <p
      v-else-if="!list.items.length"
      class="muted"
    >
      No delegations yet.
    </p>
    <ul
      v-else
      class="delegation-list"
    >
      <li
        v-for="item in list.items"
        :key="item.id"
      >
        <div>
          <strong>{{ item.sourceName }} → {{ item.targetName }}</strong>
          <p>{{ delegationPeriod(item) }}</p>
          <p v-if="item.reason">{{ item.reason }}</p>
          <p v-if="item.stoppedAtUtc">
            Stopped by {{ item.stoppedByName }} · {{ malaysiaDate(item.stoppedAtUtc) }} (MYT).
            Scheduled return: {{ malaysiaDate(item.endsAtUtc) }} (MYT).
          </p>
          <p
            v-if="item.issue"
            class="file-error"
          >
            {{ item.issue }}
          </p>
          <p>
            {{ item.totalTickets }} tickets · {{ item.returnedTickets }} returned ·
            {{ item.pendingTickets }} awaiting return
          </p>
        </div>
        <div class="delegation-row-actions">
          <span :class="['role-badge', { 'delegation-warning': item.issue }]">{{
            delegationStatuses[item.status] ?? item.status
          }}</span
          ><button
            :disabled="busy || loading"
            :aria-label="`View tickets for delegation ${item.id}`"
            @click="select(item)"
          >
            View tickets
          </button>
          <button
            v-if="item.canCancel"
            type="button"
            :disabled="busy || loading"
            :aria-label="`Cancel delegation ${item.id}`"
            @click="confirmStop(item, 'cancel')"
          >
            Cancel delegation
          </button>
          <button
            v-if="item.canEndEarly"
            type="button"
            :disabled="busy || loading"
            :aria-label="`End delegation ${item.id} early`"
            @click="confirmStop(item, 'end-early')"
          >
            End early
          </button>
        </div>
      </li>
    </ul>
    <div class="pagination">
      <span>Page {{ list.page }} of {{ Math.max(1, Math.ceil(list.total / 25)) }}</span>
      <div class="actions">
        <button
          :disabled="busy || loading || list.page === 1"
          @click="refresh(list.page - 1)"
        >
          Previous</button
        ><button
          :disabled="busy || loading || list.page * 25 >= list.total"
          @click="refresh(list.page + 1)"
        >
          Next
        </button>
      </div>
    </div>
  </section>
  <section
    v-if="detail"
    class="card delegation-section"
    aria-labelledby="delegation-tickets"
  >
    <div class="delegation-section-heading">
      <h2 id="delegation-tickets">
        Tickets: {{ detail.delegation.sourceName }} → {{ detail.delegation.targetName }}
      </h2>
      <button
        :disabled="busy"
        @click="detail = null"
      >
        Close details
      </button>
    </div>
    <p class="form-help">
      {{ detail.delegation.returnedTickets }} returned ·
      {{ detail.delegation.skippedClosedTickets }} kept closed ·
      {{ detail.delegation.skippedReassignedTickets }} kept after admin reassignment
    </p>
    <p
      v-if="!detail.tickets.length"
      class="muted"
    >
      No tickets handed over yet.
    </p>
    <ul
      v-else
      class="delegation-list"
    >
      <li
        v-for="ticket in detail.tickets"
        :key="ticket.ticketId"
      >
        <div>
          <RouterLink :to="`/tickets/${ticket.ticketId}`"
            >{{ ticket.ticketNumber }} — {{ ticket.title }}</RouterLink
          >
          <p>
            {{ statuses[ticket.status] ?? ticket.status }} · Current assignee:
            {{ ticket.currentAssigneeName ?? 'Unassigned' }}
          </p>
          <p>
            {{
              ticket.entryReason === 'ExistingTicket'
                ? 'Handed over at start'
                : 'Assigned during delegation'
            }}
            · {{ malaysiaDate(ticket.delegatedAtUtc) }} (MYT)
          </p>
        </div>
        <span class="role-badge">{{
          returnOutcomes[ticket.returnOutcome] ?? ticket.returnOutcome
        }}</span>
      </li>
    </ul>
    <div class="pagination">
      <span>Page {{ detail.page }} of {{ Math.max(1, Math.ceil(detail.total / 25)) }}</span>
      <div class="actions">
        <button
          :disabled="busy || loading || detail.page === 1"
          @click="select(detail.delegation, detail.page - 1)"
        >
          Previous</button
        ><button
          :disabled="busy || loading || detail.page * 25 >= detail.total"
          @click="select(detail.delegation, detail.page + 1)"
        >
          Next
        </button>
      </div>
    </div>
  </section>
  <dialog
    ref="stopDialog"
    aria-labelledby="stop-delegation-title"
    @close="stopSelection = null"
    @cancel="busy && $event.preventDefault()"
  >
    <form
      v-if="stopSelection"
      :aria-busy="busy"
      @submit.prevent="stopDelegation"
    >
      <h2 id="stop-delegation-title">
        {{ stopSelection.action === 'cancel' ? 'Cancel delegation?' : 'End delegation early?' }}
      </h2>
      <p>{{ stopSelection.item.sourceName }} → {{ stopSelection.item.targetName }}</p>
      <p v-if="stopSelection.action === 'cancel'">
        This arrangement will not start. No tickets will move. Its history will be kept.
      </p>
      <p v-else>
        Stop new handovers now and return eligible tickets to the original staff member. Closed
        tickets and later admin assignments will be kept. If the original staff account is
        unavailable, returns will wait until it is restored.
      </p>
      <div class="actions dialog-actions">
        <button
          type="button"
          autofocus
          :disabled="busy"
          @click="stopDialog?.close()"
        >
          Keep delegation
        </button>
        <button
          class="primary"
          :disabled="busy"
        >
          {{
            busy
              ? 'Saving…'
              : stopSelection.action === 'cancel'
                ? 'Confirm cancellation'
                : 'Confirm end early'
          }}
        </button>
      </div>
    </form>
  </dialog>
</template>
