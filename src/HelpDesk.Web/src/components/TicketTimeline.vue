<script setup lang="ts">
import { computed } from 'vue'
import { formatTicketDate, type TicketDetail } from '../services/tickets'
const props = defineProps<{ detail: TicketDetail }>()
const labels: Record<string, string> = {
  START: 'Ticket submitted',
  START_PROCESSING: 'Processing started',
  RESOLVE: 'Resolved',
  CLOSE: 'Closed',
  REOPEN: 'Reopened',
}
const entries = computed(() =>
  [
    ...props.detail.workflow.history.map((item, index) => ({
      key: `workflow-${index}`,
      title: labels[item.actionCode] ?? item.actionCode,
      actor: item.actorName,
      date: item.actionAtUtc,
      body: item.comment,
    })),
    ...props.detail.managementHistory.map((item) => ({
      key: `management-${item.id}`,
      title:
        item.eventType === 'Delegated'
          ? `Delegated: ${item.oldAssigneeName ?? 'Unassigned'} → ${item.newAssigneeName}`
          : item.eventType === 'DelegationReturned'
            ? `Delegation returned: ${item.oldAssigneeName} → ${item.newAssigneeName}`
            : item.eventType === 'PriorityChanged'
              ? `Priority: ${item.oldPriority} → ${item.newPriority}`
              : `Assignment: ${item.oldAssigneeName ?? 'Unassigned'} → ${item.newAssigneeName}`,
      actor: item.actorName,
      date: item.createdAtUtc,
      body: item.comment,
    })),
  ].sort((a, b) => Date.parse(a.date) - Date.parse(b.date)),
)
</script>
<template>
  <section class="card">
    <h2>Activity</h2>
    <p class="form-help">Workflow actions, assignment and priority changes, in order.</p>
    <ol class="ticket-history activity-timeline">
      <li
        v-for="entry in entries"
        :key="entry.key"
      >
        <strong>{{ entry.title }}</strong>
        <p>
          {{ entry.actor }} · <time :datetime="entry.date">{{ formatTicketDate(entry.date) }}</time>
        </p>
        <p
          v-if="entry.body"
          class="history-comment"
        >
          {{ entry.body }}
        </p>
      </li>
    </ol>
  </section>
</template>
