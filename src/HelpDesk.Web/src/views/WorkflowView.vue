<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'
import { ApiError, request, send } from '../services/api'

interface WorkflowTemplate {
  id: number
  code: string
  name: string
  version: number
  steps: {
    code: string
    name: string
    displayOrder: number
    isInitial: boolean
    isTerminal: boolean
  }[]
  actions: {
    code: string
    name: string
    fromStepCode: string
    toStepCode: string
    requiresComment: boolean
    actioners: string[]
  }[]
}
const template = ref<WorkflowTemplate | null>(null)
const loading = ref(true),
  busy = ref(false),
  error = ref(''),
  notice = ref('')
const errorBox = ref<HTMLElement>()
const actorLabels: Record<string, string> = {
  Requester: 'Employee requester',
  CurrentAssignee: 'Current assigned IT Staff',
  Role: 'Configured role',
  User: 'Configured user',
}
function stepName(code: string) {
  return template.value?.steps.find((step) => step.code === code)?.name ?? code
}
async function report(cause: unknown) {
  error.value = cause instanceof Error ? cause.message : 'The workflow could not be loaded.'
  await nextTick()
  errorBox.value?.focus()
}
async function load() {
  loading.value = true
  error.value = ''
  try {
    template.value = await request<WorkflowTemplate>('/workflow-templates/active')
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 404) template.value = null
    else await report(cause)
  } finally {
    loading.value = false
  }
}
async function initialize() {
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    template.value = await send<WorkflowTemplate>('/workflow-templates/default', 'POST', {})
    notice.value = 'Ticket workflow is ready. New tickets will use this template.'
  } catch (cause) {
    await report(cause)
  } finally {
    busy.value = false
  }
}
onMounted(load)
</script>

<template>
  <section class="page-heading">
    <p class="eyebrow">SYSTEM ADMINISTRATION</p>
    <h1>Ticket workflow</h1>
    <p class="muted">The process behind every support ticket.</p>
  </section>
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
      Try again
    </button>
  </div>
  <p
    v-if="notice"
    class="notice"
    role="status"
  >
    {{ notice }}
  </p>
  <section
    v-if="loading"
    class="card"
    role="status"
  >
    Loading workflow…
  </section>
  <section
    v-else-if="!template && !error"
    class="card workflow-empty"
    aria-labelledby="setup-title"
  >
    <span
      class="section-number"
      aria-hidden="true"
      >01</span
    >
    <div>
      <h2 id="setup-title">Set up your Ticket workflow</h2>
      <p class="muted">
        Initialize and publish the default four-step process before employees start submitting
        tickets.
      </p>
      <p class="form-help">
        The setup creates version 1 once. Any existing active template is preserved.
      </p>
      <button
        class="primary"
        :disabled="busy"
        @click="initialize"
      >
        {{ busy ? 'Initializing…' : 'Initialize default workflow' }}
      </button>
    </div>
  </section>
  <template v-else-if="template">
    <section
      class="card"
      aria-labelledby="template-title"
    >
      <div class="heading-row">
        <div>
          <p class="eyebrow">ACTIVE TEMPLATE</p>
          <h2 id="template-title">{{ template.name }}</h2>
        </div>
        <span class="role-badge">Version {{ template.version }} · Published</span>
      </div>
      <ol
        class="workflow-steps"
        aria-label="Workflow steps"
      >
        <li
          v-for="(step, index) in template.steps"
          :key="step.code"
        >
          <span
            class="step-index"
            aria-hidden="true"
            >{{ String(index + 1).padStart(2, '0') }}</span
          ><strong>{{ step.name }}</strong
          ><span class="form-help">{{
            step.isInitial ? 'Initial step' : step.isTerminal ? 'Final step' : 'Active workflow'
          }}</span>
        </li>
      </ol>
      <p class="form-help">
        Resolved tickets can reopen to In Progress. Closed tickets require a new ticket.
      </p>
    </section>
    <section
      class="card workflow-actions"
      aria-labelledby="actions-title"
    >
      <h2 id="actions-title">Actions and responsibility</h2>
      <p class="muted">
        Permissions follow the current requester and assignment. System Admin can perform available
        actions without skipping state rules.
      </p>
      <ul class="workflow-action-list">
        <li
          v-for="action in template.actions"
          :key="action.code"
        >
          <div>
            <h3>{{ action.name }}</h3>
            <p>
              {{ stepName(action.fromStepCode) }} <span aria-hidden="true">→</span
              ><span class="sr-only"> to </span> {{ stepName(action.toStepCode) }}
            </p>
          </div>
          <div>
            <strong>{{
              action.actioners.map((actor) => actorLabels[actor] ?? actor).join(' or ')
            }}</strong>
            <p>{{ action.requiresComment ? 'Comment required' : 'Comment optional' }}</p>
          </div>
        </li>
      </ul>
    </section>
    <p class="form-help">
      Each ticket keeps a copy of its template version. Later template changes do not change
      existing tickets.
    </p>
  </template>
</template>
