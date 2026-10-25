<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { useRouter } from 'vue-router'
import { request } from '../services/api'
import AttachmentPicker from '../components/AttachmentPicker.vue'
import { categories, type TicketDetail } from '../services/tickets'
const router = useRouter()
const title = ref(''),
  description = ref(''),
  category = ref<number | ''>(''),
  busy = ref(false),
  error = ref('')
const errorBox = ref<HTMLElement>()
const files = ref<File[]>([])
async function submit() {
  busy.value = true
  error.value = ''
  try {
    const form = new FormData()
    form.set('title', title.value)
    form.set('description', description.value)
    form.set('category', String(category.value))
    files.value.forEach((file) => form.append('files', file))
    const result = await request<TicketDetail>('/tickets', { method: 'POST', body: form })
    await router.replace(`/tickets/${result.ticket.id}`)
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : 'Unable to submit ticket.'
    await nextTick()
    errorBox.value?.focus()
  } finally {
    busy.value = false
  }
}
</script>
<template>
  <section class="page-heading">
    <p class="eyebrow">IT SUPPORT</p>
    <h1>Submit a ticket</h1>
    <p class="muted">Tell us what happened so the IT team can help.</p>
  </section>
  <form
    class="card ticket-create"
    @submit.prevent="submit"
  >
    <div
      v-if="error"
      ref="errorBox"
      class="error"
      role="alert"
      tabindex="-1"
    >
      {{ error }}
    </div>
    <label for="ticket-title">Title</label
    ><input
      id="ticket-title"
      v-model.trim="title"
      required
      maxlength="200"
      placeholder="A short description of the issue"
    />
    <label for="ticket-category">Category</label
    ><select
      id="ticket-category"
      v-model.number="category"
      required
    >
      <option
        disabled
        value=""
      >
        Select a category
      </option>
      <option
        v-for="(label, value) in categories"
        :key="value"
        :value="Number(value)"
      >
        {{ label }}
      </option>
    </select>
    <label for="ticket-description">Description</label
    ><textarea
      id="ticket-description"
      v-model.trim="description"
      required
      maxlength="4000"
      rows="7"
      aria-describedby="description-help"
      placeholder="What were you doing? What happened, and what have you tried?"
    ></textarea>
    <p
      id="description-help"
      class="form-help"
    >
      Include the details needed to investigate. Up to 4000 characters.
    </p>
    <AttachmentPicker
      v-model="files"
      :disabled="busy"
    />
    <p class="form-help">
      Your ticket starts as Open with Medium priority. IT Support will assign a staff member.
    </p>
    <div class="actions">
      <button
        class="primary"
        :disabled="busy"
      >
        {{ busy ? 'Submitting…' : 'Submit ticket' }}</button
      ><RouterLink
        class="button"
        to="/tickets"
        >Cancel</RouterLink
      >
    </div>
  </form>
</template>
