<script setup lang="ts">
import { ref, useId } from 'vue'
import { attachmentAccept, fileSize } from '../services/tickets'
const files = defineModel<File[]>({ required: true })
defineProps<{ disabled?: boolean }>()
const id = useId(), error = ref('')
function select(event: Event) {
  const input = event.target as HTMLInputElement
  const selected = [...files.value, ...Array.from(input.files ?? [])]
  input.value = ''; error.value = ''
  if (selected.length > 5) error.value = 'Select at most 5 files per submission.'
  else if (selected.some(file => !file.size || file.size > 10 * 1024 * 1024)) error.value = 'Each file must be nonempty and no larger than 10 MB.'
  else if (selected.some(file => !attachmentAccept.split(',').includes('.' + file.name.split('.').pop()?.toLowerCase()))) error.value = 'Choose a supported image, PDF, Word, Excel, TXT or LOG file.'
  else files.value = selected
}
</script>
<template>
  <div class="attachment-picker">
    <label :for="id">Attachments (optional)</label>
    <p :id="`${id}-help`" class="form-help">Up to 5 files, 10 MB each. Images (PNG, JPG, GIF, WEBP, BMP), PDF, Word, Excel, TXT and LOG.</p>
    <input :id="id" type="file" multiple :accept="attachmentAccept" :disabled="disabled" :aria-describedby="`${id}-help ${id}-error`" @change="select">
    <p :id="`${id}-error`" class="file-error" role="alert">{{ error }}</p>
    <ul v-if="files.length" class="attachment-list" aria-label="Selected attachments">
      <li v-for="(file, index) in files" :key="index"><span>{{ file.name }} <small>{{ fileSize(file.size) }}</small></span><button type="button" :disabled="disabled" :aria-label="`Remove ${file.name}`" @click="files.splice(index, 1)">Remove</button></li>
    </ul>
    <p class="form-help" aria-live="polite">{{ files.length }} of 5 files selected. Attachments cannot be changed after submitting the ticket.</p>
  </div>
</template>
