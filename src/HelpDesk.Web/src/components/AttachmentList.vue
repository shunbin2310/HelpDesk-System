<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { download } from '../services/api'
import { fileSize, type AttachmentItem } from '../services/tickets'
const props = defineProps<{ ticketId: number; files: AttachmentItem[] }>()
const busy = ref<number | null>(null),
  error = ref(''),
  preview = ref<AttachmentItem | null>(null),
  url = ref('')
const dialog = ref<HTMLDialogElement>()
function clear() {
  if (url.value) URL.revokeObjectURL(url.value)
  url.value = ''
  preview.value = null
}
async function open(file: AttachmentItem, view = false) {
  busy.value = file.id
  error.value = ''
  try {
    const blob = await download(`/tickets/${props.ticketId}/attachments/${file.id}/download`)
    const objectUrl = URL.createObjectURL(blob)
    if (view) {
      clear()
      preview.value = file
      url.value = objectUrl
      dialog.value?.showModal()
    } else {
      const link = document.createElement('a')
      link.href = objectUrl
      link.download = file.fileName
      document.body.append(link)
      link.click()
      link.remove()
      // Browsers need time to consume the download URL before it is released.
      const timer = window.setTimeout(() => {
        URL.revokeObjectURL(objectUrl)
        pending.delete(timer)
      }, 60000)
      pending.set(timer, objectUrl)
    }
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : 'Unable to download attachment.'
  } finally {
    busy.value = null
  }
}
const pending = new Map<number, string>()
onBeforeUnmount(() => {
  clear()
  for (const [timer, value] of pending) {
    clearTimeout(timer)
    URL.revokeObjectURL(value)
  }
})
</script>
<template>
  <ul
    v-if="files.length"
    class="attachment-list"
    aria-label="Attachments"
    :aria-busy="busy !== null"
  >
    <li
      v-for="file in files"
      :key="file.id"
    >
      <span
        >{{ file.fileName }} <small>{{ fileSize(file.sizeBytes) }}</small></span
      >
      <div class="file-actions">
        <button
          v-if="file.contentType.startsWith('image/') || file.contentType === 'application/pdf'"
          type="button"
          :disabled="busy !== null"
          :aria-label="`View ${file.fileName}`"
          @click="open(file, true)"
        >
          View</button
        ><button
          type="button"
          :disabled="busy !== null"
          :aria-label="`Download ${file.fileName}`"
          @click="open(file)"
        >
          {{ busy === file.id ? 'Loading…' : 'Download' }}
        </button>
      </div>
    </li>
  </ul>
  <p
    v-if="error"
    class="file-error"
    role="alert"
  >
    {{ error }}
  </p>
  <dialog
    ref="dialog"
    class="attachment-preview"
    aria-label="Attachment preview"
    @close="clear"
  >
    <template v-if="preview"
      ><h2>{{ preview.fileName }}</h2>
      <img
        v-if="preview.contentType.startsWith('image/')"
        :src="url"
        :alt="preview.fileName"
      />
      <p v-else>Open this PDF in your browser to view it.</p>
      <div class="actions">
        <a
          v-if="preview.contentType === 'application/pdf'"
          class="button primary"
          :href="url"
          target="_blank"
          rel="noopener noreferrer"
          >Open PDF</a
        ><a
          class="button"
          :href="url"
          :download="preview.fileName"
          >Download</a
        ><button
          type="button"
          autofocus
          @click="dialog?.close()"
        >
          Close preview
        </button>
      </div></template
    >
  </dialog>
</template>
