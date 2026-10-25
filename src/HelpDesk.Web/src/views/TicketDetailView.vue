<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ApiError, request, send } from '../services/api'
import { session } from '../services/session'
import {
  canManageTickets,
  formatTicketDate,
  priorities,
  statuses,
  type TicketDetail,
  type AttachmentItem,
} from '../services/tickets'

import AttachmentList from '../components/AttachmentList.vue'
import TicketTimeline from '../components/TicketTimeline.vue'

const route = useRoute()
const detail = ref<TicketDetail | null>(null),
  assignees = ref<{ id: number; fullName: string }[]>([])
const admin = computed(() => canManageTickets(session.value?.user.role))
const loading = ref(true),
  busy = ref(false),
  error = ref(''),
  notice = ref(''),
  comment = ref('')
const assignment = ref<number | ''>(''),
  priority = ref(2),
  action = ref('')
const errorBox = ref<HTMLElement>(),
  commentInput = ref<HTMLTextAreaElement>()
const selectedAction = computed(() =>
  detail.value?.workflow.availableActions.find((item) => item.code === action.value),
)
const attachments = ref<AttachmentItem[]>([])
let generation = 0
function apply(value: TicketDetail) {
  detail.value = value
  assignment.value = value.ticket.assignedToUserId ?? ''
  priority.value = Number(
    Object.keys(priorities).find((key) => priorities[Number(key)] === value.ticket.priority) ?? 2,
  )
  action.value = value.workflow.availableActions[0]?.code ?? ''
  comment.value = ''
}
async function report(cause: unknown) {
  if (cause instanceof ApiError && (cause.status === 403 || cause.status === 404))
    detail.value = null
  error.value = cause instanceof Error ? cause.message : 'Unable to complete the request.'
  await nextTick()
  errorBox.value?.focus()
}
async function load() {
  const version = ++generation
  loading.value = true
  error.value = ''
  try {
    const result = await request<TicketDetail>(`/tickets/${route.params.ticketId}`)
    const files = await request<AttachmentItem[]>(`/tickets/${route.params.ticketId}/attachments`)
    const people = admin.value
      ? await request<{ id: number; fullName: string }[]>('/tickets/assignees')
      : []
    if (version === generation) {
      apply(result)
      attachments.value = files
      assignees.value = people
    }
  } catch (cause) {
    if (version === generation) {
      detail.value = null
      await report(cause)
    }
  } finally {
    if (version === generation) loading.value = false
  }
}
async function save(kind: 'assignment' | 'priority') {
  if (!detail.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    const body =
      kind === 'assignment'
        ? { assignedToUserId: assignment.value, rowVersion: detail.value.rowVersion }
        : { priority: priority.value, rowVersion: detail.value.rowVersion }
    apply(await send<TicketDetail>(`/tickets/${detail.value.ticket.id}/${kind}`, 'PUT', body))
    notice.value = kind === 'assignment' ? 'Assignment updated.' : 'Priority updated.'
  } catch (cause) {
    await report(cause)
  } finally {
    busy.value = false
  }
}
async function execute() {
  if (!detail.value || !selectedAction.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    await send(`/tickets/${detail.value.ticket.id}/workflow/actions`, 'POST', {
      actionCode: action.value,
      rowVersion: detail.value.workflow.rowVersion,
      comment: comment.value || null,
    })
    notice.value = 'Ticket action completed.'
    await load()
  } catch (cause) {
    await report(cause)
  } finally {
    busy.value = false
  }
}
watch(() => route.params.ticketId, load, { immediate: true })
watch(action, () => {
  if (selectedAction.value?.requiresComment) void nextTick(() => commentInput.value?.focus())
})
</script>

<template>
  <RouterLink
    class="back-link"
    to="/tickets"
    >← Back to tickets</RouterLink
  >
  <div
    v-if="error"
    ref="errorBox"
    class="error"
    role="alert"
    tabindex="-1"
  >
    {{ error }}
    <button
      :disabled="loading || busy"
      @click="load"
    >
      Refresh ticket
    </button>
  </div>
  <p
    v-if="notice"
    class="notice"
    role="status"
  >
    {{ notice }}
  </p>
  <p
    v-if="loading"
    class="card"
    role="status"
  >
    Loading ticket…
  </p>
  <template v-else-if="detail">
    <section class="page-heading">
      <p class="eyebrow">{{ detail.ticket.ticketNumber }}</p>
      <h1>{{ detail.ticket.title }}</h1>
      <div class="ticket-badges">
        <span :class="['status-badge', `status-${detail.ticket.status}`]">{{
          statuses[detail.ticket.status]
        }}</span
        ><span :class="['priority-badge', `priority-${detail.ticket.priority}`]"
          >{{ detail.ticket.priority }} priority</span
        >
      </div>
    </section>
    <div class="ticket-detail-grid">
      <div class="ticket-main-column">
        <section class="card">
          <h2>Issue details</h2>
          <p class="ticket-description">{{ detail.description }}</p>
          <dl class="ticket-properties">
            <div>
              <dt>Category</dt>
              <dd>{{ detail.ticket.category }}</dd>
            </div>
            <div>
              <dt>Requested by</dt>
              <dd>{{ detail.ticket.requesterName }}</dd>
            </div>
            <div>
              <dt>Submitted</dt>
              <dd>
                <time :datetime="detail.ticket.createdAtUtc">{{
                  formatTicketDate(detail.ticket.createdAtUtc)
                }}</time>
              </dd>
            </div>
            <div>
              <dt>Assigned to</dt>
              <dd>{{ detail.ticket.assigneeName ?? 'Unassigned' }}</dd>
            </div>
          </dl>
          <AttachmentList
            :ticket-id="detail.ticket.id"
            :files="attachments"
          />
        </section>
        <section
          v-if="detail.workflow.availableActions.length"
          class="card"
          aria-labelledby="action-title"
        >
          <h2 id="action-title">Next action</h2>
          <form @submit.prevent="execute">
            <label for="workflow-action">Action</label
            ><select
              id="workflow-action"
              v-model="action"
              :disabled="busy"
            >
              <option
                v-for="item in detail.workflow.availableActions"
                :key="item.code"
                :value="item.code"
              >
                {{ item.name }}
              </option></select
            ><label for="action-comment"
              >Comment {{ selectedAction?.requiresComment ? '(required)' : '(optional)' }}</label
            ><textarea
              id="action-comment"
              :disabled="busy"
              ref="commentInput"
              v-model.trim="comment"
              rows="3"
              maxlength="4000"
              :required="selectedAction?.requiresComment"
            ></textarea>
            <div class="actions">
              <button
                class="primary"
                :disabled="busy"
              >
                {{ busy ? 'Saving…' : (selectedAction?.name ?? 'Confirm action') }}
              </button>
            </div>
          </form>
        </section>
        <section
          v-else
          class="card"
        >
          <h2>
            {{
              detail.ticket.status === 'CLOSED'
                ? 'This ticket is closed'
                : 'Waiting for the next step'
            }}
          </h2>
          <p class="muted">
            {{
              detail.ticket.status === 'CLOSED'
                ? 'If you need more help, submit a new ticket.'
                : detail.ticket.status === 'RESOLVED'
                  ? 'The employee who submitted this ticket can close it or reopen it.'
                  : detail.ticket.assigneeName
                    ? 'The assigned IT Staff member handles the current processing step.'
                    : 'IT Support will assign a staff member to this ticket.'
            }}
          </p>
          <RouterLink
            v-if="detail.ticket.status === 'CLOSED' && session?.user.role === 'EMPLOYEE'"
            class="button"
            to="/tickets/new"
            >Submit a new ticket</RouterLink
          >
        </section>
        <TicketTimeline :detail="detail" />
      </div>
      <aside
        class="ticket-side-column"
        aria-label="Ticket management"
      >
        <section
          v-if="admin"
          class="card"
        >
          <h2>Assignment</h2>
          <form @submit.prevent="save('assignment')">
            <label for="assignee">Support IT Staff</label
            ><select
              id="assignee"
              v-model.number="assignment"
              :disabled="busy"
              required
            >
              <option
                disabled
                value=""
              >
                Select a staff member
              </option>
              <option
                v-if="
                  detail.ticket.assignedToUserId &&
                  !assignees.some((person) => person.id === detail?.ticket.assignedToUserId)
                "
                :value="detail.ticket.assignedToUserId"
                disabled
              >
                {{ detail.ticket.assigneeName }} (unavailable)
              </option>
              <option
                v-for="person in assignees"
                :key="person.id"
                :value="person.id"
              >
                {{ person.fullName }}
              </option>
            </select>
            <p class="form-help">Assignment keeps the current workflow status.</p>
            <button :disabled="busy || !assignees.length">
              {{ busy ? 'Saving…' : 'Save assignment' }}
            </button>
          </form>
          <hr class="section-divider" />
          <form @submit.prevent="save('priority')">
            <label for="priority">Priority</label
            ><select
              id="priority"
              v-model.number="priority"
              :disabled="busy"
            >
              <option
                v-for="(label, value) in priorities"
                :key="value"
                :value="Number(value)"
              >
                {{ label }}
              </option></select
            ><button
              class="priority-save"
              :disabled="busy"
            >
              {{ busy ? 'Saving…' : 'Save priority' }}
            </button>
          </form>
        </section>
      </aside>
    </div>
  </template>
</template>
